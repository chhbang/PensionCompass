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

    /// <summary>Notifies the Settings UI to refresh the connection status block after the
    /// active provider switches (Google connect / disconnect / mode change).</summary>
    public event EventHandler? SyncProviderChanged;

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
        Settings.GoogleDriveConnectedEmail = string.Empty;
        // Clear cached OAuth tokens so the next connect attempt re-prompts.
        try { _ = new GoogleOAuthDataStore().ClearAsync(); } catch { /* best-effort */ }
        SwitchToNoneMode();
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
