using PensionCompass.Core.Models;

namespace PensionCompass.ViewModels;

/// <summary>
/// View-side wrapper for <see cref="FundProduct"/> that flattens the dynamic
/// <see cref="FundProduct.Returns"/> dictionary into bindable per-period properties,
/// since XAML binding can't reach a generic dictionary by key without a converter.
/// </summary>
public sealed class FundRow
{
    private readonly FundProduct _source;

    public FundRow(FundProduct source) => _source = source;

    public string ProductCode => _source.ProductCode;
    public string ProductName => _source.ProductName;
    public string AssetManager => _source.AssetManager;
    public string RiskGrade => _source.RiskGrade;

    public string Return1Month => Get(ReturnPeriod.Month1);
    public string Return3Month => Get(ReturnPeriod.Month3);
    public string Return6Month => Get(ReturnPeriod.Month6);
    public string Return1Year => Get(ReturnPeriod.Year1);
    public string Return3Year => Get(ReturnPeriod.Year3);

    private string Get(ReturnPeriod period)
        => _source.Returns.TryGetValue(period, out var v) ? v : string.Empty;
}
