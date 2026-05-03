namespace PensionCompass.Core.Models;

public sealed record FundProduct(
    string ProductCode,
    string ProductName,
    string AssetManager,
    string RiskGrade,
    IReadOnlyDictionary<ReturnPeriod, string> Returns);
