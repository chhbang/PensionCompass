using Microsoft.UI.Xaml.Controls;
using PensionCompass.ViewModels;

namespace PensionCompass.Views;

public sealed partial class SellTargetsView : Page
{
    public SellTargetsViewModel ViewModel { get; } = new();

    public SellTargetsView()
    {
        InitializeComponent();
    }
}
