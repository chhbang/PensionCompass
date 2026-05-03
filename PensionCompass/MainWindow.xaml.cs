using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using PensionCompass.Views;
using WinRT.Interop;

namespace PensionCompass;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 타이틀바 / Alt+Tab 미리보기에 표시되는 Window 아이콘은 Package.appxmanifest 의
        // Visual Assets (시작 메뉴·작업 표시줄용)와 별개라서 코드에서 직접 지정해줘야 합니다.
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow.GetFromWindowId(windowId).SetIcon("Assets/AppIcon.ico");
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
            "About" => typeof(AboutView),
            _ => null,
        };

        if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
    }
}
