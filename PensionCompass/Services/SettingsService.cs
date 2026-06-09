using PensionCompass.Core.Ai;
using Windows.Security.Credentials;
using Windows.Storage;

namespace PensionCompass.Services;

/// <summary>
/// Persists user-level settings across runs:
/// - AI provider choice ("Claude" / "Gemini" / "GPT")
/// - per-provider model id (LocalSettings) and API key (Windows credential vault)
/// - reasoning effort (Off / Low / Medium / High)
/// Subscriber info (age, annuity-start age, lifelong-annuity preference) lives on
/// <see cref="Core.Models.AccountStatusModel"/>, not here — see <see cref="AppState"/>'s migration.
///
/// Non-secret prefs use per-user packaged-app LocalSettings.
/// API keys go through <see cref="PasswordVault"/> so they're encrypted at rest by Windows
/// (user-credential-keyed). Builds prior to v1.0.4 stored plain text in LocalSettings; the
/// constructor sweeps any leftover plaintext entries into the vault on first launch.
///
/// v1.2.0 adds a SECOND vault resource ("PensionCompass.ApiKey.Cloud") that holds keys mirrored
/// to Google Drive (drive.appdata). The active store flips based on Google connection state —
/// see <see cref="IsGoogleSourceActive"/>. The two vault resources never cross-pollute: connecting
/// to Google never overwrites the offline keys; disconnecting wipes only the cloud-cache slots.
/// </summary>
public sealed class SettingsService
{
    private const string AiProviderKey = "AiProvider";
    private const string LegacyApiKeyKey = "ApiKey"; // single-key field used before per-provider keys
    private const string ClaudeApiKeyKey = "ClaudeApiKey"; // legacy plaintext slot — only used during migration sweep
    private const string GeminiApiKeyKey = "GeminiApiKey";
    private const string GptApiKeyKey = "GptApiKey";
    private const string LegacyRestrictToSamsungLifeKey = "RestrictToSamsungLifeForLifelongAnnuity"; // moved to AccountStatusModel.WantsLifelongAnnuity
    private const string ClaudeModelKey = "ClaudeModel";
    private const string GeminiModelKey = "GeminiModel";
    private const string GptModelKey = "GptModel";
    private const string ThinkingLevelKey = "ThinkingLevel";
    private const string SyncFolderKey = "SyncFolder";
    private const string SyncModeKey = "SyncMode";
    private const string GoogleDriveConnectedEmailKey = "GoogleDriveConnectedEmail";

    /// <summary>
    /// Two parallel vault resources for API keys — see [v1.2.0 plan]:
    /// <list type="bullet">
    /// <item><c>VaultResourceLocal</c>: offline mode keys — read/written when Google sync is OFF or disconnected. Never uploaded.</item>
    /// <item><c>VaultResourceCloud</c>: cached cloud keys — read/written when Google is connected. Mirrored to <c>apikeys.json</c> in drive.appdata.</item>
    /// </list>
    /// On Disconnect the Cloud slots are wiped so a stolen PC doesn't retain Drive-sourced keys after the user revokes elsewhere.
    /// </summary>
    private const string VaultResourceLocal = "PensionCompass.ApiKey";
    private const string VaultResourceCloud = "PensionCompass.ApiKey.Cloud";
    private const string VaultProviderClaude = "Claude";
    private const string VaultProviderGemini = "Gemini";
    private const string VaultProviderGpt = "GPT";

    /// <summary>
    /// v1.3.0 sync passphrase (local-only). Derives the key that encrypts <c>apikeys.json</c> in
    /// drive.appdata via <see cref="Core.Crypto.PassphraseCipher"/>. Lives ONLY in this device's vault
    /// — it is never uploaded — so a compromised Google account can't decrypt the cloud copy. Wiped on
    /// Disconnect alongside the cloud-cache key slots. Losing it means the cloud keys are unrecoverable
    /// by design; the recovery path is "re-enter keys + set a new passphrase".
    /// </summary>
    private const string VaultResourceSyncPassphrase = "PensionCompass.SyncPassphrase";
    private const string VaultPassphraseUser = "Passphrase";

