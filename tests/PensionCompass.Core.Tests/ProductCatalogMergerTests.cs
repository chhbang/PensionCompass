using PensionCompass.Core.Models;
using PensionCompass.Core.Parsing;

namespace PensionCompass.Core.Tests;

public class ProductCatalogMergerTests
{
    [Fact]
    public void Merge_FoldsReturnPeriodsByProductCode()
    {
        var fundA3m = new FundProduct("G001", "테스트펀드", "테스트운용", "보통위험",
            new Dictionary<ReturnPeriod, string> { [ReturnPeriod.Month3] = "5.00%" });
        var fundA1y = new FundProduct("G001", "테스트펀드", "테스트운용", "보통위험",
            new Dictionary<ReturnPeriod, string> { [ReturnPeriod.Year1] = "12.00%" });

        var snap1 = new ProductCatalog([], [fundA3m], [ReturnPeriod.Month3]);
        var snap2 = new ProductCatalog([], [fundA1y], [ReturnPeriod.Year1]);

        var merged = ProductCatalogMerger.Merge([snap1, snap2]);

        var fund = Assert.Single(merged.Funds);
        Assert.Equal(2, fund.Returns.Count);
        Assert.Equal("5.00%", fund.Returns[ReturnPeriod.Month3]);
        Assert.Equal("12.00%", fund.Returns[ReturnPeriod.Year1]);
        Assert.Equal([ReturnPeriod.Month3, ReturnPeriod.Year1], merged.FundReturnPeriods);
    }
}
