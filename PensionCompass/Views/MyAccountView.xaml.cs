using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PensionCompass.ViewModels;

namespace PensionCompass.Views;

public sealed partial class MyAccountView : Page
{
    public MyAccountViewModel ViewModel { get; } = new();

    public MyAccountView()
    {
        InitializeComponent();
    }

    private void ProductSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        sender.ItemsSource = ViewModel.FilterProductNames(sender.Text).ToList();
    }

    private void ProductSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var name = args.ChosenSuggestion as string ?? args.QueryText;
        if (string.IsNullOrWhiteSpace(name)) return;
        ViewModel.AddOwnedProduct(name);
        sender.Text = string.Empty;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var name = ProductSearchBox.Text;
        if (string.IsNullOrWhiteSpace(name)) return;
        ViewModel.AddOwnedProduct(name);
        ProductSearchBox.Text = string.Empty;
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is OwnedProductRow row)
            ViewModel.RemoveOwnedProduct(row);
    }

    private async void ResetAccountButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "계좌 정보 초기화 확인",
            Content = "총 적립금, 입금액, 운용수익, 보유 상품 정보, 그리고 매도 정책·실행 시점 설정을 모두 삭제합니다. 다음 실행 시에도 복원되지 않습니다. 진행할까요?",
            PrimaryButtonText = "초기화",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            ViewModel.ResetAccount();
    }
}
