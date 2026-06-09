using System.Text.Json;
using System.Text.Json.Serialization;

namespace PensionCompass.Core.Sync;

/// <summary>
/// Mirrors the user's non-secret preferences (selected AI provider, per-provider model ids, thinking
/// level) to <c>settings.json</c> in the sync provider's store, so "log in on another PC and get my
/// setup" covers more than just account/catalog/API-keys. Plain JSON (these aren't secrets — unlike
/// API keys, which go through the encrypted <see cref="ApiKeySyncService"/>). Whole-file replacement;
/// last-write-wins, which is fine for a single user across a couple of devices.
/// </summary>
public sealed class PreferenceSyncService
{
    private const string FileName = "settings.json";

    public sealed record PreferenceBundle(
        string AiProvider,
        string ClaudeModel,
        string GeminiModel,
        string GptModel,
        string ThinkingLevel);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>Reads <c>settings.json</c>; null when absent or unparseable (caller keeps local prefs).</summary>
    public PreferenceBundle? Download(ISyncProvider provider)
    {
        var bytes = provider.Read(FileName);
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<BundleDto>(bytes, JsonOptions);
            if (dto is null) return null;
            return new PreferenceBundle(
                AiProvider: dto.AiProvider ?? string.Empty,
                ClaudeModel: dto.ClaudeModel ?? string.Empty,
                GeminiModel: dto.GeminiModel ?? string.Empty,
                GptModel: dto.GptModel ?? string.Empty,
                ThinkingLevel: dto.ThinkingLevel ?? string.Empty);
        }
        catch
        {
            return null;
        }
    }

    public void Upload(ISyncProvider provider, PreferenceBundle bundle)
    {
        var dto = new BundleDto(bundle.AiProvider, bundle.ClaudeModel, bundle.GeminiModel, bundle.GptModel, bundle.ThinkingLevel);
        provider.Write(FileName, JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions));
    }

    private sealed record BundleDto(
        [property: JsonPropertyName("aiProvider")] string AiProvider,
        [property: JsonPropertyName("claudeModel")] string ClaudeModel,
        [property: JsonPropertyName("geminiModel")] string GeminiModel,
        [property: JsonPropertyName("gptModel")] string GptModel,
        [property: JsonPropertyName("thinkingLevel")] string ThinkingLevel);
}
