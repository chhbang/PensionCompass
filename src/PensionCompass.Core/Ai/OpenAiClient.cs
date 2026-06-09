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
            ["messages"] = new List<object>
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = BuildUserContent(request) },
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

    // OpenAI chat completions accepts inline base64 PDFs as a `file` content block. Guard the raw total.
    private const long MaxAttachmentBytes = 32L * 1024 * 1024;

    /// <summary>
    /// The user message content: a plain string with no attachments, or a content-block array
    /// (one <c>file</c> block per PDF via a base64 data URL, then the text). Throws a clear error
    /// when the attachments exceed the inline size budget.
    /// </summary>
    private static object BuildUserContent(AiRequest request)
    {
        if (request.Attachments.Count == 0) return request.UserPrompt;

        var total = request.Attachments.Sum(a => (long)a.Content.Length);
        if (total > MaxAttachmentBytes)
            throw new AiClientException(
                $"첨부 PDF 합계가 {total / 1024 / 1024}MB로 GPT 인라인 한도(약 32MB)를 초과합니다. 참고 자료 일부를 비활성화하거나 더 작은 파일을 사용하세요.");

        var blocks = new List<object>(request.Attachments.Count + 1);
        foreach (var a in request.Attachments)
        {
            blocks.Add(new
            {
                type = "file",
                file = new
                {
                    filename = a.FileName,
                    file_data = $"data:{a.MediaType};base64,{Convert.ToBase64String(a.Content)}",
                },
            });
        }
        blocks.Add(new { type = "text", text = request.UserPrompt });
        return blocks;
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
