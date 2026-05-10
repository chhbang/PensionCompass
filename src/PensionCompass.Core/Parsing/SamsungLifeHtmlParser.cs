using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using PensionCompass.Core.Models;

namespace PensionCompass.Core.Parsing;

/// <summary>
/// Parses a saved Samsung Life "퇴직연금 전체 상품" page (Vue SSR snapshot).
/// Walks li.data-list-item cards. Distinguishes principal-guaranteed vs funds by
/// the badge inside p.flag-group: "원리금보장형" gray badge vs grade1..grade5 risk badge.
/// One snapshot carries exactly ONE return-period column for funds; merge multiple
/// snapshots via <see cref="ProductCatalogMerger"/> to populate all five periods.
/// </summary>
public sealed class SamsungLifeHtmlParser
{
    private static readonly Regex PercentRegex = new(@"-?\d+(?:\.\d+)?\s*%", RegexOptions.Compiled);
    private static readonly Regex MaturityRegex = new(@"(\d+)\s*년", RegexOptions.Compiled);
    private static readonly Regex ReturnPeriodInTitleRegex = new(@"수익률\s*\(\s*([0-9]+(?:개월|년))\s*\)", RegexOptions.Compiled);

    private readonly AssetManagerResolver _assetManagerResolver;

    public SamsungLifeHtmlParser(AssetManagerResolver? assetManagerResolver = null)
    {
        _assetManagerResolver = assetManagerResolver ?? new AssetManagerResolver();
    }

    public ProductCatalog Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var principalGuaranteed = new List<PrincipalGuaranteedProduct>();
        var funds = new List<FundProduct>();
        var foundPeriods = new SortedSet<ReturnPeriod>();

        var items = doc.DocumentNode.SelectNodes("//li[contains(concat(' ', normalize-space(@class), ' '), ' data-list-item ')]");
        if (items == null)
            return new ProductCatalog(principalGuaranteed, funds, []);

        foreach (var li in items)
        {
            var badges = li.SelectNodes(".//p[contains(@class,'flag-group')]//span[contains(@class,'flag')]");
            if (badges == null || badges.Count == 0) continue;

            var isPrincipalGuaranteed = badges.Any(IsPrincipalGuaranteedBadge);
            var riskGrade = badges
                .Select(GetRiskGradeText)
                .FirstOrDefault(text => text != null);

            var productName = ReadText(li.SelectSingleNode(".//a[contains(@class,'desc-title')]/strong"));
            if (string.IsNullOrWhiteSpace(productName)) continue;

            var productCode = li
                .SelectSingleNode(".//input[@name='productCheckCart']")
                ?.GetAttributeValue("value", "")
                ?.Trim() ?? string.Empty;

            var valueText = ReadDescValue(li);

            if (isPrincipalGuaranteed)
            {
                principalGuaranteed.Add(new PrincipalGuaranteedProduct(
                    ProductCode: productCode,
                    ProductName: productName,
                    AssetManager: _assetManagerResolver.ResolveFromProductName(productName),
                    AppliedRate: ExtractFirstPercent(valueText),
                    MaturityTerm: ExtractMaturityTerm(productName)));
            }
            else if (riskGrade != null)
            {
                var manager = ReadText(li.SelectSingleNode(".//div[contains(@class,'desc-sum')]/span[1]"));
                var titleText = ReadText(li.SelectSingleNode(".//ul[contains(@class,'desc-list')]//li[contains(@class,'desc-item')]//div[contains(@class,'tit')]/em"));
                // Samsung Life uses a binary encoding: an explicit "위험자산" badge marks risky funds
                // (equity, mixed, alternative). Bond/safe funds simply omit the badge — there is no
                // explicit "안정자산" badge on the page. So absence of the badge implies 안정자산.
                var assetClass = badges.Any(IsRiskAssetBadge) ? "위험자산" : "안정자산";

                var returns = new Dictionary<ReturnPeriod, string>();
                if (TryExtractReturnPeriod(titleText, out var period))
                {
                    returns[period] = valueText;
                    foundPeriods.Add(period);
                }

                funds.Add(new FundProduct(
                    ProductCode: productCode,
                    ProductName: productName,
                    AssetManager: manager,
                    RiskGrade: riskGrade,
                    Returns: returns,
                    AssetClass: assetClass));
            }
        }

        return new ProductCatalog(principalGuaranteed, funds, foundPeriods.ToList());
    }

    private static bool IsPrincipalGuaranteedBadge(HtmlNode badge)
    {
        var classes = badge.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!classes.Contains("gray")) return false;
        var text = HtmlEntity.DeEntitize(badge.InnerText).Trim();
        return text == "원리금보장형";
    }

    private static string? GetRiskGradeText(HtmlNode badge)
    {
        var classes = badge.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!classes.Any(c => c.StartsWith("grade", StringComparison.Ordinal))) return null;
        var text = HtmlEntity.DeEntitize(badge.InnerText).Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static bool IsRiskAssetBadge(HtmlNode badge)
        => HtmlEntity.DeEntitize(badge.InnerText).Trim() == "위험자산";

    private static string ReadText(HtmlNode? node)
        => node == null ? string.Empty : HtmlEntity.DeEntitize(node.InnerText).Trim();

    private static string ReadDescValue(HtmlNode li)
    {
        // The desc <span> may be preceded by a tooltip button; take the LAST span under div.desc.
        var spans = li.SelectNodes(".//ul[contains(@class,'desc-list')]//li[contains(@class,'desc-item')]//div[contains(@class,'desc')]/span");
        if (spans == null || spans.Count == 0) return string.Empty;
        return HtmlEntity.DeEntitize(spans[^1].InnerText).Trim();
    }

    private static string ExtractFirstPercent(string text)
    {
        var match = PercentRegex.Match(text);
        return match.Success ? match.Value.Replace(" ", "") : text;
    }

    private static string ExtractMaturityTerm(string productName)
    {
        var match = MaturityRegex.Match(productName);
        return match.Success ? $"{match.Groups[1].Value}년" : string.Empty;
    }

    private static bool TryExtractReturnPeriod(string titleText, out ReturnPeriod period)
    {
        var match = ReturnPeriodInTitleRegex.Match(titleText);
        if (match.Success && ReturnPeriodExtensions.TryParseFromKoreanLabel(match.Groups[1].Value, out period))
            return true;
        period = default;
        return false;
    }
}
