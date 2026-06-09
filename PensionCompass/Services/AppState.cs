using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PensionCompass.Core.History;
using PensionCompass.Core.Models;
using PensionCompass.Core.Sync;
using PensionCompass.Core.Sync.Google;
using Windows.Storage;

namespace PensionCompass.Services;

/// <summary>
/// Process-wide state shared across the five screens — a poor-man's DI container.
/// On first access, restores the last-saved Account + Catalog from disk via <see cref="StateStore"/>;
/// catalog assignments auto-persist via the source-generated <c>OnCatalogChanged</c> hook,
/// account mutations require an explicit <see cref="SaveAccount"/> call from the VM that touched them.
///
/// The active <see cref="ISyncProvider"/> is mutable: when the user switches sync mode in Settings
/// we rebuild it in place, and StateStore picks up the new instance because it holds a delegate.
/// </summary>
public sealed partial class AppState : ObservableObject
{
    public static AppState Instance { get; } = new();

    [ObservableProperty]
    private ProductCatalog? _catalog;

    public AccountStatusModel Account { get; private set; } = new();

    public SettingsService Settings { get; } = new();

    private readonly StateStore _store;
    private ISyncProvider _activeSyncProvider = NoopSyncProvider.Instance;
    private readonly ApiKeySyncService _apiKeySync = new();

    /// <summary>Notifies the Settings UI to refresh the connection status block after the
    /// active provider switches (Google connect / disconnect / mode change).</summary>
    public event EventHandler? SyncProviderChanged;

    /// <summary>
    /// State of the encrypted cloud API-key bundle (v1.3.0). Drives the Settings passphrase UI:
    /// whether to show "locked — enter passphrase", "set a passphrase", or "✓ encrypted & synced".
    /// </summary>
    public enum CloudKeyStatus
    {
        /// <summary>Not connected, or no cloud key bundle exists yet.</summary>
        None,
        /// <summary>Cloud keys are decrypted (encrypted-at-rest) and loaded into the cache.</summary>
        Loaded,
        /// <summary>Keys were loaded from a LEGACY PLAINTEXT cloud bundle (v1.2.0). The cloud copy is NOT
        /// yet encrypted — the user must set a passphrase to secure it. Distinct from <see cref="Loaded"/>
        /// so the UI never falsely claims the cloud copy is encrypted.</summary>
        LoadedPlaintext,
        /// <summary>An encrypted bundle exists but this device has no/incorrect passphrase yet — prompt the user.</summary>
        NeedsPassphrase,
        /// <summary>The stored passphrase did not decrypt the cloud bundle (changed on another PC?).</summary>
        WrongPassphrase,
        /// <summary>The cloud bundle is structurally broken — advise reset.</summary>
        Corrupt,
    }

    /// <summary>Result of <see cref="ApplyCloudKeyPassphraseAsync"/>.</summary>
    public enum PassphraseApplyResult
    {
        /// <summary>Passphrase set and current keys encrypted+uploaded (first-time set / legacy upgrade / corrupt overwrite).</summary>
        Set,
        /// <summary>Existing encrypted bundle decrypted and loaded (new-PC unlock).</summary>
        Unlocked,
        /// <summary>Passphrase did not decrypt the existing encrypted bundle.</summary>
        WrongPassphrase,
        /// <summary>Cloud file is broken and there are no local keys to recreate it from — user must enter keys first.</summary>
        CorruptNoLocalKeys,
        /// <summary>A cloud file exists but couldn't be read (offline / token / 5xx). Aborted without writing, to avoid clobbering it.</summary>
        TransientError,
        /// <summary>Google isn't connected.</summary>
        NotConnected,
    }

    private CloudKeyStatus _cloudKeyStatus = CloudKeyStatus.None;

    /// <summary>Current cloud-key encryption state for the Settings UI to bind against.</summary>
    public CloudKeyStatus CloudKeys => _cloudKeyStatus;

