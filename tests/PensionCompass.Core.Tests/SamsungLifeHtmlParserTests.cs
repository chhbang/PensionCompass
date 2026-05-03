using PensionCompass.Core.Models;
using PensionCompass.Core.Parsing;

namespace PensionCompass.Core.Tests;

public class SamsungLifeHtmlParserTests
{
    private static string LoadReferenceHtml()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "samsunglife.html");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Parse_ReferenceSnapshot_ProducesPrincipalGuaranteedAndFunds()
    {
        var parser = new SamsungLifeHtmlParser();

        var catalog = parser.Parse(LoadReferenceHtml());

        Assert.NotEmpty(catalog.PrincipalGuaranteed);
        Assert.NotEmpty(catalog.Funds);
    }

    [Fact]
    public void Parse_ReferenceSnapshot_FundExposesAssetManagerFromDescSum()
    {
        var parser = new SamsungLifeHtmlParser();

        var catalog = parser.Parse(LoadReferenceHtml());

        var fund = catalog.Funds.SingleOrDefault(f => f.ProductCode == "G04783");
        Assert.NotNull(fund);
        Assert.Equal("[온라인전용]미래에셋코어테크증권투자신탁(주식)", fund.ProductName);
        Assert.Contains("미래에셋자산운용", fund.AssetManager);
        Assert.Equal("매우높은위험", fund.RiskGrade);
    }

    [Fact]
    public void Parse_ReferenceSnapshot_FundCarriesExactlyOneReturnPeriod()
    {
        var parser = new SamsungLifeHtmlParser();

        var catalog = parser.Parse(LoadReferenceHtml());

        Assert.Single(catalog.FundReturnPeriods);
        var period = catalog.FundReturnPeriods[0];
        Assert.All(catalog.Funds, f =>
        {
            // The snapshot was saved sorted by some single period; every fund card
            // exposes that one period (or none, if the value was missing).
            Assert.True(f.Returns.Count <= 1);
            if (f.Returns.Count == 1)
                Assert.True(f.Returns.ContainsKey(period));
        });
    }

    [Fact]
    public void Parse_PrincipalGuaranteed_ResolvesPrefixedManagerAndMaturity()
    {
        var parser = new SamsungLifeHtmlParser();

        var catalog = parser.Parse(LoadReferenceHtml());

        var pubon = catalog.PrincipalGuaranteed.SingleOrDefault(p => p.ProductCode == "C02004");
        Assert.NotNull(pubon);
        Assert.Equal("푸본현대생명", pubon.AssetManager);
        Assert.Equal("1년", pubon.MaturityTerm);
    }

    [Fact]
    public void Parse_PrincipalGuaranteed_FallsBackToSamsungLifeForUnprefixedNames()
    {
        var parser = new SamsungLifeHtmlParser();

        var catalog = parser.Parse(LoadReferenceHtml());

        var samsungOwn = catalog.PrincipalGuaranteed.SingleOrDefault(p => p.ProductCode == "G02003");
        Assert.NotNull(samsungOwn);
        Assert.Equal("이율보증형(3년)", samsungOwn.ProductName);
        Assert.Equal("삼성생명", samsungOwn.AssetManager);
        Assert.Equal("3년", samsungOwn.MaturityTerm);
    }

    [Fact]
    public void Parse_PrincipalGuaranteed_AppliedRateExtractsFirstPercent()
    {
        var parser = new SamsungLifeHtmlParser();

        var catalog = parser.Parse(LoadReferenceHtml());

        // G02003's applied rate is rendered as "3.65%(3.78%)" — first number wins.
        var samsungOwn = catalog.PrincipalGuaranteed.Single(p => p.ProductCode == "G02003");
        Assert.Equal("3.65%", samsungOwn.AppliedRate);
    }
}
