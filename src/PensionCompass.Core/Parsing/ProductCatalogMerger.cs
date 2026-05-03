using PensionCompass.Core.Models;

namespace PensionCompass.Core.Parsing;

/// <summary>
/// Merges multiple <see cref="ProductCatalog"/> snapshots into one.
/// A single Samsung Life HTML snapshot carries exactly one return-period column for funds,
/// determined by the sort order at the time of save. To populate all five 수익률 columns
/// (1개월, 3개월, 6개월, 1년, 3년) the user must save and import five snapshots; this merger
/// folds them together by product code, OR'ing the per-period return values.
/// </summary>
public static class ProductCatalogMerger
{
    public static ProductCatalog Merge(IEnumerable<ProductCatalog> catalogs)
    {
        var pgByCode = new Dictionary<string, PrincipalGuaranteedProduct>(StringComparer.Ordinal);
        var fundByCode = new Dictionary<string, FundProductBuilder>(StringComparer.Ordinal);
        var periods = new SortedSet<ReturnPeriod>();

        foreach (var catalog in catalogs)
        {
            foreach (var pg in catalog.PrincipalGuaranteed)
                pgByCode[pg.ProductCode] = pg;

            foreach (var fund in catalog.Funds)
            {
                if (!fundByCode.TryGetValue(fund.ProductCode, out var builder))
                {
                    builder = new FundProductBuilder(fund);
                    fundByCode[fund.ProductCode] = builder;
                }

                foreach (var (period, value) in fund.Returns)
                    builder.Returns[period] = value;
            }

            foreach (var period in catalog.FundReturnPeriods)
                periods.Add(period);
        }

        var mergedFunds = fundByCode.Values
            .Select(b => new FundProduct(
                b.Source.ProductCode,
                b.Source.ProductName,
                b.Source.AssetManager,
                b.Source.RiskGrade,
                new Dictionary<ReturnPeriod, string>(b.Returns)))
            .ToList();

        return new ProductCatalog(
            pgByCode.Values.ToList(),
            mergedFunds,
            periods.ToList());
    }

    private sealed class FundProductBuilder
    {
        public FundProduct Source { get; }
        public Dictionary<ReturnPeriod, string> Returns { get; }

        public FundProductBuilder(FundProduct source)
        {
            Source = source;
            Returns = new Dictionary<ReturnPeriod, string>(source.Returns);
        }
    }
}
