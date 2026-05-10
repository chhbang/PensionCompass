using System.Globalization;
using System.Text.RegularExpressions;

namespace PensionCompass.Core.Validation;

public enum IrpValidationStatus
{
    /// <summary>Both 위험자산 ≤ 70% and 안정자산 ≥ 30% confirmed.</summary>
    Compliant,
    /// <summary>One or both ratios extracted and at least one is outside the legal bound.</summary>
    Violation,
    /// <summary>Could not extract ratios from the response — manual check required.</summary>
    UnableToVerify,
}

/// <param name="RiskAssetPercent">Extracted 위험자산 ratio (0-100), or null if not found.</param>
/// <param name="SafeAssetPercent">Extracted 안정자산 ratio (0-100), or null if not found.</param>
public sealed record IrpValidationResult(
    decimal? RiskAssetPercent,
    decimal? SafeAssetPercent,
    IrpValidationStatus Status,
    string Message);

/// <summary>
/// Heuristic validator: scans the AI's markdown response for 위험자산 / 안정자산 ratio mentions
/// and checks them against the IRP 70/30 legal limit. The prompt asks the AI to print these
/// totals explicitly so the regex usually finds them; if it doesn't, status is UnableToVerify.
/// Pure and UI-free so it lives in Core.
/// </summary>
public static class IrpRuleValidator
{
    public const decimal MaxRiskAssetPercent = 70m;
    public const decimal MinSafeAssetPercent = 30m;

    // Captures the numeric portion of any "<digits>.<digits>%" or "<digits>%" token.
    private static readonly Regex PercentTokenRegex = new(@"(\d+(?:\.\d+)?)\s*%", RegexOptions.Compiled);

    public static IrpValidationResult Validate(string? aiResponseMarkdown)
    {
        if (string.IsNullOrWhiteSpace(aiResponseMarkdown))
        {
            return new IrpValidationResult(null, null, IrpValidationStatus.UnableToVerify,
                "AI 응답이 비어 있어 IRP 70/30 검증을 수행할 수 없습니다.");
        }

        decimal? risk = null;
        decimal? safe = null;

        // Scan line-by-line, taking the LAST matching line for each keyword so we bias toward the
        // recommendation summary (which usually comes after the analysis section). Lines containing
        // both keywords are skipped — they're typically explanatory prose, not the numeric summary.
        foreach (var rawLine in aiResponseMarkdown.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var hasRisk = line.Contains("위험자산", StringComparison.Ordinal);
            var hasSafe = line.Contains("안정자산", StringComparison.Ordinal);
            if (hasRisk == hasSafe) continue; // both or neither

            var pct = ExtractFirstPercent(line);
            if (pct is null) continue;

            if (hasRisk) risk = pct;
            else safe = pct;
        }

        if (risk is null && safe is null)
        {
            return new IrpValidationResult(null, null, IrpValidationStatus.UnableToVerify,
                "AI 응답에서 위험자산·안정자산 비중을 자동으로 추출하지 못했습니다. 직접 확인해 주세요.");
        }

        var violations = new List<string>();
        if (risk is > MaxRiskAssetPercent)
            violations.Add($"위험자산 비중 {Format(risk.Value)}%가 IRP 법적 한도 70%를 초과합니다.");
        if (safe is < MinSafeAssetPercent)
            violations.Add($"안정자산 비중 {Format(safe.Value)}%가 IRP 법적 의무 30%에 미달합니다.");

        if (violations.Count > 0)
        {
            return new IrpValidationResult(risk, safe, IrpValidationStatus.Violation,
                string.Join(" ", violations));
        }

        // Both within bounds, OR only one of the two extracted but not violating.
        if (risk is not null && safe is not null)
        {
            return new IrpValidationResult(risk, safe, IrpValidationStatus.Compliant,
                $"IRP 70/30 규제 준수 (위험자산 {Format(risk.Value)}% / 안정자산 {Format(safe.Value)}%).");
        }

        // Only one ratio extracted — can't fully assert compliance, but report what we have.
        var partial = risk is not null
            ? $"위험자산 비중 {Format(risk.Value)}%만 확인됨 (안정자산 비중은 추출 실패)."
            : $"안정자산 비중 {Format(safe!.Value)}%만 확인됨 (위험자산 비중은 추출 실패).";
        return new IrpValidationResult(risk, safe, IrpValidationStatus.UnableToVerify,
            partial + " 양쪽 모두 직접 확인해 주세요.");
    }

    private static decimal? ExtractFirstPercent(string line)
    {
        var match = PercentTokenRegex.Match(line);
        if (!match.Success) return null;
        return decimal.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : null;
    }

    private static string Format(decimal value)
        => value.ToString("0.0", CultureInfo.InvariantCulture);
}