    private readonly ApplicationDataContainer _store = ApplicationData.Current.LocalSettings;

    public SettingsService()
    {
        MigrateLegacyApiKey();
        MigratePlaintextApiKeysToVault();
    }

    public string AiProvider
    {
        get => _store.Values[AiProviderKey] as string ?? "Claude";
        set => _store.Values[AiProviderKey] = value;
    }

    public string ClaudeApiKey
    {
        get => ReadVault(ActiveResource, VaultProviderClaude);
        set { WriteVault(ActiveResource, VaultProviderClaude, value); RaiseApiKeysChanged(); }
    }

    public string GeminiApiKey
    {
        get => ReadVault(ActiveResource, VaultProviderGemini);
        set { WriteVault(ActiveResource, VaultProviderGemini, value); RaiseApiKeysChanged(); }
    }

    public string GptApiKey
    {
        get => ReadVault(ActiveResource, VaultProviderGpt);
        set { WriteVault(ActiveResource, VaultProviderGpt, value); RaiseApiKeysChanged(); }
    }

    /// <summary>
    /// True when API keys should be read from / written to the cloud-cache vault slots (which mirror
    /// Drive's <c>apikeys.json</c>). When false, the regular local-only slots are used. The Settings
    /// UI surfaces an explanatory caption so the user understands which set is currently live.
    /// </summary>
    public bool IsGoogleSourceActive
        => SyncMode == SyncMode.GoogleDrive
           && !string.IsNullOrEmpty(GoogleDriveConnectedEmail);

    private string ActiveResource => IsGoogleSourceActive ? VaultResourceCloud : VaultResourceLocal;

    /// <summary>
    /// Fires when the user changes an API key through the Settings UI (any of the <c>*ApiKey</c>
    /// setters above). AppState subscribes to this in Google-connected mode and pushes the new
    /// bundle up to Drive. Does NOT fire when the cache is populated from a Drive download — that
    /// path raises <see cref="ApiKeysReloadedFromCloud"/> instead, to avoid an upload/download loop.
    /// </summary>
    public event EventHandler? ApiKeysChanged;

    /// <summary>
    /// Fires when a background download from Drive has refreshed the cloud-cache vault slots.
    /// SettingsViewModel subscribes to push <c>PropertyChanged</c> at the View so PasswordBoxes
    /// re-read the new values. Only this event — not <see cref="ApiKeysChanged"/> — is raised on
    /// download, so AppState's UI-change handler stays silent and no upload is triggered.
    /// </summary>
    public event EventHandler? ApiKeysReloadedFromCloud;

    private void RaiseApiKeysChanged() => ApiKeysChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Called by AppState's background refresh path after writing to the Cloud slots.</summary>
    public void NotifyApiKeysReloadedFromCloud()
        => ApiKeysReloadedFromCloud?.Invoke(this, EventArgs.Empty);

    // ──────── Cloud cache direct access (bypasses ActiveResource routing) ────────
    // Used by AppState during connect (populate cache from Drive download) and disconnect (wipe).
    // Reading via the regular ClaudeApiKey/etc. accessors honors ActiveResource which may not match
    // the cache slot the caller actually wants to touch.

    public void SetCloudCachedKey(string provider, string value)
        => WriteVault(VaultResourceCloud, provider, value);

    public string ReadCloudCachedKey(string provider)
        => ReadVault(VaultResourceCloud, provider);

    public void ClearAllCloudCachedKeys()
    {
        WriteVault(VaultResourceCloud, VaultProviderClaude, string.Empty);
        WriteVault(VaultResourceCloud, VaultProviderGemini, string.Empty);
        WriteVault(VaultResourceCloud, VaultProviderGpt, string.Empty);
    }

    // ──────── Sync passphrase (v1.3.0 — local-only, encrypts the cloud key bundle) ────────

    /// <summary>True when a sync passphrase is stored on this device.</summary>
    public bool HasSyncPassphrase
        => !string.IsNullOrEmpty(ReadVault(VaultResourceSyncPassphrase, VaultPassphraseUser));

