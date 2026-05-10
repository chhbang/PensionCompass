using PensionCompass.Core.Ai;
using PensionCompass.Core.History;
using PensionCompass.Core.Models;

namespace PensionCompass.Core.Tests;

public class PromptBuilderTests
{
    private static AccountStatusModel SampleAccount() => new()
    {
        TotalAmount = 192_808_229m,
        DepositAmount = 181_000_000m,
        ProfitAmount = 11_808_229m,
        RebalanceTiming = RebalanceTiming.Immediate,
        OwnedItems =
        [
            new OwnedProductModel
            {
                ProductName = "이율보증형(3년)",
                CurrentValue = 30_646_260m,
                IsSellable = false,
            },
            new OwnedProductModel
            {
                ProductName = "[온라인]우리BIG2플러스증권(채권혼)ClassC-Pe",
                CurrentValue = 23_129_650m,
                ReturnRate = 14.17m,
                IsSellable = true,
            },
        ],
    };

    private static ProductCatalog SampleCatalog() => new(
        PrincipalGuaranteed:
        [
            new("C02011", "무배당 메리츠 신탁제공용 이율보증형보험Ⅲ (IRP 3년)", "메리츠", "3.80%", "3년"),
        ],
        Funds:
        [
            new("G04783", "[온라인전용]미래에셋코어테크증권투자신탁(주식)", "미래에셋자산운용㈜", "매우높은위험",
                new Dictionary<ReturnPeriod, string> { [ReturnPeriod.Month3] = "45.00%" }),
        ],
        FundReturnPeriods: [ReturnPeriod.Month3]);

    [Fact]
    public void Build_AlwaysProducesKoreanSystemPrompt()
    {
        var output = PromptBuilder.Build(new PromptInput(null, new AccountStatusModel(), ""));

        Assert.Contains("IRP", output.SystemPrompt);
        Assert.Contains("한국어", output.SystemPrompt);
    }

    [Fact]
    public void Build_ImmediateTiming_DoesNotIncludeExecutionDate()
    {
        var account = SampleAccount();
        account.RebalanceTiming = RebalanceTiming.Immediate;
        account.ExecutionDate = null;

        var output = PromptBuilder.Build(new PromptInput(null, account, ""));

        Assert.Contains("최대한 즉시", output.UserPrompt);
        Assert.DoesNotContain("실행 예정일", output.UserPrompt);
    }

    [Fact]
    public void Build_MaturityReservation_IncludesExecutionDate()
    {
        var account = SampleAccount();
        account.RebalanceTiming = RebalanceTiming.MaturityReservation;
        account.ExecutionDate = new DateOnly(2027, 9, 20);

        var output = PromptBuilder.Build(new PromptInput(null, account, ""));

        Assert.Contains("만기 예약용", output.UserPrompt);
        Assert.Contains("2027년 9월 20일", output.UserPrompt);
    }

    [Fact]
    public void Build_LifelongAnnuity_IncludesStrictManagerConstraint()
    {
        var account = SampleAccount();
        account.WantsLifelongAnnuity = true;
        var output = PromptBuilder.Build(new PromptInput(null, account, ""));

        Assert.Contains("운용사 제약", output.UserPrompt);
        Assert.Contains("삼성생명보험주식회사", output.UserPrompt);
        // The constraint must explicitly call out Samsung affiliates as a separate legal entity
        // — they don't qualify for the lifelong-annuity payout.
        Assert.Contains("삼성자산운용", output.UserPrompt);
    }

    [Fact]
    public void Build_NoLifelongAnnuity_OmitsManagerConstraint()
    {
        var account = SampleAccount();
        account.WantsLifelongAnnuity = false;
        var output = PromptBuilder.Build(new PromptInput(null, account, ""));

        Assert.DoesNotContain("운용사 제약", output.UserPrompt);
    }

