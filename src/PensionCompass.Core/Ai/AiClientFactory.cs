namespace PensionCompass.Core.Ai;

public static class AiClientFactory
{
    /// <summary>
    /// Creates an <see cref="IAiClient"/> for the given provider label as persisted by SettingsService:
    /// "Claude" / "Gemini" / "GPT". Pass <paramref name="model"/> as null/empty to use each client's default.
    /// </summary>
    public static IAiClient Create(string providerName, string apiKey, string? model = null)
        => providerName switch
        {
            "Claude" => new AnthropicClient(apiKey, model),
            "Gemini" => new GeminiClient(apiKey, model),
            "GPT" => new OpenAiClient(apiKey, model),
            _ => throw new ArgumentException($"알 수 없는 AI 공급자: {providerName}", nameof(providerName)),
        };
}
