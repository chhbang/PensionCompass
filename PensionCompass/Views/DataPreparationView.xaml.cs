using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PensionCompass.Services;
using PensionCompass.ViewModels;
using Windows.Storage.Pickers;

namespace PensionCompass.Views;

public sealed partial class DataPreparationView : Page
{
    public DataPreparationViewModel ViewModel { get; } = new();

    public DataPreparationView()
    {
        InitializeComponent();
        ViewModel.LoadFromAppState();
    }

    private async void LoadHtmlButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = CreateHtmlPicker();
        var files = await picker.PickMultipleFilesAsync();
        if (files == null || files.Count == 0) return;

        await ViewModel.ImportHtmlAsync(files.Select(f => f.Path).ToList(), replaceExisting: true);
    }

    private async void MergeHtmlButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = CreateHtmlPicker();
        var files = await picker.PickMultipleFilesAsync();
        if (files == null || files.Count == 0) return;

        await ViewModel.ImportHtmlAsync(files.Select(f => f.Path).ToList(), replaceExisting: false);
    }

    private async void SaveCsvButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder == null) return;
        await ViewModel.SaveCsvAsync(folder);
    }

    private async void LoadCsvButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder == null) return;
        await ViewModel.LoadCsvAsync(folder);
    }

    private async void ResetCatalogButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "카탈로그 초기화 확인",
            Content = "불러온 상품 카탈로그를 모두 삭제합니다. 다음 실행 시에도 복원되지 않습니다. 진행할까요?",
            PrimaryButtonText = "초기화",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            ViewModel.ResetCatalog();
    }

    private static async System.Threading.Tasks.Task<string?> PickFolderAsync()
    {
        if (App.Window is null) return null;

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        WindowHelper.Initialize(picker, App.Window);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private static FileOpenPicker CreateHtmlPicker()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.Downloads,
        };
        picker.FileTypeFilter.Add(".html");
        picker.FileTypeFilter.Add(".htm");
        if (App.Window is { } window)
            WindowHelper.Initialize(picker, window);
        return picker;
    }
}