    [Fact]
    public void Build_SubscriberInfo_RendersAgesAndComputesYearsToStart()
    {
        var account = SampleAccount();
        account.CurrentAge = 45;
        account.DesiredAnnuityStartAge = 60;
        account.WantsLifelongAnnuity = true;

        var output = PromptBuilder.Build(new PromptInput(null, account, ""));

        Assert.Contains("## 가입자 정보 / 희망사항", output.UserPrompt);
        Assert.Contains("만 45세", output.UserPrompt);
        Assert.Contains("만 60세", output.UserPrompt);
        Assert.Contains("약 15년 후", output.UserPrompt);
        Assert.Contains("종신 지급형 연금 수령 의향: 예", output.UserPrompt);
    }

    [Fact]
    public void Build_NoSubscriberInfo_OmitsSection()
    {
        // No ages set, lifelong-annuity off, no contribution, no other assets → skip the section entirely.
        var account = SampleAccount();
        account.CurrentAge = null;
        account.DesiredAnnuityStartAge = null;
        account.WantsLifelongAnnuity = false;
        account.MonthlyContribution = null;
        account.OtherRetirementAssets = "";

        var output = PromptBuilder.Build(new PromptInput(null, account, ""));

        Assert.DoesNotContain("## 가입자 정보", output.UserPrompt);
    }

    [Fact]
    public void Build_MonthlyContribution_RendersAmountAndDcaNote()
    {
        var account = SampleAccount();
        account.MonthlyContribution = 500_000m;

        var output = PromptBuilder.Build(new PromptInput(null, account, ""));

        Assert.Contains("매월 ₩500,000 적립식", output.UserPrompt);
        Assert.Contains("DCA", output.UserPrompt);
    }

    [Fact]
    public void Build_NoMonthlyContribution_StatesNoneExplicitly()
    {
        var account = SampleAccount();
        account.CurrentAge = 45; // ensure section renders
        account.MonthlyContribution = null;

        var output = PromptBuilder.Build(new PromptInput(null, account, ""));

        Assert.Contains("추가 납입 계획: 없음", output.UserPrompt);
    }

    [Fact]
    public void Build_OtherRetirementAssets_RendersAsBulletedSubItems()
    {
        var account = SampleAccount();
        account.OtherRetirementAssets = "국민연금 가입 중\n주택 1채 보유\n";

        var output = PromptBuilder.Build(new PromptInput(null, account, ""));

        Assert.Contains("다른 노후 자산", output.UserPrompt);
        Assert.Contains("  - 국민연금 가입 중", output.UserPrompt);
        Assert.Contains("  - 주택 1채 보유", output.UserPrompt);
    }

    [Fact]
    public void Build_SubscriberInfo_IncludesMacroOverPreferenceGuidance()
    {
        // The user has explicitly rejected risk-tolerance inputs because they want AI to weigh macro
        // conditions, not mirror the user's stated preference. The prompt must spell that bias out so
        // the AI doesn't anchor on a holdings-implied "공격형" profile when conditions don't warrant it.
        var account = SampleAccount();
        account.CurrentAge = 45;

        var output = PromptBuilder.Build(new PromptInput(null, account, ""));

        Assert.Contains("거시·시장 환경", output.UserPrompt);
        Assert.Contains("듣고 싶은 답이 아니더라도", output.UserPrompt);
    }

    [Fact]
    public void Build_DefaultCycle_RendersSixMonthSection()
    {
        // SampleAccount doesn't set RebalanceCycle explicitly, so the model default (SixMonths) applies.
        var output = PromptBuilder.Build(new PromptInput(null, SampleAccount(), ""));

        Assert.Contains("## 리밸런싱 주기", output.UserPrompt);
        Assert.Contains("**6개월**", output.UserPrompt);
    }

    [Fact]
    public void Build_ThreeMonthCycle_FlagsShortHorizonGuidance()
    {
        var account = SampleAccount();
        account.RebalanceCycle = RebalanceCycle.ThreeMonths;

        var output = PromptBuilder.Build(new PromptInput(null, account, ""));

        Assert.Contains("**3개월**", output.UserPrompt);
        // Short horizon should steer the AI toward shorter return columns and acknowledge noise risk.
        Assert.Contains("1·3개월 수익률", output.UserPrompt);
    }

