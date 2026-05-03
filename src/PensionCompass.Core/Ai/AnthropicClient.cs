using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PensionCompass.Core.Ai;

/// <summary>
/// Calls Anthropic's Messages API at https://api.anthropic.com/v1/messages.
/// Defaults to Claude Opus 4.7. ThinkingLevel maps to the <c>thinking.budget_tokens</c> field;
/// when enabled, the response's <c>content</c> array can interleave thinking blocks with text
/// blocks, so we filter for <c>type == "text"</c> only.
/// </summary>
public sealed class AnthropicClient : IAiClient
{
    public const string DefaultModel = "claude-opus-4-7";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    private readonly string _apiKey;
    private readonly string _model;

    public AnthropicClient(string apiKey, string? model = null)
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
    }

    public string ProviderName => "Claude";
    public string ModelId => _model;

    public async Task<string> GenerateAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        httpRequest.Headers.Add("x-api-key", _apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        var (maxTokens, budgetTokens) = MapThinking(request.ThinkingLevel);

        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["max_tokens"] = maxTokens,
            ["system"] = request.SystemPrompt,
            ["messages"] = new[] { new { role = "user", content = request.UserPrompt } },
        };
        if (budgetTokens > 0)
        {
            body["thinking"] = new
            {
                type = "enabled",
                budget_tokens = budgetTokens,
            };
        }
        httpRequest.Content = JsonContent.Create(body);

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiClientException($"Anthropic API 요청 실패: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new AiClientException($"Anthropic API 오류 ({(int)response.StatusCode}): {Truncate(json)}");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var contentArray = doc.RootElement.GetProperty("content");
            var sb = new StringBuilder();
            foreach (var block in contentArray.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type)
                    && type.GetString() == "text"
                    && block.TryGetProperty("text", out var textProp))
                {
                    sb.Append(textProp.GetString());
                }
            }
            if (sb.Length == 0)
                throw new AiClientException("Anthropic 응답에 text 블록이 없습니다.");
            return sb.ToString();
        }
        catch (Exception ex) when (ex is not AiClientException)
        {
            throw new AiClientException($"Anthropic 응답 파싱 실패: {ex.Message}. 본문: {Truncate(json)}", ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models?limit=100");
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiClientException($"Anthropic 모델 목록 요청 실패: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new AiClientException($"Anthropic 모델 목록 조회 실패 ({(int)response.StatusCode}): {Truncate(json)}");

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
            return list.OrderBy(s => s, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is not AiClientException)
        {
            throw new AiClientException($"Anthropic 모델 목록 파싱 실패: {ex.Message}. 본문: {Truncate(json)}", ex);
        }
    }

    private static (int maxTokens, int budgetTokens) MapThinking(ThinkingLevel level) => level switch
    {
        ThinkingLevel.Off => (8192, 0),
        ThinkingLevel.Low => (12_000, 2_000),
        ThinkingLevel.Medium => (24_000, 8_000),
        ThinkingLevel.High => (48_000, 24_000),
        _ => (8192, 0),
    };

    private static string Truncate(string s, int max = 500)
        => s.Length <= max ? s : s[..max] + "...";
}
