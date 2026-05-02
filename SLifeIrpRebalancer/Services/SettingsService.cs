using SLifeIrpRebalancer.Core.Ai;
using Windows.Storage;

namespace SLifeIrpRebalancer.Services;

/// <summary>
/// Persists user-level settings across runs:
/// - AI provider choice ("Claude" / "Gemini" / "GPT")
/// - per-provider model id and API key (each provider has its own credentials)
/// - reasoning effort (Off / Low / Medium / High)
/// Subscriber info (age, annuity-start age, lifelong-annuity preference) lives on
/// <see cref="Core.Models.AccountStatusModel"/>, not here — see <see cref="AppState"/>'s migration.
/// Backing store is per-user packaged-app LocalSettings. API keys are stored in plain text —
/// adequate for a single-user personal app, but if multi-user / shared-machine scenarios become
/// relevant, migrate to <c>Windows.Security.Credentials.PasswordVault</c> for OS-level encryption.
/// </summary>
public sealed class SettingsService
{
    private const string AiProviderKey = "AiProvider";
    private const string LegacyApiKeyKey = "ApiKey"; // single-key field used before per-provider keys
    private const string ClaudeApiKeyKey = "ClaudeApiKey";
    private const string GeminiApiKeyKey = "GeminiApiKey";
    private const string GptApiKeyKey = "GptApiKey";
    private const string LegacyRestrictToSamsungLifeKey = "RestrictToSamsungLifeForLifelongAnnuity"; // moved to AccountStatusModel.WantsLifelongAnnuity
    private const string ClaudeModelKey = "ClaudeModel";
    private const string GeminiModelKey = "GeminiModel";
    private const string GptModelKey = "GptModel";
    private const string ThinkingLevelKey = "ThinkingLevel";

    private readonly ApplicationDataContainer _store = ApplicationData.Current.LocalSettings;

    public SettingsService()
    {
        MigrateLegacyApiKey();
    }

    public string AiProvider
    {
        get => _store.Values[AiProviderKey] as string ?? "Claude";
        set => _store.Values[AiProviderKey] = value;
    }

    public string ClaudeApiKey
    {
        get => _store.Values[ClaudeApiKeyKey] as string ?? string.Empty;
        set => _store.Values[ClaudeApiKeyKey] = value;
    }

    public string GeminiApiKey
    {
        get => _store.Values[GeminiApiKeyKey] as string ?? string.Empty;
        set => _store.Values[GeminiApiKeyKey] = value;
    }

    public string GptApiKey
    {
        get => _store.Values[GptApiKeyKey] as string ?? string.Empty;
        set => _store.Values[GptApiKeyKey] = value;
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
    /// upgrade we move the legacy value into whichever per-provider slot is currently selected,
    /// then drop the legacy key.
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
}
