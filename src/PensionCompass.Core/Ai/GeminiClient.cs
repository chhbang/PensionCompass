using System.Net.Http.Json;
using System.Text.Json;

namespace PensionCompass.Core.Ai;

/// <summary>
/// Calls Google Gemini's generateContent endpoint at
/// https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent.
/// ThinkingLevel maps to <c>generationConfig.thinkingConfig.thinkingBudget</c> on Gemini 2.5+
/// thinking models. -1 means "dynamic" (the model picks its own budget); 0 disables thinking.
/// </summary>
public sealed class GeminiClient : IAiClient
{
    public const string DefaultModel = "gemini-2.5-pro";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    private readonly string _apiKey;
    private readonly string _model;

    public GeminiClient(string apiKey, string? model = null)
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
    }

    public string ProviderName => "Gemini";
    public string ModelId => _model;

    public async Task<string> GenerateAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={Uri.EscapeDataString(_apiKey)}";
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);

        var body = new Dictionary<string, object?>
        {
            ["system_instruction"] = new
            {
                parts = new[] { new { text = request.SystemPrompt } },
            },
            ["contents"] = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = request.UserPrompt } },
                },
            },
        };
        body["generationConfig"] = new
        {
            thinkingConfig = new
            {
                thinkingBudget = MapThinkingBudget(request.ThinkingLevel),
            },
        };
        httpRequest.Content = JsonContent.Create(body);

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiClientException($"Gemini API 요청 실패: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new AiClientException($"Gemini API 오류 ({(int)response.StatusCode}): {Truncate(json)}");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var candidates = doc.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() == 0)
                throw new AiClientException("Gemini 응답에 candidates가 없습니다.");
            var parts = candidates[0].GetProperty("content").GetProperty("parts");
            if (parts.GetArrayLength() == 0)
                throw new AiClientException("Gemini 응답이 비어 있습니다.");
            return parts[0].GetProperty("text").GetString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is not AiClientException)
        {
            throw new AiClientException($"Gemini 응답 파싱 실패: {ex.Message}. 본문: {Truncate(json)}", ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(_apiKey)}&pageSize=200";
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiClientException($"Gemini 모델 목록 요청 실패: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new AiClientException($"Gemini 모델 목록 조회 실패 ({(int)response.StatusCode}): {Truncate(json)}");

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var models))
                return [];

            var list = new List<string>();
            foreach (var m in models.EnumerateArray())
            {
                if (!m.TryGetProperty("name", out var name)) continue;
                var nameStr = name.GetString();
                if (string.IsNullOrEmpty(nameStr)) continue;

                // Only include models that actually support generateContent —
                // skip embedding-only / aqa-only / vision-only entries.
                var supportsGenerate = false;
                if (m.TryGetProperty("supportedGenerationMethods", out var methods))
                {
                    foreach (var method in methods.EnumerateArray())
                    {
                        if (method.GetString() == "generateContent")
                        {
                            supportsGenerate = true;
                            break;
                        }
                    }
                }
                if (!supportsGenerate) continue;

                // Strip the "models/" path prefix so users can paste the result straight into the model field.
                if (nameStr.StartsWith("models/", StringComparison.Ordinal))
                    nameStr = nameStr["models/".Length..];
                list.Add(nameStr);
            }
            return list.OrderBy(s => s, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is not AiClientException)
        {
            throw new AiClientException($"Gemini 모델 목록 파싱 실패: {ex.Message}. 본문: {Truncate(json)}", ex);
        }
    }

    private static int MapThinkingBudget(ThinkingLevel level) => level switch
    {
        ThinkingLevel.Off => 0,
        ThinkingLevel.Low => 2_048,
        ThinkingLevel.Medium => 8_192,
        ThinkingLevel.High => -1, // dynamic — let Gemini pick its own ceiling
        _ => -1,
    };

    private static string Truncate(string s, int max = 500)
        => s.Length <= max ? s : s[..max] + "...";
}
