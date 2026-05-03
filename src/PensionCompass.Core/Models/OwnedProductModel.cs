namespace PensionCompass.Core.Models;

public sealed class OwnedProductModel
{
    public string ProductName { get; set; } = string.Empty;
    public decimal CurrentValue { get; set; }
    public decimal? ReturnRate { get; set; }
    public decimal? AnnualizedReturn { get; set; }
    public int? InvestedDays { get; set; }
    public decimal? TotalShares { get; set; }

    /// <summary>
    /// True (default) means the AI may include this holding in the sell pool;
    /// false locks it (the user wants to keep this product no matter what).
    /// The AI decides full-vs-partial-sell amounts on its own — this flag only sets a hard floor.
    /// </summary>
    public bool IsSellable { get; set; } = true;
}
