using System.Globalization;
using PensionCompass.Core.History;

namespace PensionCompass.ViewModels;

/// <summary>
/// View row that pairs a saved <see cref="RebalanceSessionEntry"/> with the realized period
/// outcome between this session and the next-newer one (or the current live account, when it's
/// the newest entry). Forwards the entry's identity props so the existing XAML bindings continue
/// to work, then adds <see cref="PeriodReturnLabel"/> for the new column.
/// </summary>
public sealed class HistoryEntryRow
{
    public RebalanceSessionEntry Entry { get; }
    public PeriodComparison? Comparison { get; }

    public HistoryEntryRow(RebalanceSessionEntry entry, PeriodComparison? comparison)
    {
        Entry = entry;
        Comparison = comparison;
    }

    public string FilePath => Entry.FilePath;
    public RebalanceSessionMeta Meta => Entry.Meta;
    public string DisplayLabel => Entry.DisplayLabel;

    /// <summary>
    /// One-line period-return summary suitable for the list row caption.
    /// Returns an empty string when there's no later snapshot to compare against (e.g. a
    /// single-session history).
    /// </summary>
    public string PeriodReturnLabel
    {
        get
        {
            if (Comparison is not { } c) return string.Empty;
            if (c.DaysElapsed <= 0) return string.Empty;

            var anchor = c.CurrentTimestamp.ToLocalTime().Date == DateTime.Now.Date
                ? "현재 대비"
                : $"{c.CurrentTimestamp.ToLocalTime():yyyy-MM-dd} 대비";

            if (c.PeriodReturnPercent is not { } pct)
                return $"{anchor} {c.DaysElapsed}일 — 수익률 산정 불가";

            var sign = pct >= 0 ? "+" : "";
            var caveat = c.ContributionSource switch
            {
                ContributionSource.Unavailable => " (gross)",
                ContributionSource.MonthlyEstimate => " (근사)",
                _ => "",
            };
            return string.Create(CultureInfo.CurrentCulture,
                $"{anchor} {c.DaysElapsed}일 — 운용수익 {sign}{pct:0.00}%{caveat}");
        }
    }
}