    [Fact]
    public void Build_OneYearCycle_FlagsLongHorizonGuidance()
    {
        var account = SampleAccount();
        account.RebalanceCycle = RebalanceCycle.OneYear;

        var output = PromptBuilder.Build(new PromptInput(null, account, ""));

        Assert.Contains("**1년**", output.UserPrompt);
        // Long horizon should steer toward longer-period signals and conservative volatility.
        Assert.Contains("6개월·1년·3년", output.UserPrompt);
    }

    [Fact]
    public void Build_HoldingsTable_DistinguishesSellableFromLocked()
    {
        var output = PromptBuilder.Build(new PromptInput(null, SampleAccount(), ""));

        // The 이율보증형 row was IsSellable=false, the bond fund row was IsSellable=true.
        Assert.Contains("매도 금지", output.UserPrompt);
        Assert.Contains("매도 가능", output.UserPrompt);
        Assert.Contains("이율보증형(3년)", output.UserPrompt);
        Assert.Contains("₩30,646,260", output.UserPrompt);
        Assert.Contains("14.17%", output.UserPrompt);
    }

    [Fact]
    public void Build_Catalog_RendersBothPrincipalGuaranteedAndFundsTables()
    {
        var output = PromptBuilder.Build(new PromptInput(SampleCatalog(), SampleAccount(), ""));

        Assert.Contains("### 원리금보장형", output.UserPrompt);
        Assert.Contains("### 펀드", output.UserPrompt);
        Assert.Contains("미래에셋코어테크", output.UserPrompt);
        Assert.Contains("45.00%", output.UserPrompt);
    }

    [Fact]
    public void Build_AlwaysIncludesIrpLegalConstraintsSection()
    {
        var output = PromptBuilder.Build(new PromptInput(SampleCatalog(), SampleAccount(), ""));

        Assert.Contains("## IRP 법적 제약", output.UserPrompt);
        Assert.Contains("70%", output.UserPrompt);
        Assert.Contains("30%", output.UserPrompt);
    }

    [Fact]
    public void Build_PrincipalGuaranteedSection_DeclaresThemAsSafeAssets()
    {
        var output = PromptBuilder.Build(new PromptInput(SampleCatalog(), SampleAccount(), ""));

        Assert.Contains("원리금보장형 상품은 모두 IRP 분류상 **안정자산**", output.UserPrompt);
    }

    [Fact]
    public void Build_FundTable_IncludesAssetClassColumn()
    {
        var catalog = new ProductCatalog(
            PrincipalGuaranteed: [],
            Funds:
            [
                new("G04783", "고위험펀드", "운용사A", "매우높은위험",
                    new Dictionary<ReturnPeriod, string> { [ReturnPeriod.Month3] = "30.00%" },
                    AssetClass: "위험자산"),
            ],
            FundReturnPeriods: [ReturnPeriod.Month3]);

        var output = PromptBuilder.Build(new PromptInput(catalog, SampleAccount(), ""));

        // Header row carries the 자산구분 column.
        Assert.Contains("자산구분", output.UserPrompt);
        // Body row contains the actual classification value.
        Assert.Contains("위험자산", output.UserPrompt);
    }

    [Fact]
    public void Build_NullCatalog_NotesItIsMissing()
    {
        var output = PromptBuilder.Build(new PromptInput(null, SampleAccount(), ""));

        Assert.Contains("상품 카탈로그가 입력되지 않았습니다", output.UserPrompt);
    }

    [Fact]
    public void Build_UserAddendum_AppearsVerbatim()
    {
        var query = "이란 전쟁 상황을 반영해주세요.";
        var output = PromptBuilder.Build(new PromptInput(null, SampleAccount(), query));

        Assert.Contains(query, output.UserPrompt);
    }

    [Fact]
    public void Build_EmptyAddendum_RendersAsNone()
    {
        var output = PromptBuilder.Build(new PromptInput(null, SampleAccount(), ""));

        Assert.Contains("## 사용자 추가 요구사항", output.UserPrompt);
        Assert.Contains("(없음)", output.UserPrompt);
    }

    [Fact]
    public void Build_NoPriorSession_OmitsPriorSection()
    {
        var output = PromptBuilder.Build(new PromptInput(null, SampleAccount(), "", PriorSession: null));

        Assert.DoesNotContain("## 직전 리밸런싱", output.UserPrompt);
    }

