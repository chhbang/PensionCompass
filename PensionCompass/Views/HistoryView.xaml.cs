using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using PensionCompass.Core.Markdown;
using PensionCompass.Services;
using PensionCompass.ViewModels;
using Windows.Storage.Pickers;

namespace PensionCompass.Views;

public sealed partial class HistoryView : Page
{
    public HistoryViewModel ViewModel { get; } = new();

    public HistoryView()
    {
        InitializeComponent();
        ViewModel.Refresh();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += HistoryView_Loaded;
    }

    private async void HistoryView_Loaded(object sender, RoutedEventArgs e)
    {
        await ResponseWebView.EnsureCoreWebView2Async();
        if (ViewModel.LoadedSession is { } session)
            UpdateWebViewContent(session.RecommendationMarkdown);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.LoadedSession))
            UpdateWebViewContent(ViewModel.LoadedSession?.RecommendationMarkdown ?? string.Empty);
    }

    private void UpdateWebViewContent(string markdown)
    {
        if (ResponseWebView.CoreWebView2 == null) return;
        var html = string.IsNullOrWhiteSpace(markdown)
            ? "<html><body style=\"font-family:Malgun Gothic;color:#666;padding:24px\">선택한 회차가 없습니다.</body></html>"
            : MarkdownToHtml.Convert(markdown);
        ResponseWebView.NavigateToString(html);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Refresh();
    }

    private async void LoadExternalButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Window is null) return;
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.Desktop,
        };
        picker.FileTypeFilter.Add(".json");
        WindowHelper.Initialize(picker, App.Window);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        ViewModel.LoadFromExternalFile(file.Path);
    }

    private void UseAsPriorButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEntry is not { } row) return;
        AppState.Instance.PendingPriorEntry = row.Entry;

        // Navigate the parent Frame to the AI Rebalance page; that page's VM picks up the pending
        // entry on its next RefreshHistoryEntries() pass and pre-selects it in the combo.
        if (Frame is { } frame)
            frame.Navigate(typeof(AiRebalanceView), null, new EntranceNavigationTransitionInfo());
    }

    private async void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Window is null || ViewModel.LoadedSession is null) return;

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"리밸런싱_제안_{ViewModel.LoadedSession.Meta.Timestamp.ToLocalTime():yyyyMMdd_HHmm}",
        };
        picker.FileTypeChoices.Add("PDF 문서", [".pdf"]);
        WindowHelper.Initialize(picker, App.Window);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        await ViewModel.ExportSelectedToPdfAsync(file.Path);
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEntry is not { } entry) return;
        var dialog = new ContentDialog
        {
            Title = "이력 삭제 확인",
            Content = $"\"{entry.DisplayLabel}\" 회차를 삭제합니다. 동기화 폴더에 있는 파일이라면 다른 PC에서도 사라집니다. 진행할까요?",
            PrimaryButtonText = "삭제",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        ViewModel.DeleteSelected();
    }
}
