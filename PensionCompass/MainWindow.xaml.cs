using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using PensionCompass.Views;

namespace PensionCompass;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void RootNavigationView_Loaded(object sender, RoutedEventArgs e)
    {
        if (RootNavigationView.MenuItems.Count > 0
            && RootNavigationView.MenuItems[0] is NavigationViewItem first)
        {
            RootNavigationView.SelectedItem = first;
        }
    }

    private void RootNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            return;

        var pageType = tag switch
        {
            "Settings" => typeof(SettingsView),
            "DataPreparation" => typeof(DataPreparationView),
            "MyAccount" => typeof(MyAccountView),
            "SellTargets" => typeof(SellTargetsView),
            "AiRebalance" => typeof(AiRebalanceView),
            _ => null,
        };

        if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
    }
}
