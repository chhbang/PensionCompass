using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PensionCompass.Services;
using PensionCompass.ViewModels;
using Windows.Storage.Pickers;

namespace PensionCompass.Views;

public sealed partial class ReferenceLibraryView : Page
{
    public ReferenceLibraryViewModel ViewModel { get; } = new();

    public ReferenceLibraryView()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) => ViewModel.Refresh();

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Window is null) return;

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".pdf");
        WindowHelper.Initialize(picker, App.Window);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            byte[] bytes;
            using (var stream = await file.OpenStreamForReadAsync())
            using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms);
                bytes = ms.ToArray();
            }
            ViewModel.Add(file.Name, bytes);
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("PDF 추가 실패", ex.Message);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ReferenceRowViewModel row) return;

        var confirm = new ContentDialog
        {
            Title = "참고 자료 삭제",
            Content = $"\"{row.FileName}\" 을(를) 라이브러리에서 삭제할까요? 이 PC에서 제거되며 되돌릴 수 없습니다.",
            PrimaryButtonText = "삭제",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.Remove(row);
    }

    private async System.Threading.Tasks.Task ShowDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "확인",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
