namespace PensionCompass.Core.Models;

public enum ReturnPeriod
{
    Month1,
    Month3,
    Month6,
    Year1,
    Year3,
}

public static class ReturnPeriodExtensions
{
    public static string ToKoreanLabel(this ReturnPeriod period) => period switch
    {
        ReturnPeriod.Month1 => "1개월",
        ReturnPeriod.Month3 => "3개월",
        ReturnPeriod.Month6 => "6개월",
        ReturnPeriod.Year1 => "1년",
        ReturnPeriod.Year3 => "3년",
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, null),
    };

    public static string ToCsvHeader(this ReturnPeriod period) => $"수익률({period.ToKoreanLabel()})";

    public static bool TryParseFromKoreanLabel(string label, out ReturnPeriod period)
    {
        switch (label)
        {
            case "1개월": period = ReturnPeriod.Month1; return true;
            case "3개월": period = ReturnPeriod.Month3; return true;
            case "6개월": period = ReturnPeriod.Month6; return true;
            case "1년":   period = ReturnPeriod.Year1;  return true;
            case "3년":   period = ReturnPeriod.Year3;  return true;
            default:      period = default;             return false;
        }
    }
}
