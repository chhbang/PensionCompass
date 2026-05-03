using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace PensionCompass.Converters;

/// <summary>
/// True → Visible, False → Collapsed. Pass parameter "Invert" to swap the polarity.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is bool b && b;
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Visible;
}
