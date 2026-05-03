using System.Text.RegularExpressions;

namespace PensionCompass.Core.Parsing;

/// <summary>
/// Derives the 운용사 (asset manager) for principal-guaranteed products,
/// which — unlike funds — do not carry the manager in a dedicated DOM element.
/// Funds expose div.desc-sm > span:first-child explicitly; this resolver is for the other case.
/// </summary>
public sealed class AssetManagerResolver
{
    /// <summary>
    /// Asset-manager value the resolver assigns to Samsung Life Insurance Co., Ltd.'s own
    /// principal-guaranteed products (unprefixed names like "이율보증형(3년)"). The HTML span
    /// for Samsung Life-managed funds (S Selection, 삼성그룹주식형, 인덱스주식형 등) instead
    /// reads "삼성생명보험" verbatim — both strings refer to the same legal entity, so use
    /// <see cref="IsSamsungLifeInsurance"/> rather than equality against this constant when
    /// matching across the PG/fund split.
    /// Affiliates like 삼성자산운용 (Samsung Asset Management — a separate legal entity) do NOT count.
    /// </summary>
    public const string SamsungLifeInsurance = "삼성생명";

    private const string SamsungLifeFallback = SamsungLifeInsurance;

    /// <summary>
    /// Returns true if the given <paramref name="assetManager"/> string designates Samsung Life
    /// Insurance itself (the entity that qualifies for the lifelong-annuity payout) — covers both
    /// the HTML fund span "삼성생명보험" and the resolver fallback "삼성생명". Affiliate strings
    /// like "삼성자산운용" return false.
    /// </summary>
    public static bool IsSamsungLifeInsurance(string? assetManager)
    {
        if (string.IsNullOrWhiteSpace(assetManager)) return false;
        var trimmed = assetManager.Trim();
        return trimmed == "삼성생명" || trimmed == "삼성생명보험";
    }

    private static readonly string[] KnownPrefixes =
    [
        "푸본현대생명", "고려저축은행", "케이비저축은행", "신한저축은행",
        "페퍼저축은행", "웰컴저축은행", "다올저축은행", "OK저축은행",
        "메리츠", "한화", "교보", "흥국", "신한라이프", "DB", "KB",
        "삼성", "미래에셋", "NH",
    ];

    private static readonly Regex LeadingBracketTag = new(@"^\[[^\]]+\]\s*", RegexOptions.Compiled);
    private static readonly Regex LeadingMubaedang = new(@"^무배당\s+", RegexOptions.Compiled);

    public string ResolveFromProductName(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return SamsungLifeFallback;

        var normalized = productName;
        normalized = LeadingBracketTag.Replace(normalized, "");
        normalized = LeadingMubaedang.Replace(normalized, "");
        normalized = normalized.TrimStart();

        foreach (var prefix in KnownPrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                return prefix;
        }

        return SamsungLifeFallback;
    }
}
