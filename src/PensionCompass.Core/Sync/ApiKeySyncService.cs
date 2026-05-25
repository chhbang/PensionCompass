using System.Text.Json;
using System.Text.Json.Serialization;

namespace PensionCompass.Core.Sync;

/// <summary>
/// Bridges the three per-provider API keys to a single <c>apikeys.json</c> file inside the sync
/// provider's backing store. Used by the v1.2.0 Google-account API-key-link feature: when the user
/// is connected to Google, key changes in Settings round-trip through this service to drive.appdata,
/// and on connect we re-download to repopulate the local PasswordVault cloud-cache slots.
///
/// Format intentionally minimal: a flat JSON dict with three string fields. Whole-file replacement on
/// each save — payload is tiny, so we avoid the complexity of partial merges or per-key timestamps.
/// Last-write-wins is fine because no realistic user races two PCs through the Settings UI
/// simultaneously.
///
/// Filesystem-folder mode (v1.0.x compat) does NOT use this service — the design deliberately
/// excludes that mode to avoid writing plaintext API keys into the user's general filesystem
/// (where any process the user runs can read them). drive.appdata is gated by the OAuth client +
/// Google account combo and is hidden from the Drive web UI, which is a meaningfully better trust
/// boundary.
/// </summary>
public sealed class ApiKeySyncService
{
    private const string FileName = "apikeys.json";

    public sealed record ApiKeyBundle(string Claude, string Gemini, string Gpt)
    {
        public bool IsAllEmpty
            => string.IsNullOrEmpty(Claude) && string.IsNullOrEmpty(Gemini) && string.IsNullOrEmpty(Gpt);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Reads <c>apikeys.json</c> from the provider and returns the parsed bundle. Returns null when
    /// the file does not exist (typical for first-ever Google connect on a fresh account) or when
    /// the payload is unparseable. Missing fields in the JSON degrade to empty strings rather than
    /// failing — a partial bundle is still useful (the user might have configured only Claude).
    /// </summary>
    public ApiKeyBundle? Download(ISyncProvider provider)
    {
        var bytes = provider.Read(FileName);
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<BundleDto>(bytes, JsonOptions);
            if (dto is null) return null;
            return new ApiKeyBundle(
                Claude: dto.Claude ?? string.Empty,
                Gemini: dto.Gemini ?? string.Empty,
                Gpt: dto.Gpt ?? string.Empty);
        }
        catch
        {
            // Corrupt JSON shouldn't crash the app — surface as "no remote bundle" so the caller
            // falls back to whatever's already in the local cache.
            return null;
        }
    }

    /// <summary>
    /// Serializes the bundle and hands it to the provider's Write (fire-and-forget for the Google
    /// implementation — upload happens on the provider's background channel). Calling with an
    /// all-empty bundle still writes — that's how the user clears their cloud keys (or how a
    /// Disconnect cleanup signals "empty" to other PCs sharing the Google account).
    /// </summary>
    public void Upload(ISyncProvider provider, ApiKeyBundle bundle)
    {
        var dto = new BundleDto(bundle.Claude, bundle.Gemini, bundle.Gpt);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
        provider.Write(FileName, bytes);
    }

    private sealed record BundleDto(
        [property: JsonPropertyName("claude")] string Claude,
        [property: JsonPropertyName("gemini")] string Gemini,
        [property: JsonPropertyName("gpt")] string Gpt);
}
