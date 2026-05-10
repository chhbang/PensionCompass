using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PensionCompass.Core.Ai;
using PensionCompass.Core.History;
using PensionCompass.Core.Models;
using PensionCompass.Core.Pdf;
using PensionCompass.Core.Validation;
using PensionCompass.Services;

namespace PensionCompass.ViewModels;

public sealed partial class AiRebalanceViewModel : ObservableObject
{
    [ObservableProperty]
    private string _userQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResponse))]
    [NotifyPropertyChangedFor(nameof(CanSaveHistory))]
    private string _aiResponse = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "포트폴리오 제안 받기 버튼을 누르면 입력된 정보가 AI에게 전달됩니다.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanExportPdf))]
    [NotifyPropertyChangedFor(nameof(CanSaveHistory))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExportedPdf))]
    private string _lastExportedPdfPath = string.Empty;

    /// <summary>
    /// User-selected past session that will be appended to the next prompt as a "직전 리밸런싱"
    /// reference block. Null when "이전 회차 참고 안 함" is selected (the default).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PriorSessionDescription))]
    private RebalanceSessionEntry? _selectedPriorEntry;

    /// <summary>The full session document loaded lazily when the user picks an entry.</summary>
    private RebalanceSession? _resolvedPriorSession;

    /// <summary>
    /// Realized period return between the selected prior session and the current account state,
    /// computed by <see cref="PeriodComparisonCalculator"/> whenever <see cref="SelectedPriorEntry"/>
    /// resolves. The view binds to the display props below to show a quick summary card so the user
    /// can see what factual data will reach the AI before they hit "포트폴리오 제안 받기".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPriorOutcome))]
    [NotifyPropertyChangedFor(nameof(PriorOutcomeHeader))]
    [NotifyPropertyChangedFor(nameof(PriorOutcomeDetail))]
    private PeriodComparison? _priorOutcome;

    /// <summary>Recent past sessions, newest first. Loaded by <see cref="RefreshHistoryEntries"/>.</summary>
    public ObservableCollection<RebalanceSessionEntry> AvailableHistory { get; } = [];

    /// <summary>True after a session has been saved this turn — flips the button to "저장됨" so
    /// the user doesn't double-save the same response.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveHistory))]
    private bool _hasSavedCurrentResponse;

    /// <summary>Result of the IRP 70/30 rule check on the current AI response, or null when
    /// no response is loaded yet. Computed automatically when <see cref="AiResponse"/> changes
    /// via <see cref="GenerateProposalAsync"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidation))]
    [NotifyPropertyChangedFor(nameof(ValidationStatusKey))]
    [NotifyPropertyChangedFor(nameof(ValidationDisplayHeader))]
    [NotifyPropertyChangedFor(nameof(ValidationDisplayMessage))]
    private IrpValidationResult? _validationResult;

    public bool HasValidation => ValidationResult is not null;
    public string ValidationStatusKey => ValidationResult?.Status.ToString() ?? "None";
    public string ValidationDisplayHeader => ValidationResult?.Status switch
    {
        IrpValidationStatus.Compliant => "IRP 70/30 규제 준수",
        IrpValidationStatus.Violation => "IRP 70/30 규제 위반 감지",
        IrpValidationStatus.UnableToVerify => "IRP 70/30 자동 검증 불가",
        _ => string.Empty,
    };
    public string ValidationDisplayMessage => ValidationResult?.Message ?? string.Empty;

    public bool HasExportedPdf => !string.IsNullOrEmpty(LastExportedPdfPath);

    /// <summary>Provider+model used for the current response, captured at request time so the PDF metadata stays accurate.</summary>
    private string _lastProviderName = string.Empty;
    private string _lastModelId = string.Empty;
    private DateTime _lastResponseAt;
    private ThinkingLevel _lastThinkingLevel = ThinkingLevel.High;

    public bool HasResponse => !string.IsNullOrWhiteSpace(AiResponse);
    public bool CanGenerate => !IsBusy;
    public bool CanExportPdf => !IsBusy && HasResponse;
    public bool CanSaveHistory => !IsBusy && HasResponse && !HasSavedCurrentResponse;

    public string PriorSessionDescription => SelectedPriorEntry is null
        ? "이전 회차 참고 안 함"
        : $"{SelectedPriorEntry.Meta.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm}  ·  {SelectedPriorEntry.Meta.ProviderName} ({SelectedPriorEntry.Meta.ModelId})";

    public bool HasPriorOutcome => PriorOutcome is not null;

    public string PriorOutcomeHeader
    {
        get
        {
            if (PriorOutcome is not { } o) return string.Empty;
            var pct = o.PeriodReturnPercent;
            var pctText = pct.HasValue ? $"{(pct.Value >= 0 ? "+" : "")}{pct.Value:0.00}%" : "N/A";
            return $"직전 회차 → 현재: {o.DaysElapsed}일 동안 {pctText}";
        }
    }

    public string PriorOutcomeDetail
    {
        get
        {
            if (PriorOutcome is not { } o) return string.Empty;
            var contribNote = o.ContributionSource switch
            {
                ContributionSource.DepositAmountDelta => "DepositAmount 델타",
                ContributionSource.MonthlyEstimate => "월 적립 추정",
                _ => "입금 차감 안 됨",
            };
            var contribAmount = o.NetContribution.HasValue
                ? $"{o.NetContribution.Value:N0}원 ({contribNote})"
                : "정보 없음";
            var annualized = o.AnnualizedReturnPercent is { } ann ? $"  ·  연환산 약 {(ann >= 0 ? "+" : "")}{ann:0.00}%" : string.Empty;
            return $"₩{o.PriorTotal:N0} → ₩{o.CurrentTotal:N0}  ·  신규 입금 {contribAmount}{annualized}";
        }
    }

    /// <summary>Reloads the recent-history listing — call when navigating to the screen and after saves.
    /// If the History screen handed off a "use this as prior" entry, pre-select it here.</summary>
    public void RefreshHistoryEntries()
    {
        AvailableHistory.Clear();
        try
        {
            var entries = RebalanceHistoryStore.List(AppState.Instance.CandidateHistoryRoots());
            // Show top 8; the dedicated 이력 screen shows the full list. Keeps the combo readable.
            foreach (var entry in entries.Take(8))
                AvailableHistory.Add(entry);

            if (AppState.Instance.ConsumePendingPriorEntry() is { } pending)
            {
                // Make sure the handed-off entry is in the visible list even if it falls outside top-8,
                // otherwise the SelectedPriorEntry binding silently snaps back to null.
                if (!AvailableHistory.Any(e => string.Equals(e.FilePath, pending.FilePath, StringComparison.OrdinalIgnoreCase)))
                    AvailableHistory.Insert(0, pending);
                SelectedPriorEntry = AvailableHistory.First(e => string.Equals(e.FilePath, pending.FilePath, StringComparison.OrdinalIgnoreCase));
                StatusMessage = "이전 회차로 \"" + SelectedPriorEntry.DisplayLabel + "\" 가 선택됐습니다. 포트폴리오 제안 받기를 누르면 그 추천이 컨텍스트로 함께 전달됩니다.";
            }
        }
        catch
        {
            // Listing failures shouldn't block the AI flow — leave the combo empty.
        }
    }

    /// <summary>
    /// Builds the prompt that <em>would</em> be sent for the current inputs, for previewing.
    /// No validation — PromptBuilder gracefully handles empty account / null catalog / missing
    /// execution date by emitting placeholder lines, and the preview should reflect that exact output.
    /// </summary>
    public PromptOutput BuildPromptPreview()
    {
        var prior = ResolvePriorSession();
        return PromptBuilder.Build(new PromptInput(
            AppState.Instance.Catalog,
            AppState.Instance.Account,
            UserQuery,
            prior,
            PriorOutcome));
    }

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
        HasSavedCurrentResponse = false;
        ValidationResult = null;
        try
        {
            var resolvedPrior = ResolvePriorSession();
            var prompt = PromptBuilder.Build(new PromptInput(
                AppState.Instance.Catalog,
                account,
                UserQuery,
                resolvedPrior,
                PriorOutcome));

            var client = AiClientFactory.Create(settings.AiProvider, apiKey, settings.GetActiveModel());
            _lastProviderName = client.ProviderName;
            _lastModelId = client.ModelId;
            _lastThinkingLevel = settings.ThinkingLevel;
            StatusMessage = $"{client.ProviderName} ({client.ModelId}, 사고 수준: {settings.ThinkingLevel.ToKoreanLabel()}) 호출 중... 응답까지 1~3분 정도 걸릴 수 있습니다.";

            var aiRequest = new AiRequest(prompt.SystemPrompt, prompt.UserPrompt, settings.ThinkingLevel);
            var response = await client.GenerateAsync(aiRequest, cancellationToken);
            AiResponse = response;
            ValidationResult = IrpRuleValidator.Validate(response);
            _lastResponseAt = DateTime.Now;
            StatusMessage = $"{client.ProviderName} 응답 완료. 마음에 들면 \"이력에 저장\" 버튼으로 이번 회차를 보관하세요. (저장은 선택입니다 — 다른 AI를 더 돌려보고 결정해도 됩니다.)";
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

    /// <summary>
    /// Persists the current AI response as a <see cref="RebalanceSession"/> JSON in the active
    /// history folder (sync folder when configured, else LocalState\History\). Idempotent —
    /// flips <see cref="HasSavedCurrentResponse"/> so the button greys out after one save.
    /// </summary>
    public string? SaveCurrentResponseToHistory()
    {
        if (!HasResponse) return null;

        var account = AppState.Instance.Account;
        var catalog = AppState.Instance.Catalog;

        var meta = new RebalanceSessionMeta(
            Timestamp: _lastResponseAt == default ? DateTime.Now : _lastResponseAt,
            ProviderName: string.IsNullOrEmpty(_lastProviderName) ? AppState.Instance.Settings.AiProvider : _lastProviderName,
            ModelId: string.IsNullOrEmpty(_lastModelId) ? AppState.Instance.Settings.GetActiveModel() : _lastModelId,
            ThinkingLevel: _lastThinkingLevel,
            HoldingsCount: account.OwnedItems.Count,
            TotalAmount: account.TotalAmount,
            CatalogPrincipalGuaranteedCount: catalog?.PrincipalGuaranteed.Count ?? 0,
            CatalogFundCount: catalog?.Funds.Count ?? 0);

        var session = new RebalanceSession(
            Meta: meta,
            Account: CloneAccount(account),
            UserAdditionalQuery: UserQuery ?? string.Empty,
            RecommendationMarkdown: AiResponse);

        try
        {
            var folder = AppState.Instance.ActiveHistoryFolder;
            var path = RebalanceHistoryStore.Save(folder, session);
            HasSavedCurrentResponse = true;
            StatusMessage = $"이력에 저장됐습니다: {path}";
            RefreshHistoryEntries();
            return path;
        }
        catch (Exception ex)
        {
            StatusMessage = $"이력 저장 오류: {ex.Message}";
            return null;
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

    /// <summary>
    /// Loads the full session document for the user-selected combo entry, caching it so we don't
    /// re-read the file on every prompt rebuild. Resets when the user changes selection. Also
    /// recomputes <see cref="PriorOutcome"/> against the live account state on each fresh load
    /// so the comparison stays in step if the user edits their account between selections.
    /// </summary>
    private RebalanceSession? ResolvePriorSession()
    {
        if (SelectedPriorEntry is null)
        {
            _resolvedPriorSession = null;
            PriorOutcome = null;
            return null;
        }
        if (_resolvedPriorSession?.Meta.Timestamp == SelectedPriorEntry.Meta.Timestamp)
            return _resolvedPriorSession;
        _resolvedPriorSession = RebalanceHistoryStore.Load(SelectedPriorEntry.FilePath);
        PriorOutcome = _resolvedPriorSession is not null
            ? PeriodComparisonCalculator.Compare(_resolvedPriorSession, AppState.Instance.Account, DateTime.Now)
            : null;
        return _resolvedPriorSession;
    }

    partial void OnSelectedPriorEntryChanged(RebalanceSessionEntry? value)
    {
        // Invalidate the cached document and outcome.
        _resolvedPriorSession = null;
        PriorOutcome = null;
        // Eagerly load + compute so the outcome banner appears immediately on selection,
        // not only after the user clicks Generate / Preview. File is small (<100 KB).
        if (value is not null) ResolvePriorSession();
    }

    /// <summary>
    /// Snapshots the account into a fresh model so the history file is independent of subsequent
    /// edits to the live account. Mirrors the pattern <see cref="ViewModels.DataPreparationViewModel.Clone"/>
    /// uses for portfolio CSV export.
    /// </summary>
    private static AccountStatusModel CloneAccount(AccountStatusModel source) => new()
    {
        TotalAmount = source.TotalAmount,
        DepositAmount = source.DepositAmount,
        ProfitAmount = source.ProfitAmount,
        RebalanceTiming = source.RebalanceTiming,
        ExecutionDate = source.ExecutionDate,
        RebalanceCycle = source.RebalanceCycle,
        CurrentAge = source.CurrentAge,
        DesiredAnnuityStartAge = source.DesiredAnnuityStartAge,
        WantsLifelongAnnuity = source.WantsLifelongAnnuity,
        MonthlyContribution = source.MonthlyContribution,
        OtherRetirementAssets = source.OtherRetirementAssets,
        OwnedItems = source.OwnedItems.Select(h => new OwnedProductModel
        {
            ProductName = h.ProductName,
            CurrentValue = h.CurrentValue,
            ReturnRate = h.ReturnRate,
            AnnualizedReturn = h.AnnualizedReturn,
            InvestedDays = h.InvestedDays,
            TotalShares = h.TotalShares,
            IsSellable = h.IsSellable,
        }).ToList(),
    };
}
