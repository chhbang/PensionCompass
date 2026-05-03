using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PensionCompass.Core.Ai;

/// <summary>
/// Calls OpenAI's chat completions endpoint at https://api.openai.com/v1/chat/completions.
/// ThinkingLevel maps to <c>reasoning_effort</c> for GPT-5 era reasoning models;
/// non-reasoning models will ignore or reject the field, so Off omits it entirely.
/// </summary>
public sealed class OpenAiClient : IAiClient
{
    public const string DefaultModel = "gpt-5";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiClient(string apiKey, string? model = null)
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
    }

    public string ProviderName => "GPT";
    public string ModelId => _model;

    public async Task<string> GenerateAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["messages"] = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt },
            },
        };
        var effort = MapReasoningEffort(request.ThinkingLevel);
        if (effort != null)
            body["reasoning_effort"] = effort;
        httpRequest.Content = JsonContent.Create(body);

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiClientException($"OpenAI API 요청 실패: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new AiClientException($"OpenAI API 오류 ({(int)response.StatusCode}): {Truncate(json)}");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
                throw new AiClientException("OpenAI 응답이 비어 있습니다.");
            return choices[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is not AiClientException)
        {
            throw new AiClientException($"OpenAI 응답 파싱 실패: {ex.Message}. 본문: {Truncate(json)}", ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiClientException($"OpenAI 모델 목록 요청 실패: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new AiClientException($"OpenAI 모델 목록 조회 실패 ({(int)response.StatusCode}): {Truncate(json)}");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");
            var list = new List<string>();
            foreach (var m in data.EnumerateArray())
            {
                if (m.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } s)
                    list.Add(s);
            }
            // OpenAI's catalog includes embedding/audio/moderation/etc. — filter to chat-capable model
            // families. The list endpoint doesn't expose capabilities, so we go by name prefix.
            var chatLike = list.Where(IsChatLikeModel).ToList();
            return (chatLike.Count > 0 ? chatLike : list)
                .OrderBy(s => s, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is not AiClientException)
        {
            throw new AiClientException($"OpenAI 모델 목록 파싱 실패: {ex.Message}. 본문: {Truncate(json)}", ex);
        }
    }

    private static bool IsChatLikeModel(string id)
        => id.StartsWith("gpt-", StringComparison.Ordinal)
            || id.StartsWith("o1", StringComparison.Ordinal)
            || id.StartsWith("o3", StringComparison.Ordinal)
            || id.StartsWith("o4", StringComparison.Ordinal)
            || id.StartsWith("chatgpt-", StringComparison.Ordinal);

    private static string? MapReasoningEffort(ThinkingLevel level) => level switch
    {
        ThinkingLevel.Off => null,
        ThinkingLevel.Low => "low",
        ThinkingLevel.Medium => "medium",
        ThinkingLevel.High => "high",
        _ => null,
    };

    private static string Truncate(string s, int max = 500)
        => s.Length <= max ? s : s[..max] + "...";
}
