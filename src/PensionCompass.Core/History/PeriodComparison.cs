using PensionCompass.Core.Models;

namespace PensionCompass.Core.History;

/// <summary>
/// One holding's value at the start of a comparison window vs the end. Either side can be
/// null when the user added the product after the prior snapshot, or removed it before the
/// later snapshot. Comparison key is the literal product name.
/// </summary>
public sealed record HoldingChange(
    string ProductName,
    decimal? PriorValue,
    decimal? CurrentValue)
{
    public decimal? AbsoluteChange => PriorValue.HasValue && CurrentValue.HasValue
        ? CurrentValue.Value - PriorValue.Value
        : null;

    public HoldingChangeKind Kind =>
        (PriorValue.HasValue, CurrentValue.HasValue) switch
        {
            (true, true) => HoldingChangeKind.Held,
            (true, false) => HoldingChangeKind.Sold,
            (false, true) => HoldingChangeKind.Bought,
            _ => HoldingChangeKind.Held, // unreachable but compiler wants it
        };
}

public enum HoldingChangeKind
{
    /// <summary>Present in both snapshots.</summary>
    Held,
    /// <summary>Present in the prior snapshot only — sold or removed during the period.</summary>
    Sold,
    /// <summary>Present in the current snapshot only — bought during the period.</summary>
    Bought,
}

/// <summary>
/// How <see cref="PeriodComparison.NetContribution"/> was determined. Affects how trustworthy
/// <see cref="PeriodComparison.PeriodReturnPercent"/> is — a deposit-delta is exact, a monthly
/// estimate is rough, and "unavailable" means the return is gross of any contributions.
/// </summary>
public enum ContributionSource
{
    /// <summary>Net contribution = current.DepositAmount − prior.DepositAmount (most accurate).</summary>
    DepositAmountDelta,
    /// <summary>Net contribution estimated from MonthlyContribution × elapsed months.</summary>
    MonthlyEstimate,
    /// <summary>Could not determine net contribution; period return is gross of cash flows.</summary>
    Unavailable,
}

/// <summary>
/// Realized outcome of a past rebalancing session — how the user's portfolio actually moved
/// between the time the session was saved and a later snapshot (current account or another
/// saved session). Pure value object; the calculator below produces it.
/// </summary>
public sealed record PeriodComparison(
    DateTime PriorTimestamp,
    DateTime CurrentTimestamp,
    int DaysElapsed,
    decimal PriorTotal,
    decimal CurrentTotal,
    decimal? NetContribution,
    decimal? PeriodReturnPercent,
    decimal? AnnualizedReturnPercent,
    ContributionSource ContributionSource,
    IReadOnlyList<HoldingChange> HoldingChanges);

/// <summary>
/// Pure calculator. No I/O, no rounding for display — produces the raw numbers; UI layers
/// format them.
/// </summary>
public static class PeriodComparisonCalculator
{
    /// <summary>
    /// Compares a saved session against the current (live) account state. The current state
    /// has no timestamp of its own, so the caller passes one (typically <c>DateTime.Now</c> or
    /// the time the user opened the rebalance screen).
    /// </summary>
    public static PeriodComparison Compare(
        RebalanceSession prior,
        AccountStatusModel current,
        DateTime currentTimestamp)
    {
        var priorTs = prior.Meta.Timestamp;
        var daysElapsed = Math.Max(0, (int)(currentTimestamp - priorTs).TotalDays);

        var priorTotal = prior.Account.TotalAmount;
        var currentTotal = current.TotalAmount;

        var (netContribution, contributionSource) = ResolveNetContribution(
            prior.Account, current, daysElapsed);

        decimal? periodReturnPct = null;
        decimal? annualizedPct = null;
        if (priorTotal > 0)
        {
            // Subtract net contribution when known so the return reflects investment performance,
            // not merely cash inflow. When unknown, the result is gross of contributions and the
            // ContributionSource flag tells the caller to caveat accordingly.
            var contribAdjustment = netContribution ?? 0m;
            var pnl = currentTotal - priorTotal - contribAdjustment;
            periodReturnPct = pnl / priorTotal * 100m;

            // Compound-annualize only if the period is non-trivially long; for sub-monthly
            // windows the annualized number would be wildly noisy and misleading.
            if (daysElapsed >= 30 && periodReturnPct is { } pct)
            {
                var rDecimal = (double)pct / 100.0;
                var growthFactor = 1.0 + rDecimal;
                if (growthFactor > 0.0)
                {
                    var annualized = Math.Pow(growthFactor, 365.0 / daysElapsed) - 1.0;
                    annualizedPct = (decimal)(annualized * 100.0);
                }
            }
        }

        var holdingChanges = BuildHoldingChanges(prior.Account.OwnedItems, current.OwnedItems);

        return new PeriodComparison(
            PriorTimestamp: priorTs,
            CurrentTimestamp: currentTimestamp,
            DaysElapsed: daysElapsed,
            PriorTotal: priorTotal,
            CurrentTotal: currentTotal,
            NetContribution: netContribution,
            PeriodReturnPercent: periodReturnPct,
            AnnualizedReturnPercent: annualizedPct,
            ContributionSource: contributionSource,
            HoldingChanges: holdingChanges);
    }