    /// <summary>The locally-stored sync passphrase, or empty if none. Used to encrypt/decrypt the
    /// cloud <c>apikeys.json</c>. Never serialized anywhere but the local credential vault.</summary>
    public string SyncPassphrase
        => ReadVault(VaultResourceSyncPassphrase, VaultPassphraseUser);

    public void SetSyncPassphrase(string passphrase)
        => WriteVault(VaultResourceSyncPassphrase, VaultPassphraseUser, passphrase ?? string.Empty);

    public void ClearSyncPassphrase()
        => WriteVault(VaultResourceSyncPassphrase, VaultPassphraseUser, string.Empty);

    /// <summary>
    /// One-shot accessor for AppState's migration of the lifelong-annuity flag from LocalSettings
    /// to <see cref="Core.Models.AccountStatusModel.WantsLifelongAnnuity"/>. Returns null if the
    /// legacy slot was never set; otherwise returns the stored bool. Always pair with
    /// <see cref="ClearLegacyLifelongAnnuityFlag"/> after a successful copy into the account.
    /// </summary>
    public bool? ReadLegacyLifelongAnnuityFlag()
        => _store.Values[LegacyRestrictToSamsungLifeKey] is bool b ? b : null;

    public void ClearLegacyLifelongAnnuityFlag()
        => _store.Values.Remove(LegacyRestrictToSamsungLifeKey);

    public string ClaudeModel
    {
        get => _store.Values[ClaudeModelKey] as string ?? AnthropicClient.DefaultModel;
        set => _store.Values[ClaudeModelKey] = value;
    }

    public string GeminiModel
    {
        get => _store.Values[GeminiModelKey] as string ?? GeminiClient.DefaultModel;
        set => _store.Values[GeminiModelKey] = value;
    }

    public string GptModel
    {
        get => _store.Values[GptModelKey] as string ?? OpenAiClient.DefaultModel;
        set => _store.Values[GptModelKey] = value;
    }

    public ThinkingLevel ThinkingLevel
    {
        get => _store.Values[ThinkingLevelKey] is string s
            && Enum.TryParse<ThinkingLevel>(s, out var lvl)
            ? lvl
            : ThinkingLevel.High;
        set => _store.Values[ThinkingLevelKey] = value.ToString();
    }

    /// <summary>
    /// Optional folder path on the user's PC where account/catalog state is mirrored and
    /// rebalance history sessions are saved. When the user points this at a folder backed
    /// by their cloud sync client (OneDrive / Google Drive desktop / Dropbox), state and
    /// history flow across PCs automatically. Empty string means "no sync, LocalState only".
    /// API keys are never written here regardless of this setting — they stay in PasswordVault.
    /// </summary>
    public string SyncFolder
    {
        get => _store.Values[SyncFolderKey] as string ?? string.Empty;
        set => _store.Values[SyncFolderKey] = value ?? string.Empty;
    }

    /// <summary>
    /// Which sync backend is currently active. Default <see cref="SyncMode.None"/> means
    /// LocalState only. Switching to <see cref="SyncMode.GoogleDrive"/> requires a successful
    /// OAuth connect first; the AppState orchestrates that and only flips this setting on success.
    /// </summary>
    public SyncMode SyncMode
    {
        get => _store.Values[SyncModeKey] is string s && Enum.TryParse<SyncMode>(s, out var mode)
            ? mode
            // v1.0.x users with a SyncFolder configured are upgraded to FilesystemFolder mode
            // implicitly so their existing setup keeps working.
            : (string.IsNullOrWhiteSpace(SyncFolder) ? SyncMode.None : SyncMode.FilesystemFolder);
        set => _store.Values[SyncModeKey] = value.ToString();
    }

    /// <summary>
    /// Email address of the Google account currently linked for Drive sync, or empty when not
    /// connected. Set by <see cref="AppState"/> after a successful OAuth connect; cleared on disconnect.
    /// Used purely for UI display ("✓ user@example.com 연결됨"); the OAuth tokens themselves live
    /// in <see cref="GoogleOAuthDataStore"/>.
    /// </summary>
    public string GoogleDriveConnectedEmail
    {
        get => _store.Values[GoogleDriveConnectedEmailKey] as string ?? string.Empty;
        set => _store.Values[GoogleDriveConnectedEmailKey] = value ?? string.Empty;
    }

