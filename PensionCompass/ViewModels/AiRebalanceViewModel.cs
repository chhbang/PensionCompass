using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PensionCompass.Core.Ai;
using PensionCompass.Core.Models;
using PensionCompass.Core.Pdf;
using PensionCompass.Services;

namespace PensionCompass.ViewModels;

public sealed partial class AiRebalanceViewModel : ObservableObject
{
    [ObservableProperty]
    private string _userQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResponse))]
    private string _aiResponse = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "포트폴리오 제안 받기 버튼을 누르면 입력된 정보가 AI에게 전달됩니다.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanExportPdf))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExportedPdf))]
    private string _lastExportedPdfPath = string.Empty;

    public bool HasExportedPdf => !string.IsNullOrEmpty(LastExportedPdfPath);

    /// <summary>Provider+model used for the current response, captured at request time so the PDF metadata stays accurate.</summary>
    private string _lastProviderName = string.Empty;
    private string _lastModelId = string.Empty;
    private DateTime _lastResponseAt;

    public bool HasResponse => !string.IsNullOrWhiteSpace(AiResponse);
    public bool CanGenerate => !IsBusy;
    public bool CanExportPdf => !IsBusy && HasResponse;

    /// <summary>
    /// Builds the prompt that <em>would</em> be sent for the current inputs, for previewing.
    /// No validation — PromptBuilder gracefully handles empty account / null catalog / missing
    /// execution date by emitting placeholder lines, and the preview should reflect that exact output.
    /// </summary>
    public PromptOutput BuildPromptPreview()
        => PromptBuilder.Build(new PromptInput(
            AppState.Instance.Catalog,
            AppState.Instance.Account,
            UserQuery));

    public async Task GenerateProposalAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;

        var settings = AppState.Instance.Settings;
        var apiKey = settings.GetActiveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusMessage = $"환경 설정 화면에서 \"{settings.AiProvider}\" API Key를 먼저 입력해주세요.";
            return;
        }

        var account = AppState.Instance.Account;
        if (account.TotalAmount <= 0 || account.OwnedItems.Count == 0)
        {
            StatusMessage = "내 계좌 정보 화면에서 총 적립금과 보유 상품을 먼저 입력해주세요.";
            return;
        }

        if (account.RebalanceTiming == RebalanceTiming.MaturityReservation
            && !account.ExecutionDate.HasValue)
        {
            StatusMessage = "만기 예약용 리밸런싱은 실행 날짜를 입력해야 합니다 (매도 대상 화면).";
            return;
        }

        IsBusy = true;
        AiResponse = string.Empty;
        // The previous PDF is from a different response — drop the convenience link so the user
        // doesn't accidentally open a stale file thinking it matches the new proposal.
        LastExportedPdfPath = string.Empty;
        try
        {
            var prompt = PromptBuilder.Build(new PromptInput(
                AppState.Instance.Catalog,
                account,
                UserQuery));

            var client = AiClientFactory.Create(settings.AiProvider, apiKey, settings.GetActiveModel());
            _lastProviderName = client.ProviderName;
            _lastModelId = client.ModelId;
            StatusMessage = $"{client.ProviderName} ({client.ModelId}, 사고 수준: {settings.ThinkingLevel.ToKoreanLabel()}) 호출 중... 응답까지 1~3분 정도 걸릴 수 있습니다.";

            var aiRequest = new AiRequest(prompt.SystemPrompt, prompt.UserPrompt, settings.ThinkingLevel);
            var response = await client.GenerateAsync(aiRequest, cancellationToken);
            AiResponse = response;
            _lastResponseAt = DateTime.Now;
            StatusMessage = $"{client.ProviderName} 응답 완료. 마크다운으로 표시됩니다. PDF로 저장 가능합니다.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "요청이 취소되었습니다.";
        }
        catch (AiClientException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"예상치 못한 오류: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportPdfAsync(string filePath)
    {
        if (!HasResponse)
        {
            StatusMessage = "내보낼 응답이 없습니다.";
            return;
        }

        IsBusy = true;
        try
        {
            var report = new PdfReport(
                GeneratedAt: _lastResponseAt == default ? DateTime.Now : _lastResponseAt,
                ProviderName: string.IsNullOrEmpty(_lastProviderName) ? AppState.Instance.Settings.AiProvider : _lastProviderName,
                ModelId: string.IsNullOrEmpty(_lastModelId) ? "(미지정)" : _lastModelId,
                Account: AppState.Instance.Account,
                AiResponseMarkdown: AiResponse);
            await Task.Run(() => PdfExporter.Export(filePath, report));
            LastExportedPdfPath = filePath;
            StatusMessage = "PDF 저장 완료. 아래 경로를 클릭하면 기본 뷰어에서 열립니다.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"PDF 저장 오류: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
