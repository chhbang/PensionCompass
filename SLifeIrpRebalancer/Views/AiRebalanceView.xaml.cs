using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SLifeIrpRebalancer.Core.Markdown;
using SLifeIrpRebalancer.Services;
using SLifeIrpRebalancer.ViewModels;
using Windows.Storage.Pickers;

namespace SLifeIrpRebalancer.Views;

public sealed partial class AiRebalanceView : Page
{
    public AiRebalanceViewModel ViewModel { get; } = new();

    public AiRebalanceView()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += AiRebalanceView_Loaded;
    }

    private async void AiRebalanceView_Loaded(object sender, RoutedEventArgs e)
    {
        // CoreWebView2 must be initialized before NavigateToString works.
        await ResponseWebView.EnsureCoreWebView2Async();
        if (ViewModel.HasResponse)
            UpdateWebViewContent(ViewModel.AiResponse);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiRebalanceViewModel.AiResponse))
            UpdateWebViewContent(ViewModel.AiResponse);
    }

    private void UpdateWebViewContent(string markdown)
    {
        if (ResponseWebView.CoreWebView2 == null) return;
        var html = MarkdownToHtml.Convert(markdown);
        ResponseWebView.NavigateToString(html);
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.GenerateProposalAsync();
    }

    private async void PreviewPromptButton_Click(object sender, RoutedEventArgs e)
    {
        var prompt = ViewModel.BuildPromptPreview();

        var systemBox = new TextBox
        {
            Text = prompt.SystemPrompt,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, Malgun Gothic"),
            MinHeight = 80,
        };
        var userBox = new TextBox
        {
            Text = prompt.UserPrompt,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, Malgun Gothic"),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(userBox, ScrollBarVisibility.Auto);

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = "System Prompt", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        content.Children.Add(systemBox);
        content.Children.Add(new TextBlock { Text = "User Prompt", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 8, 0, 0) });
        content.Children.Add(userBox);

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 12, 0),
        };

        var dialog = new ContentDialog
        {
            Title = "API로 전송될 프롬프트 미리보기",
            Content = scroll,
            CloseButtonText = "닫기",
            PrimaryButtonText = "User Prompt 복사",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 1100.0;
        dialog.Resources["ContentDialogMaxHeight"] = 800.0;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(prompt.UserPrompt);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            args.Cancel = true; // keep dialog open after copy
        };

        await dialog.ShowAsync();
    }

    private void OpenExportedPdfButton_Click(object sender, RoutedEventArgs e)
    {
        var path = ViewModel.LastExportedPdfPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            ViewModel.StatusMessage = "PDF 파일을 찾을 수 없습니다 (이동 또는 삭제됐을 수 있습니다).";
            return;
        }

        try
        {
            // UseShellExecute=true delegates to the OS file association so the user's preferred PDF viewer launches.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.Exception ex)
        {
            ViewModel.StatusMessage = $"PDF 열기 실패: {ex.Message}";
        }
    }

    private async void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Window is null) return;

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"리밸런싱_제안_{System.DateTime.Now:yyyyMMdd_HHmm}",
        };
        picker.FileTypeChoices.Add("PDF 문서", [".pdf"]);
        WindowHelper.Initialize(picker, App.Window);

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        await ViewModel.ExportPdfAsync(file.Path);
    }
}
