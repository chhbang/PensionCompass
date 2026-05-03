using System;
using Microsoft.UI.Xaml.Data;

namespace PensionCompass.Converters;

/// <summary>
/// Formats numeric amounts as ₩-prefixed Korean Won with thousands separators (e.g. ₩30,646,260).
/// Accepts decimal / double / int. Returns empty string for null. Read-only (one-way).
/// </summary>
public sealed class WonFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is null) return string.Empty;

        decimal amount = value switch
        {
            decimal d => d,
            double dbl when double.IsNaN(dbl) => 0m,
            double dbl => (decimal)dbl,
            int i => i,
            long l => l,
            _ => 0m,
        };

        return $"₩{amount:N0}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