    /// <summary>Returns the model id configured for the currently-selected provider.</summary>
    public string GetActiveModel() => AiProvider switch
    {
        "Claude" => ClaudeModel,
        "Gemini" => GeminiModel,
        "GPT" => GptModel,
        _ => ClaudeModel,
    };

    /// <summary>Returns the API key for the currently-selected provider.</summary>
    public string GetActiveApiKey() => AiProvider switch
    {
        "Claude" => ClaudeApiKey,
        "Gemini" => GeminiApiKey,
        "GPT" => GptApiKey,
        _ => string.Empty,
    };

    /// <summary>
    /// Earlier builds stored a single shared <c>ApiKey</c> regardless of provider; this caused 401s
    /// when the user had only entered (say) a Gemini key but switched to Claude. On first run after
    /// upgrade we move the legacy value into whichever per-provider LocalSettings slot is currently
    /// selected; the subsequent <see cref="MigratePlaintextApiKeysToVault"/> sweep then carries it
    /// (and any other already-typed keys) into the credential vault.
    /// </summary>
    private void MigrateLegacyApiKey()
    {
        if (_store.Values[LegacyApiKeyKey] is not string legacy || string.IsNullOrEmpty(legacy))
            return;

        var providerSlot = AiProvider switch
        {
            "Claude" => ClaudeApiKeyKey,
            "Gemini" => GeminiApiKeyKey,
            "GPT" => GptApiKeyKey,
            _ => GeminiApiKeyKey,
        };
        if (_store.Values[providerSlot] is not string existing || string.IsNullOrEmpty(existing))
            _store.Values[providerSlot] = legacy;

        _store.Values.Remove(LegacyApiKeyKey);
    }

    /// <summary>
    /// Sweeps any plaintext API keys still sitting in LocalSettings (from builds before the vault
    /// migration) into the credential vault, then deletes the LocalSettings entries. Idempotent —
    /// after the first successful sweep there's nothing left to read so subsequent launches are no-ops.
    /// Each provider migrates independently so a partial failure on one doesn't block the others.
    /// </summary>
    private void MigratePlaintextApiKeysToVault()
    {
        var slots = new (string LocalSettingsKey, string VaultUserName)[]
        {
            (ClaudeApiKeyKey, VaultProviderClaude),
            (GeminiApiKeyKey, VaultProviderGemini),
            (GptApiKeyKey, VaultProviderGpt),
        };

        foreach (var (localKey, userName) in slots)
        {
            if (_store.Values[localKey] is not string plaintext || string.IsNullOrEmpty(plaintext))
                continue;
            try
            {
                // Legacy plaintext keys were the user's OFFLINE keys (no Drive sync existed back then),
                // so they migrate into the Local vault resource regardless of current sync mode.
                WriteVault(VaultResourceLocal, userName, plaintext);
                _store.Values.Remove(localKey);
            }
            catch
            {
                // Vault write failed (rare — disabled by group policy etc.). Leave the plaintext
                // in place rather than dropping the user's API key; next launch will retry.
            }
        }
    }

    private static string ReadVault(string resource, string userName)
    {
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(resource, userName);
            credential.RetrievePassword();
            return credential.Password ?? string.Empty;
        }
        catch
        {
            // PasswordVault.Retrieve throws when no matching entry exists — empty is the natural fallback.
            return string.Empty;
        }
    }

    private static void WriteVault(string resource, string userName, string value)
    {
        var vault = new PasswordVault();

        // Remove any existing entry first so we don't duplicate or hit "already exists" semantics.
        try
        {
            var existing = vault.Retrieve(resource, userName);
            vault.Remove(existing);
        }
        catch { /* nothing to remove */ }

        // Empty value means "clear the credential" — already removed above, nothing more to do.
        if (string.IsNullOrEmpty(value)) return;

        vault.Add(new PasswordCredential(resource, userName, value));
    }
}
