using PensionCompass.Core.Parsing;

namespace PensionCompass.Core.Tests;

public class CoveredCallDetectorTests
{
    // Real product names pulled from reference/csv/펀드_상품목록.csv so we know we catch what the
    // Samsung Life HTML actually emits.
    [Theory]
    [InlineData("[온라인전용]미래에셋퇴직연금배당커버드콜액티브증권자1(주식혼")]
    [InlineData("[온라인]신한퇴직연금커버드콜인덱스증권자투자신탁[주혼-파생]Ce")]
    [InlineData("[온라인전용]미래에셋연금글로벌배당커버드콜액티브증권자투자신탁 1(주식혼합)종류C-P2e")]
    [InlineData("[온라인]미래미국배당커버드콜액티브증권자투자신탁(주식)(UH)C-P2e")]
    public void IsCoveredCallFund_RealKoreanNames_ReturnsTrue(string name)
        => Assert.True(CoveredCallDetector.IsCoveredCallFund(name));

    [Theory]
    [InlineData("Vanilla Covered Call Equity ETF")]
    [InlineData("Vanilla CoveredCall Equity ETF")]
    [InlineData("vanilla covered call fund")] // case-insensitive
    public void IsCoveredCallFund_EnglishForms_ReturnsTrue(string name)
        => Assert.True(CoveredCallDetector.IsCoveredCallFund(name));

    [Theory]
    [InlineData("")]
    [InlineData("[온라인전용]미래에셋코어테크증권투자신탁(주식)")]
    [InlineData("삼성그룹주식형")]
    [InlineData("S Selection 주식형")]
    [InlineData("이율보증형(3년)")]
    public void IsCoveredCallFund_NonCoveredCallNames_ReturnsFalse(string name)
        => Assert.False(CoveredCallDetector.IsCoveredCallFund(name));
}
