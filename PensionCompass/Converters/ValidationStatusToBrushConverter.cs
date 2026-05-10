using System;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace PensionCompass.Converters;

/// <summary>
/// Maps the IRP validation status string ("Compliant" / "Violation" / "UnableToVerify") to a
/// background brush for the result banner. Soft pastel tones so the banner is noticeable but
/// doesn't fight with the WebView2 response below it.
/// </summary>
public sealed class ValidationStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value as string;
        return key switch
        {
            "Compliant" => new SolidColorBrush(Color.FromArgb(0xFF, 0xDC, 0xF6, 0xE0)),       // soft green
            "Violation" => new SolidColorBrush(Color.FromArgb(0xFF, 0xFC, 0xDC, 0xDC)),       // soft red
            "UnableToVerify" => new SolidColorBrush(Color.FromArgb(0xFF, 0xFE, 0xF5, 0xDC)),  // soft yellow
            _ => new SolidColorBrush(Colors.Transparent),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