    /// <summary>Raised whenever <see cref="CloudKeys"/> changes (may fire on a background thread —
    /// subscribers must marshal to the UI thread before touching XAML).</summary>
    public event EventHandler? CloudKeyStatusChanged;

    private void SetCloudKeyStatus(CloudKeyStatus status)
    {
        _cloudKeyStatus = status;
        CloudKeyStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private AppState()
    {
        // StateStore reads through a supplier so swapping providers at runtime takes effect on
        // the next save/load without rebuilding the store.
        _store = new StateStore(() => _activeSyncProvider);
        _activeSyncProvider = BuildProviderForCurrentSettings();

        if (_store.LoadAccount() is { } persistedAccount)
            Account = persistedAccount;

        if (_store.LoadCatalog() is { } persistedCatalog)
            _catalog = persistedCatalog; // backing field directly to avoid re-saving during load

        MigrateLifelongAnnuityFromSettings();

        // v1.2.0 API-key Google integration:
        // - Subscribe to UI-driven key changes so we can push them to Drive.
        // - If a Google session is already alive from a previous launch, refresh the cloud-cache
        //   slots in the background so cross-PC edits flow through without a manual re-connect.
        Settings.ApiKeysChanged += OnApiKeysChangedFromUi;
        if (Settings.IsGoogleSourceActive)
            _ = Task.Run(RefreshCloudApiKeysFromDriveInBackground);
    }

    public ISyncProvider ActiveSyncProvider => _activeSyncProvider;

    /// <summary>Exposes the configured sync folder root (or null when not set) for components
    /// that need to write history files alongside the state mirror. Only meaningful in
    /// <see cref="SyncMode.FilesystemFolder"/> mode — Google Drive history goes through the
    /// provider, not the filesystem.</summary>
    public string? SyncFolderRoot
        => Settings.SyncMode == SyncMode.FilesystemFolder && !string.IsNullOrWhiteSpace(Settings.SyncFolder)
            ? Settings.SyncFolder
            : null;

    /// <summary>
    /// One-shot hand-off slot used by the History → AI Rebalance flow.
    /// </summary>
    public RebalanceSessionEntry? PendingPriorEntry { get; set; }

    public RebalanceSessionEntry? ConsumePendingPriorEntry()
    {
        var entry = PendingPriorEntry;
        PendingPriorEntry = null;
        return entry;
    }

    /// <summary>
    /// The folder we WRITE rebalance history sessions into: <c>&lt;syncFolder&gt;\History</c> in
    /// FilesystemFolder mode, otherwise <c>&lt;LocalState&gt;\History</c>. (In Google Drive mode
    /// the folder filesystem path is still LocalState — the cloud mirror happens via the
    /// state store's provider on save.)
    /// </summary>
    public string ActiveHistoryFolder
        => Path.Combine(SyncFolderRoot ?? ApplicationData.Current.LocalFolder.Path, RebalanceHistoryStore.HistoryFolderName);

    public IEnumerable<string> CandidateHistoryRoots()
    {
        yield return ApplicationData.Current.LocalFolder.Path;
        if (SyncFolderRoot is { } sync) yield return sync;
    }

    private void MigrateLifelongAnnuityFromSettings()
    {
        if (Settings.ReadLegacyLifelongAnnuityFlag() is not { } legacy) return;
        Account.WantsLifelongAnnuity = legacy;
        _store.SaveAccount(Account);
        Settings.ClearLegacyLifelongAnnuityFlag();
    }

    public void SaveAccount() => _store.SaveAccount(Account);

    public void ResetAccount()
    {
        Account = new AccountStatusModel();
        _store.DeleteAccount();
    }

    public void ResetCatalog()
    {
        Catalog = null; // triggers OnCatalogChanged → DeleteCatalog
    }

    partial void OnCatalogChanged(ProductCatalog? value)
    {
        if (value == null)
            _store.DeleteCatalog();
        else
            _store.SaveCatalog(value);
    }

    // ──────── Sync mode switching ────────

    /// <summary>
    /// Builds the provider that matches whatever <see cref="SyncMode"/> is currently in settings.
    /// For Google Drive mode this returns a provider whose OAuth flow has NOT yet been triggered —
    /// the next read/write will lazy-init via <c>GoogleWebAuthorizationBroker</c>. If tokens were
    /// previously stored in the vault, that lazy init runs silently (no browser pop-up).
    /// </summary>
    private ISyncProvider BuildProviderForCurrentSettings() => Settings.SyncMode switch
    {
        SyncMode.FilesystemFolder => new FilesystemFolderSyncProvider(() => Settings.SyncFolder),
        SyncMode.GoogleDrive => new GoogleDriveSyncProvider(
            GoogleOAuthCredentials.ClientId,
            GoogleOAuthCredentials.ClientSecret,
            new GoogleOAuthDataStore()),
        _ => NoopSyncProvider.Instance,
    };

    /// <summary>
    /// Switches sync to "off" — LocalState only. Doesn't touch any cloud data; if the user wants
    /// to clean up Drive they revoke the OAuth grant in their Google Account settings.
    /// </summary>
    public void SwitchToNoneMode()
    {
        DisposeProviderIfNeeded(_activeSyncProvider);
        _activeSyncProvider = NoopSyncProvider.Instance;
        Settings.SyncMode = SyncMode.None;
        SyncProviderChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Switches to filesystem-folder mode (legacy v1.0.x style). The <paramref name="folderPath"/>
    /// is also written to <see cref="SettingsService.SyncFolder"/>; pass an empty string to clear.
    /// </summary>
    public void SwitchToFilesystemFolderMode(string folderPath)
    {
        Settings.SyncFolder = folderPath?.Trim() ?? string.Empty;
        DisposeProviderIfNeeded(_activeSyncProvider);
        _activeSyncProvider = string.IsNullOrWhiteSpace(Settings.SyncFolder)
            ? NoopSyncProvider.Instance
            : new FilesystemFolderSyncProvider(() => Settings.SyncFolder);
        Settings.SyncMode = string.IsNullOrWhiteSpace(Settings.SyncFolder)
            ? SyncMode.None
            : SyncMode.FilesystemFolder;
        SyncProviderChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Triggers Google Drive OAuth (browser pop-up if no cached tokens), and on success swaps in
    /// the new provider, persists <see cref="SyncMode.GoogleDrive"/>, captures the connected
    /// email for the UI, and runs the first-time migration (uploads existing local data to Drive
    /// if Drive is empty). Throws on OAuth failure so the caller can surface the error.
    /// </summary>
    public async Task ConnectGoogleDriveAsync(CancellationToken ct = default)
    {
        var newProvider = new GoogleDriveSyncProvider(
            GoogleOAuthCredentials.ClientId,
            GoogleOAuthCredentials.ClientSecret,
            new GoogleOAuthDataStore());

        // NOTE on threading: every top-level await below intentionally does NOT use
        // ConfigureAwait(false). When this method is invoked from a Settings button click the
        // caller's SynchronizationContext is the UI thread; using ConfigureAwait(false) on the
        // FIRST await would drop that context and the subsequent awaits would never marshal back
        // (subsequent `await` calls without ConfigureAwait capture *current* context, which by
        // then is null → continuation runs on threadpool). The final SyncProviderChanged event
        // fires INotifyPropertyChanged on subscribers, which WinUI binds to XAML — those updates
        // are UI-thread-affine, so we have to remain on UI context throughout. The per-call
        // performance cost of a few extra thread-hops is negligible (Drive API calls dominate).
        var drive = await newProvider.InitializeAsync(ct);

        // Capture connected user's email for the Settings UI status display.
        try
        {
            var aboutReq = drive.About.Get();
            aboutReq.Fields = "user(emailAddress)";
            var about = await aboutReq.ExecuteAsync(ct);
            Settings.GoogleDriveConnectedEmail = about.User?.EmailAddress ?? string.Empty;
        }
        catch
        {
            Settings.GoogleDriveConnectedEmail = string.Empty;
        }

        // Run first-time migration: if Drive appdata is empty, upload current local snapshots
        // (which the user may have populated under FilesystemFolder mode or just LocalState).
        await MigrateLocalToDriveAsync(newProvider, ct);

        // v1.2.0: pull any per-account API keys the user previously stored on this Google
        // account (from another PC, or a previous install). MUST run BEFORE SyncMode flips so
        // that when IsGoogleSourceActive becomes true a moment later the cache is already hot
        // and the UI shows the right values immediately instead of flashing empty boxes. If
        // Drive has no apikeys.json yet (fresh account), this is a no-op — user enters keys
        // manually and the OnApiKeysChangedFromUi handler uploads them (once a passphrase is set).
        // v1.3.0: the bundle is encrypted; if this PC has no passphrase yet, RefreshCloudKeyCache
        // sets CloudKeys = NeedsPassphrase and the Settings UI prompts the user to unlock.
        RefreshCloudKeyCache(newProvider);

        // Swap providers atomically. Old provider is disposed asynchronously to drain pending writes.
        var old = _activeSyncProvider;
        _activeSyncProvider = newProvider;
        Settings.SyncMode = SyncMode.GoogleDrive;
        DisposeProviderIfNeeded(old);
        SyncProviderChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Disconnects Google Drive: revokes nothing on Google's side (user must do that in Google
    /// Account settings if they want), but stops mirroring locally and switches back to None.
    /// LocalState is preserved so the user keeps their data.
    /// </summary>
    public void DisconnectGoogleDrive()
    {
        // Wipe THIS device's cloud-cache API key slots AND the sync passphrase, so a lost/stolen PC
        // retains neither the Drive-sourced keys nor the secret that could decrypt them. The
        // local-resource slots are untouched — that's the "offline mode" key set the user reverts to.
        //
        // NOTE: the encrypted apikeys.json on Drive is INTENTIONALLY left in place — other PCs on the
        // same Google account still rely on it, and on reconnect the user re-enters the passphrase once
        // to unlock. It's safe to leave because it's encrypted (a strong passphrase keeps it unreadable).
        // To purge the cloud copy entirely, the user revokes the app at myaccount.google.com/permissions.
        Settings.ClearAllCloudCachedKeys();
        Settings.ClearSyncPassphrase();
        Settings.GoogleDriveConnectedEmail = string.Empty;
        SetCloudKeyStatus(CloudKeyStatus.None);
        // Clear cached OAuth tokens so the next connect attempt re-prompts.
        try { _ = new GoogleOAuthDataStore().ClearAsync(); } catch { /* best-effort */ }
        SwitchToNoneMode();
    }

    /// <summary>
    /// Downloads + decrypts <c>apikeys.json</c> from the provider using the locally-stored passphrase
    /// (if any), writes the keys into the SettingsService cloud-cache vault slots, and updates
    /// <see cref="CloudKeys"/>. Used by the connect flow and the startup background refresh. Returns
    /// true only when keys were actually loaded into the cache.
    ///
    /// v1.3.0: if the cloud bundle is encrypted and this device has no/incorrect passphrase, the cache
    /// is NOT populated and <see cref="CloudKeys"/> becomes <see cref="CloudKeyStatus.NeedsPassphrase"/>
    /// (or <see cref="CloudKeyStatus.WrongPassphrase"/>) so the Settings UI can prompt for an unlock.
    /// A legacy plaintext bundle (written by v1.2.0) still loads, and is opportunistically re-encrypted
    /// on the spot when this device already has a passphrase.
    /// </summary>
    private bool RefreshCloudKeyCache(ISyncProvider provider)
    {
        var result = _apiKeySync.Download(provider, Settings.SyncPassphrase);
        switch (result.Status)
        {
            case ApiKeySyncService.DownloadStatus.Ok when result.WasEncrypted:
                ApplyBundleToCache(result.Bundle!);
                SetCloudKeyStatus(CloudKeyStatus.Loaded);
                return true;
            case ApiKeySyncService.DownloadStatus.Ok: // legacy PLAINTEXT bundle (not an encrypted envelope)
                // SECURITY: a device that already uses encryption must NOT ingest an unauthenticated
                // plaintext bundle — an attacker with write access to drive.appdata could plant one to
                // strip encryption or inject their own keys (the keys end up driving the user's AI calls).
                // Once we hold a passphrase the cloud copy should always be an encrypted envelope, so a
                // plaintext file here is anomalous → refuse it (the encrypted local cache keys are
                // untouched; the next key edit re-uploads an encrypted bundle).
                if (Settings.HasSyncPassphrase)
                {
                    SetCloudKeyStatus(CloudKeyStatus.Corrupt);
                    return false;
                }
                // Fresh device, no passphrase yet: this is the legitimate v1.2.0 → v1.3.0 upgrade path.
                // Load the keys so the user isn't locked out, but mark the cloud copy as NOT-yet-encrypted
                // so the UI prompts them to set a passphrase (re-encryption is user-initiated, never silent).
                ApplyBundleToCache(result.Bundle!);
                SetCloudKeyStatus(CloudKeyStatus.LoadedPlaintext);
                return true;
            case ApiKeySyncService.DownloadStatus.NotPresent:
                SetCloudKeyStatus(CloudKeyStatus.None);
                return false;
            case ApiKeySyncService.DownloadStatus.EncryptedNeedsPassphrase:
                SetCloudKeyStatus(CloudKeyStatus.NeedsPassphrase);
                return false;
            case ApiKeySyncService.DownloadStatus.WrongPassphrase:
                SetCloudKeyStatus(CloudKeyStatus.WrongPassphrase);
                return false;
            default:
                SetCloudKeyStatus(CloudKeyStatus.Corrupt);
                return false;
        }
    }

    private void ApplyBundleToCache(ApiKeySyncService.ApiKeyBundle bundle)
    {
        Settings.SetCloudCachedKey("Claude", bundle.Claude);
        Settings.SetCloudCachedKey("Gemini", bundle.Gemini);
        Settings.SetCloudCachedKey("GPT", bundle.Gpt);
    }

    /// <summary>Snapshot of the three cloud-cache key slots as a bundle.</summary>
    private ApiKeySyncService.ApiKeyBundle CurrentCloudCacheBundle()
        => new(
            Claude: Settings.ReadCloudCachedKey("Claude"),
            Gemini: Settings.ReadCloudCachedKey("Gemini"),
            Gpt: Settings.ReadCloudCachedKey("GPT"));

    /// <summary>Encrypts the current cloud-cache key slots with the stored passphrase and uploads.
    /// No-op (and safe) when no passphrase is set — that's how we keep keys local-only until the
    /// user opts into encrypted cloud sync. Best-effort; upload failures are swallowed.</summary>
    private void TryUploadCurrentCloudKeys(ISyncProvider provider)
    {
        if (!Settings.HasSyncPassphrase) return;
        try { _apiKeySync.Upload(provider, CurrentCloudCacheBundle(), Settings.SyncPassphrase); }
        catch { /* upload errors surface on the provider's LastErrorMessage if needed */ }
    }

    /// <summary>
    /// Background refresh kicked off from the constructor when the app launches into an
    /// already-connected Google session. Pulls the latest <c>apikeys.json</c> so cross-PC edits
    /// flow through (PC1 changes key → PC2 next launch sees the change), and signals UI via
    /// <see cref="SettingsService.NotifyApiKeysReloadedFromCloud"/>. Best-effort — network failure
    /// just leaves the existing cache values in place. CloudKeys is still updated so a launch into a
    /// passphrase-locked account surfaces the unlock prompt.
    /// </summary>
    private void RefreshCloudApiKeysFromDriveInBackground()
    {
        try
        {
            if (RefreshCloudKeyCache(_activeSyncProvider))
                Settings.NotifyApiKeysReloadedFromCloud();
        }
        catch { /* best-effort — offline / token expired etc. */ }
    }

    /// <summary>
    /// Handler for <see cref="SettingsService.ApiKeysChanged"/> — fires when the user edits a key
    /// via the Settings UI. In Google-connected mode WITH a passphrase set, we encrypt and push the
    /// full 3-key bundle up to Drive. Without a passphrase the keys stay in this PC's cloud-cache
    /// vault only (never uploaded as plaintext) until the user sets one. In any non-Google mode this
    /// is a no-op.
    /// </summary>
    private void OnApiKeysChangedFromUi(object? sender, EventArgs e)
    {
        if (!Settings.IsGoogleSourceActive) return;
        if (!Settings.HasSyncPassphrase) return;
        TryUploadCurrentCloudKeys(_activeSyncProvider);
    }

    /// <summary>
    /// Applies a passphrase entered in Settings. Two unified behaviors based on the current cloud state:
    /// <list type="bullet">
    /// <item><b>Unlock</b> — if an encrypted bundle exists and the passphrase decrypts it, the keys are
    /// loaded and the passphrase is stored locally (this is the new-PC flow).</item>
    /// <item><b>Set</b> — if no bundle exists yet, or the cloud copy is legacy plaintext, the passphrase
    /// is stored, the current keys are encrypted and uploaded (securing any plaintext copy).</item>
    /// </list>
    /// A wrong passphrase against an encrypted bundle is reported without storing anything.
    /// </summary>
    public async Task<PassphraseApplyResult> ApplyCloudKeyPassphraseAsync(string passphrase, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(passphrase)) return PassphraseApplyResult.WrongPassphrase;
        if (!Settings.IsGoogleSourceActive) return PassphraseApplyResult.NotConnected;

        var provider = _activeSyncProvider;
        return await Task.Run(() =>
        {
            var result = _apiKeySync.Download(provider, passphrase);
            switch (result.Status)
            {
                case ApiKeySyncService.DownloadStatus.Ok when result.WasEncrypted:
                    Settings.SetSyncPassphrase(passphrase);
                    ApplyBundleToCache(result.Bundle!);
                    SetCloudKeyStatus(CloudKeyStatus.Loaded);
                    Settings.NotifyApiKeysReloadedFromCloud();
                    return PassphraseApplyResult.Unlocked;

                case ApiKeySyncService.DownloadStatus.WrongPassphrase:
                    SetCloudKeyStatus(CloudKeyStatus.WrongPassphrase);
                    return PassphraseApplyResult.WrongPassphrase;

                case ApiKeySyncService.DownloadStatus.Corrupt:
                {
                    // The cloud file is structurally broken. Treat the passphrase entry as intent to
                    // overwrite it — but only if we actually hold keys locally to recreate it from;
                    // otherwise overwriting with an empty bundle would lose data, so ask the user to
                    // enter keys first (the UI message is driven by CorruptNoLocalKeys).
                    var localForCorrupt = CurrentCloudCacheBundle();
                    if (localForCorrupt.IsAllEmpty)
                    {
                        SetCloudKeyStatus(CloudKeyStatus.Corrupt);
                        return PassphraseApplyResult.CorruptNoLocalKeys;
                    }
                    Settings.SetSyncPassphrase(passphrase);
                    try { _apiKeySync.Upload(provider, localForCorrupt, passphrase); }
                    catch { /* best-effort; status still reflects local keys are protected */ }
                    SetCloudKeyStatus(CloudKeyStatus.Loaded);
                    Settings.NotifyApiKeysReloadedFromCloud();
                    return PassphraseApplyResult.Set;
                }

                default:
                    // NotPresent, or Ok-but-legacy-plaintext → set the passphrase and secure the keys.
                    // GUARD: NotPresent can also mean "read transiently failed" (the Drive provider
                    // swallows errors → null bytes). If a file actually exists, refuse to overwrite it
                    // with whatever's in the local cache — a network blip during unlock must not destroy
                    // the only cloud copy of the keys.
                    if (result.Status == ApiKeySyncService.DownloadStatus.NotPresent
                        && _apiKeySync.RemoteBundleExists(provider))
                    {
                        return PassphraseApplyResult.TransientError;
                    }
                    Settings.SetSyncPassphrase(passphrase);
                    if (result.Bundle is not null) ApplyBundleToCache(result.Bundle); // preserve legacy keys
                    TryUploadCurrentCloudKeys(provider);
                    SetCloudKeyStatus(CloudKeyStatus.Loaded);
                    Settings.NotifyApiKeysReloadedFromCloud();
                    return PassphraseApplyResult.Set;
            }
        }, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// First-time-migration: if the Drive appdata is empty, push up local <c>account.json</c>,
    /// <c>catalog.json</c>, and any <c>History/*.json</c> files. Idempotent — if Drive already
    /// has a copy of any of these, the local copy is NOT pushed (cloud is the truth).
    /// </summary>
    private async Task MigrateLocalToDriveAsync(GoogleDriveSyncProvider drive, CancellationToken ct)
    {
        var localFolder = ApplicationData.Current.LocalFolder.Path;
        var legacySyncFolder = Settings.SyncFolder; // may also have data from v1.0.x usage

        await TryUploadIfMissingAsync(drive, "account.json", ReadLocalBytes(localFolder, "account.json", legacySyncFolder)).ConfigureAwait(false);
        await TryUploadIfMissingAsync(drive, "catalog.json", ReadLocalBytes(localFolder, "catalog.json", legacySyncFolder)).ConfigureAwait(false);

        // History: enumerate local AND legacy-sync-folder History/, dedupe by name.
        var historyByName = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var root in new[] { localFolder, legacySyncFolder })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var historyDir = Path.Combine(root, RebalanceHistoryStore.HistoryFolderName);
            if (!Directory.Exists(historyDir)) continue;
            foreach (var path in Directory.EnumerateFiles(historyDir, "*.json"))
            {
                var name = Path.GetFileName(path);
                if (historyByName.ContainsKey(name)) continue;
                try { historyByName[name] = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false); }
                catch { /* skip unreadable files */ }
            }
        }
        foreach (var (name, bytes) in historyByName)
            await TryUploadIfMissingAsync(drive, $"History/{name}", bytes).ConfigureAwait(false);
    }

    private static async Task TryUploadIfMissingAsync(GoogleDriveSyncProvider drive, string fileName, byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return;
        // Drive provider's GetModifiedTime returns null when the file doesn't exist.
        if (drive.GetModifiedTime(fileName) is not null) return;
        drive.Write(fileName, bytes); // fire-and-forget; flush at end of migration
        // Wait briefly so the queued write actually starts before we move on.
        await Task.Delay(50).ConfigureAwait(false);
    }

    private static byte[]? ReadLocalBytes(string localFolder, string fileName, string? syncFolder)
    {
        // Pick whichever local copy is newer between LocalState and the legacy sync folder.
        var candidates = new List<string>();
        var localPath = Path.Combine(localFolder, fileName);
        if (File.Exists(localPath)) candidates.Add(localPath);
        if (!string.IsNullOrWhiteSpace(syncFolder))
        {
            var syncPath = Path.Combine(syncFolder, fileName);
            if (File.Exists(syncPath)) candidates.Add(syncPath);
        }
        if (candidates.Count == 0) return null;
        candidates.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
        try { return File.ReadAllBytes(candidates[0]); }
        catch { return null; }
    }

    private static void DisposeProviderIfNeeded(ISyncProvider provider)
    {
        if (provider is IAsyncDisposable adisp)
            _ = Task.Run(async () => { try { await adisp.DisposeAsync().ConfigureAwait(false); } catch { } });
        else if (provider is IDisposable disp)
        {
            try { disp.Dispose(); } catch { }
        }
    }
}
