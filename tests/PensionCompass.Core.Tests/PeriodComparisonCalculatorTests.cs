using PensionCompass.Core.Ai;
using PensionCompass.Core.History;
using PensionCompass.Core.Models;

namespace PensionCompass.Core.Tests;

public class PeriodComparisonCalculatorTests
{
    private static readonly DateTime PriorTs = new(2026, 2, 1, 10, 0, 0, DateTimeKind.Local);
    private static readonly DateTime CurrentTs = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Local); // ~89 days later

    private static RebalanceSession BuildPrior(decimal totalAmount, decimal? depositAmount, decimal? monthlyContribution = null, params (string name, decimal value)[] holdings)
    {
        var account = new AccountStatusModel
        {
            TotalAmount = totalAmount,
            DepositAmount = depositAmount,
            MonthlyContribution = monthlyContribution,
            OwnedItems = holdings.Select(h => new OwnedProductModel
            {
                ProductName = h.name,
                CurrentValue = h.value,
            }).ToList(),
        };
        var meta = new RebalanceSessionMeta(
            Timestamp: PriorTs,
            ProviderName: "Claude",
            ModelId: "claude-opus-4-7",
            ThinkingLevel: ThinkingLevel.High,
            HoldingsCount: account.OwnedItems.Count,
            TotalAmount: totalAmount,
            CatalogPrincipalGuaranteedCount: 0,
            CatalogFundCount: 0);
        return new RebalanceSession(meta, account, "", "# 그때 추천\n\n매도 X 매수 Y");
    }

    private static AccountStatusModel BuildCurrent(decimal totalAmount, decimal? depositAmount = null, decimal? monthlyContribution = null, params (string name, decimal value)[] holdings)
        => new()
        {
            TotalAmount = totalAmount,
            DepositAmount = depositAmount,
            MonthlyContribution = monthlyContribution,
            OwnedItems = holdings.Select(h => new OwnedProductModel
            {
                ProductName = h.name,
                CurrentValue = h.value,
            }).ToList(),
        };

    [Fact]
    public void Compare_DepositDeltaIsPreferred_OverMonthlyEstimate()
    {
        // Prior 100M, current 110M, deposits went 50M → 53M (3M added during the period).
        // Real return: (110M - 100M - 3M) / 100M = 7%. The 3M-from-monthly estimate would
        // have been different; deposit delta should win when both signals are available.
        var prior = BuildPrior(100_000_000m, depositAmount: 50_000_000m, monthlyContribution: 1_000_000m);
        var current = BuildCurrent(110_000_000m, depositAmount: 53_000_000m, monthlyContribution: 1_000_000m);

        var result = PeriodComparisonCalculator.Compare(prior, current, CurrentTs);

        Assert.Equal(ContributionSource.DepositAmountDelta, result.ContributionSource);
        Assert.Equal(3_000_000m, result.NetContribution);
        Assert.Equal(7m, result.PeriodReturnPercent);
    }

    [Fact]
    public void Compare_FallsBackToMonthlyEstimate_WhenDepositMissing()
    {
        // No DepositAmount on either side, but monthly is set.
        var prior = BuildPrior(100_000_000m, depositAmount: null, monthlyContribution: 1_000_000m);
        var current = BuildCurrent(110_000_000m, depositAmount: null, monthlyContribution: 1_000_000m);

        var result = PeriodComparisonCalculator.Compare(prior, current, CurrentTs);

        Assert.Equal(ContributionSource.MonthlyEstimate, result.ContributionSource);
        // 89 days / 30 ≈ 2.97 months × 1M = 2.97M estimated contribution.
        Assert.NotNull(result.NetContribution);
        Assert.True(result.NetContribution!.Value > 2_900_000m);
        Assert.True(result.NetContribution!.Value < 3_000_000m);
    }

    [Fact]
    public void Compare_MarksContributionUnavailable_WhenNoSignals()
    {
        var prior = BuildPrior(100_000_000m, depositAmount: null);
        var current = BuildCurrent(110_000_000m, depositAmount: null);

        var result = PeriodComparisonCalculator.Compare(prior, current, CurrentTs);

        Assert.Equal(ContributionSource.Unavailable, result.ContributionSource);
        Assert.Null(result.NetContribution);
        // Without contribution data, return is gross: 10M / 100M = 10%.
        Assert.Equal(10m, result.PeriodReturnPercent);
    }

    [Fact]
    public void Compare_ProducesAnnualizedReturn_WhenPeriodAtLeastOneMonth()
    {
        // 89 days, +10% gross. Annualized ≈ (1.10)^(365/89) - 1 ≈ 47%.
        var prior = BuildPrior(100_000_000m, depositAmount: null);
        var current = BuildCurrent(110_000_000m, depositAmount: null);

        var result = PeriodComparisonCalculator.Compare(prior, current, CurrentTs);

        Assert.NotNull(result.AnnualizedReturnPercent);
        Assert.True(result.AnnualizedReturnPercent!.Value > 40m);
        Assert.True(result.AnnualizedReturnPercent!.Value < 55m);
    }

    [Fact]
    public void Compare_DoesNotAnnualize_ForSubMonthlyPeriods()
    {
        // 5 days only — annualizing would explode wildly, so the calculator should skip it.
        var prior = BuildPrior(100_000_000m, depositAmount: null);
        var current = BuildCurrent(101_000_000m, depositAmount: null);

        var result = PeriodComparisonCalculator.Compare(
            prior, current, currentTimestamp: PriorTs.AddDays(5));

        Assert.NotNull(result.PeriodReturnPercent);
        Assert.Null(result.AnnualizedReturnPercent);
    }

    [Fact]
    public void Compare_HoldingChanges_ClassifiesHeldBoughtSold()
    {
        var prior = BuildPrior(100_000_000m, null, null,
            ("이율보증형(3년)", 30_000_000m),
            ("BIG2플러스혼합", 70_000_000m)); // user will sell this one
        var current = BuildCurrent(110_000_000m,
            holdings: new[]
            {
                ("이율보증형(3년)", 31_000_000m),  // held; small gain
                ("미래에셋퇴직플랜", 79_000_000m),    // newly bought
            });

        var result = PeriodComparisonCalculator.Compare(prior, current, CurrentTs);

        var byName = result.HoldingChanges.ToDictionary(h => h.ProductName);

        Assert.Equal(HoldingChangeKind.Held, byName["이율보증형(3년)"].Kind);
        Assert.Equal(1_000_000m, byName["이율보증형(3년)"].AbsoluteChange);

        Assert.Equal(HoldingChangeKind.Sold, byName["BIG2플러스혼합"].Kind);
        Assert.Equal(70_000_000m, byName["BIG2플러스혼합"].PriorValue);
        Assert.Null(byName["BIG2플러스혼합"].CurrentValue);

        Assert.Equal(HoldingChangeKind.Bought, byName["미래에셋퇴직플랜"].Kind);
        Assert.Null(byName["미래에셋퇴직플랜"].PriorValue);
    }

    [Fact]
    public void Compare_DaysElapsedNeverNegative()
    {
        // Defensive: if the user fed in a current timestamp earlier than the prior session
        // (clock skew, manual edit), daysElapsed clamps to 0.
        var prior = BuildPrior(100_000_000m, null);
        var current = BuildCurrent(100_000_000m);

        var result = PeriodComparisonCalculator.Compare(
            prior, current, currentTimestamp: PriorTs.AddDays(-5));

        Assert.Equal(0, result.DaysElapsed);
    }

    [Fact]
    public void Compare_ZeroPriorTotal_ReturnsNullReturn()
    {
        // Avoids division by zero; the return is undefined for a zero starting balance.
        var prior = BuildPrior(0m, depositAmount: 0m);
        var current = BuildCurrent(1_000_000m, depositAmount: 1_000_000m);

        var result = PeriodComparisonCalculator.Compare(prior, current, CurrentTs);

        Assert.Null(result.PeriodReturnPercent);
        Assert.Null(result.AnnualizedReturnPercent);
    }

    [Fact]
    public void Compare_TwoSessions_OverloadDelegatesToAccountVersion()
    {
        var prior = BuildPrior(100_000_000m, depositAmount: 50_000_000m);

        // Build a "current" session that's actually 89 days later.
        var currentAccount = BuildCurrent(110_000_000m, depositAmount: 53_000_000m);
        var currentMeta = new RebalanceSessionMeta(
            CurrentTs, "Gemini", "gemini-3.1-pro", ThinkingLevel.High,
            HoldingsCount: 0, TotalAmount: 110_000_000m,
            CatalogPrincipalGuaranteedCount: 0, CatalogFundCount: 0);
        var current = new RebalanceSession(currentMeta, currentAccount, "", "# 새 추천");

        var result = PeriodComparisonCalculator.Compare(prior, current);

        Assert.Equal(7m, result.PeriodReturnPercent);
        Assert.Equal(CurrentTs, result.CurrentTimestamp);
    }
}
