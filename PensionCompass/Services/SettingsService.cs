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

    /// <summary>Single resource string for all API key entries; provider name is stored as the userName field.</summary>
    private const string VaultResource = "PensionCompass.ApiKey";
    private const string VaultProviderClaude = "Claude";
    private const string VaultProviderGemini = "Gemini";
    private const string VaultProviderGpt = "GPT";

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
        get => ReadVault(VaultProviderClaude);
        set => WriteVault(VaultProviderClaude, value);
    }

    public string GeminiApiKey
    {
        get => ReadVault(VaultProviderGemini);
        set => WriteVault(VaultProviderGemini, value);
    }

    public string GptApiKey
    {
        get => ReadVault(VaultProviderGpt);
        set => WriteVault(VaultProviderGpt, value);
    }

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
                WriteVault(userName, plaintext);
                _store.Values.Remove(localKey);
            }
            catch
            {
                // Vault write failed (rare — disabled by group policy etc.). Leave the plaintext
                // in place rather than dropping the user's API key; next launch will retry.
            }
        }
    }

    private static string ReadVault(string userName)
    {
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(VaultResource, userName);
            credential.RetrievePassword();
            return credential.Password ?? string.Empty;
        }
        catch
        {
            // PasswordVault.Retrieve throws when no matching entry exists — empty is the natural fallback.
            return string.Empty;
        }
    }

    private static void WriteVault(string userName, string value)
    {
        var vault = new PasswordVault();

        // Remove any existing entry first so we don't duplicate or hit "already exists" semantics.
        try
        {
            var existing = vault.Retrieve(VaultResource, userName);
            vault.Remove(existing);
        }
        catch { /* nothing to remove */ }

        // Empty value means "clear the credential" — already removed above, nothing more to do.
        if (string.IsNullOrEmpty(value)) return;

        vault.Add(new PasswordCredential(VaultResource, userName, value));
    }
}