    /// <summary>Compares two saved sessions (e.g. session N vs session N+1 in the history list).</summary>
    public static PeriodComparison Compare(RebalanceSession prior, RebalanceSession current)
        => Compare(prior, current.Account, current.Meta.Timestamp);

    /// <summary>
    /// Picks the best available signal for net contribution during the period:
    /// 1. DepositAmount delta — exact when both snapshots have it.
    /// 2. MonthlyContribution × elapsed months — rough but better than nothing.
    /// 3. Unavailable — return is reported gross of cash flows.
    /// </summary>
    private static (decimal? Amount, ContributionSource Source) ResolveNetContribution(
        AccountStatusModel prior, AccountStatusModel current, int daysElapsed)
    {
        if (prior.DepositAmount.HasValue && current.DepositAmount.HasValue)
        {
            var delta = current.DepositAmount.Value - prior.DepositAmount.Value;
            // Negative delta would imply withdrawal — possible but rare in IRP. Pass through as-is;
            // it will reduce net contribution and slightly inflate the implied return, which is correct.
            return (delta, ContributionSource.DepositAmountDelta);
        }

        var monthly = current.MonthlyContribution ?? prior.MonthlyContribution;
        if (monthly is > 0m && daysElapsed > 0)
        {
            var months = daysElapsed / 30m; // approximate; user may have skipped or doubled some months
            return (monthly.Value * months, ContributionSource.MonthlyEstimate);
        }

        return (null, ContributionSource.Unavailable);
    }

    private static List<HoldingChange> BuildHoldingChanges(
        IReadOnlyList<OwnedProductModel> prior,
        IReadOnlyList<OwnedProductModel> current)
    {
        // Use a name-keyed dictionary; collisions inside a single side are coalesced by summing,
        // which mirrors how a user might end up with the same product appearing on two rows.
        var priorByName = prior
            .GroupBy(h => h.ProductName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(h => h.CurrentValue), StringComparer.Ordinal);
        var currentByName = current
            .GroupBy(h => h.ProductName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(h => h.CurrentValue), StringComparer.Ordinal);

        var allNames = new HashSet<string>(priorByName.Keys, StringComparer.Ordinal);
        foreach (var name in currentByName.Keys) allNames.Add(name);

        return allNames
            .Select(name => new HoldingChange(
                ProductName: name,
                PriorValue: priorByName.TryGetValue(name, out var p) ? p : null,
                CurrentValue: currentByName.TryGetValue(name, out var c) ? c : null))
            // Sort for stable presentation: held items first (largest current value first),
            // then bought, then sold.
            .OrderBy(h => h.Kind switch
            {
                HoldingChangeKind.Held => 0,
                HoldingChangeKind.Bought => 1,
                HoldingChangeKind.Sold => 2,
                _ => 3,
            })
            .ThenByDescending(h => h.CurrentValue ?? h.PriorValue ?? 0m)
            .ToList();
    }
}
