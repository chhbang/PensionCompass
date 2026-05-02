using SLifeIrpRebalancer.Core.Ai;
using SLifeIrpRebalancer.Core.Models;

namespace SLifeIrpRebalancer.Core.Tests;

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
        // No ages set, lifelong-annuity off → the section adds no signal, so it's skipped entirely
        // rather than rendering empty bullets.
        var account = SampleAccount();
        account.CurrentAge = null;
        account.DesiredAnnuityStartAge = null;
        account.WantsLifelongAnnuity = false;

        var output = PromptBuilder.Build(new PromptInput(null, account, ""));

        Assert.DoesNotContain("## 가입자 정보", output.UserPrompt);
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
}
