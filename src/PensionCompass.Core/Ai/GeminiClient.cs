using System.Net.Http.Headers;
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

        // Attachments go through the File API (inline_data caps the whole request at 20MB, which a
        // single 19MB PDF already blows past once base64-inflated). Upload → poll ACTIVE → reference.
        var userParts = request.Attachments.Count > 0
            ? await BuildPartsWithAttachmentsAsync(request, cancellationToken)
            : new object[] { new { text = request.UserPrompt } };

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
                    parts = userParts,
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

    /// <summary>Uploads each attachment via the File API and builds the user parts: one
    /// <c>file_data</c> part per PDF, then the prompt text.</summary>
    private async Task<object[]> BuildPartsWithAttachmentsAsync(AiRequest request, CancellationToken ct)
    {
        var parts = new List<object>(request.Attachments.Count + 1);
        foreach (var a in request.Attachments)
        {
            var fileUri = await UploadFileAsync(a, ct);
            parts.Add(new { file_data = new { mime_type = a.MediaType, file_uri = fileUri } });
        }
        parts.Add(new { text = request.UserPrompt });
        return parts.ToArray();
    }

    /// <summary>
    /// Resumable upload of one document to the Gemini File API, then polls until the file leaves
    /// PROCESSING. Returns the file URI to reference in generateContent. The File API has no
    /// practical size limit for our PDFs (2GB/file), so no size guard here — unlike the inline path.
    /// </summary>
    private async Task<string> UploadFileAsync(DocumentAttachment a, CancellationToken ct)
    {
        var startUrl = $"https://generativelanguage.googleapis.com/upload/v1beta/files?key={Uri.EscapeDataString(_apiKey)}";

        // 1) Start a resumable upload session.
        var start = new HttpRequestMessage(HttpMethod.Post, startUrl);
        start.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
        start.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "start");
        start.Headers.TryAddWithoutValidation("X-Goog-Upload-Header-Content-Length", a.Content.Length.ToString());
        start.Headers.TryAddWithoutValidation("X-Goog-Upload-Header-Content-Type", a.MediaType);
        start.Content = JsonContent.Create(new { file = new { display_name = a.FileName } });

        HttpResponseMessage startResp;
        try { startResp = await Http.SendAsync(start, ct); }
        catch (HttpRequestException ex) { throw new AiClientException($"Gemini 파일 업로드 시작 실패: {ex.Message}", ex); }
        if (!startResp.IsSuccessStatusCode)
            throw new AiClientException($"Gemini 파일 업로드 시작 오류 ({(int)startResp.StatusCode}): {Truncate(await startResp.Content.ReadAsStringAsync(ct))}");
        if (!startResp.Headers.TryGetValues("X-Goog-Upload-URL", out var uploadUrls))
            throw new AiClientException("Gemini 파일 업로드 URL을 받지 못했습니다.");
        var uploadUrl = uploadUrls.First();

        // 2) Upload the bytes and finalize in one shot.
        var upload = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        upload.Headers.TryAddWithoutValidation("X-Goog-Upload-Offset", "0");
        upload.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "upload, finalize");
        upload.Content = new ByteArrayContent(a.Content);
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue(a.MediaType);

        HttpResponseMessage uploadResp;
        try { uploadResp = await Http.SendAsync(upload, ct); }
        catch (HttpRequestException ex) { throw new AiClientException($"Gemini 파일 업로드 실패: {ex.Message}", ex); }
        var uploadJson = await uploadResp.Content.ReadAsStringAsync(ct);
        if (!uploadResp.IsSuccessStatusCode)
            throw new AiClientException($"Gemini 파일 업로드 오류 ({(int)uploadResp.StatusCode}): {Truncate(uploadJson)}");

        string fileName, fileUri, state;
        try
        {
            using var doc = JsonDocument.Parse(uploadJson);
            var file = doc.RootElement.GetProperty("file");
            fileName = file.GetProperty("name").GetString() ?? string.Empty;
            fileUri = file.GetProperty("uri").GetString() ?? string.Empty;
            state = file.TryGetProperty("state", out var st) ? st.GetString() ?? string.Empty : string.Empty;
        }
        catch (Exception ex) when (ex is not AiClientException)
        {
            throw new AiClientException($"Gemini 파일 업로드 응답 파싱 실패: {ex.Message}. 본문: {Truncate(uploadJson)}", ex);
        }
        if (string.IsNullOrEmpty(fileUri))
            throw new AiClientException("Gemini 파일 URI를 받지 못했습니다.");

        // 3) Poll until the file is ACTIVE (small PDFs are usually ACTIVE immediately).
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        while (state == "PROCESSING")
        {
            if (DateTime.UtcNow > deadline)
                throw new AiClientException($"Gemini 파일 처리 대기 시간 초과: {a.FileName}");
            await Task.Delay(2000, ct);
            var getUrl = $"https://generativelanguage.googleapis.com/v1beta/{fileName}?key={Uri.EscapeDataString(_apiKey)}";
            var getResp = await Http.GetAsync(getUrl, ct);
            var getJson = await getResp.Content.ReadAsStringAsync(ct);
            if (!getResp.IsSuccessStatusCode)
                throw new AiClientException($"Gemini 파일 상태 조회 오류 ({(int)getResp.StatusCode}): {Truncate(getJson)}");
            using var gdoc = JsonDocument.Parse(getJson);
            state = gdoc.RootElement.TryGetProperty("state", out var st2) ? st2.GetString() ?? string.Empty : string.Empty;
        }
        if (state == "FAILED")
            throw new AiClientException($"Gemini 파일 처리 실패: {a.FileName}");

        return fileUri;
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