    [Fact]
    public void Build_WithPriorOutcome_EmbedsRealizedReturnSection()
    {
        var outcome = new PeriodComparison(
            PriorTimestamp: new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Local),
            CurrentTimestamp: new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Local),
            DaysElapsed: 89,
            PriorTotal: 100_000_000m,
            CurrentTotal: 110_000_000m,
            NetContribution: 3_000_000m,
            PeriodReturnPercent: 7m,
            AnnualizedReturnPercent: 31m,
            ContributionSource: ContributionSource.DepositAmountDelta,
            HoldingChanges: []);

        var output = PromptBuilder.Build(new PromptInput(
            null, SampleAccount(), "", PriorSession: null, PriorOutcome: outcome));

        Assert.Contains("## 직전 회차 → 현재 시점 실제 성과", output.UserPrompt);
        Assert.Contains("89일", output.UserPrompt);
        Assert.Contains("+7.00%", output.UserPrompt);
        Assert.Contains("연환산 약 +31.00%", output.UserPrompt);
        Assert.Contains("DepositAmount 델타 기준 — 정확", output.UserPrompt);
    }

    [Fact]
    public void Build_WithPriorOutcome_NoteWhenContributionUnavailable()
    {
        var outcome = new PeriodComparison(
            PriorTimestamp: new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Local),
            CurrentTimestamp: new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Local),
            DaysElapsed: 89,
            PriorTotal: 100_000_000m,
            CurrentTotal: 110_000_000m,
            NetContribution: null,
            PeriodReturnPercent: 10m,
            AnnualizedReturnPercent: null,
            ContributionSource: ContributionSource.Unavailable,
            HoldingChanges: []);

        var output = PromptBuilder.Build(new PromptInput(
            null, SampleAccount(), "", PriorSession: null, PriorOutcome: outcome));

        Assert.Contains("기간 수익률 (입금 차감 안 됨, gross): +10.00%", output.UserPrompt);
        Assert.Contains("정보 없음", output.UserPrompt);
    }

    [Fact]
    public void Build_NoPriorOutcome_OmitsRealizedSection()
    {
        var output = PromptBuilder.Build(new PromptInput(null, SampleAccount(), ""));

        Assert.DoesNotContain("## 직전 회차 → 현재 시점 실제 성과", output.UserPrompt);
    }

    [Fact]
    public void Build_WithPriorSession_EmbedsTimestampAiAndMarkdown()
    {
        var prior = new RebalanceSession(
            Meta: new RebalanceSessionMeta(
                Timestamp: new DateTime(2026, 2, 4, 10, 0, 0, DateTimeKind.Local),
                ProviderName: "Claude",
                ModelId: "claude-opus-4-7",
                ThinkingLevel: ThinkingLevel.High,
                HoldingsCount: 5,
                TotalAmount: 150_000_000m,
                CatalogPrincipalGuaranteedCount: 0,
                CatalogFundCount: 0),
            Account: new AccountStatusModel(),
            UserAdditionalQuery: "그때는 안정 자산 비중을 늘려달라고 했음",
            RecommendationMarkdown: "# 그때 받은 추천\n\n매도 후보: 펀드 X 30%");

        var output = PromptBuilder.Build(new PromptInput(null, SampleAccount(), "", PriorSession: prior));

        Assert.Contains("## 직전 리밸런싱", output.UserPrompt);
        Assert.Contains("2026년 2월 4일", output.UserPrompt);
        Assert.Contains("Claude (claude-opus-4-7", output.UserPrompt);
        // Prior user query gets quoted as a blockquote line so the AI can distinguish then-input.
        Assert.Contains("> 그때는 안정 자산 비중을 늘려달라고 했음", output.UserPrompt);
        // Recommendation markdown is embedded verbatim so the AI can read what was said.
        Assert.Contains("매도 후보: 펀드 X 30%", output.UserPrompt);
        // Guidance must be present so the AI doesn't simply parrot the prior recommendation.
        Assert.Contains("일관성 있는 장기 전략을 유지하되, 단순히 이전 추천을 답습하지는 말고", output.UserPrompt);
    }
}
