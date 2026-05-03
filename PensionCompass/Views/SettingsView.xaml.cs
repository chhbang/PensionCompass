using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PensionCompass.Core.Ai;
using PensionCompass.Services;
using PensionCompass.ViewModels;

namespace PensionCompass.Views;

public sealed partial class SettingsView : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsView()
    {
        InitializeComponent();
        // PasswordBox.Password is intentionally not bindable in WinUI 3 (security policy),
        // so we initialize each one imperatively and forward changes through PasswordChanged.
        ClaudeApiKeyBox.Password = ViewModel.ClaudeApiKey;
        GeminiApiKeyBox.Password = ViewModel.GeminiApiKey;
        GptApiKeyBox.Password = ViewModel.GptApiKey;
    }

    private void ClaudeApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        => ViewModel.ClaudeApiKey = ClaudeApiKeyBox.Password;

    private void GeminiApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        => ViewModel.GeminiApiKey = GeminiApiKeyBox.Password;

    private void GptApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        => ViewModel.GptApiKey = GptApiKeyBox.Password;

    private async void ListModelsButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppState.Instance.Settings;
        var apiKey = settings.GetActiveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await ShowErrorDialogAsync(
                "API Key 필요",
                $"{settings.AiProvider} 모델 목록을 조회하려면 위의 \"{settings.AiProvider}\" API Key를 먼저 입력해주세요.");
            return;
        }

        var statusText = new TextBlock
        {
            Text = $"{settings.AiProvider} 모델 목록 불러오는 중...",
            Margin = new Thickness(0, 0, 0, 8),
        };
        var progress = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 8) };
        var listView = new ListView
        {
            Height = 320,
            SelectionMode = ListViewSelectionMode.Single,
            BorderThickness = new Thickness(1),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        };

        var dialog = new ContentDialog
        {
            Title = $"{settings.AiProvider} 모델 목록",
            Content = new StackPanel
            {
                Spacing = 0,
                Children = { statusText, progress, listView },
                Width = 500,
            },
            PrimaryButtonText = "선택",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot,
        };

        listView.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = listView.SelectedItem != null;

        // Kick off the fetch in parallel with showing the dialog so the spinner appears immediately.
        _ = LoadModelsIntoDialogAsync(settings, apiKey, listView, statusText, progress);

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || listView.SelectedItem is not string picked)
            return;

        switch (settings.AiProvider)
        {
            case "Claude": ViewModel.ClaudeModel = picked; break;
            case "Gemini": ViewModel.GeminiModel = picked; break;
            case "GPT": ViewModel.GptModel = picked; break;
        }
    }

    private static async System.Threading.Tasks.Task LoadModelsIntoDialogAsync(
        SettingsService settings,
        string apiKey,
        ListView listView,
        TextBlock statusText,
        ProgressBar progress)
    {
        try
        {
            var client = AiClientFactory.Create(settings.AiProvider, apiKey);
            IReadOnlyList<string> models = await client.ListModelsAsync();
            listView.ItemsSource = models;

            // Highlight the model currently saved for this provider so the user can see whether it's valid.
            var current = settings.GetActiveModel();
            if (!string.IsNullOrEmpty(current) && models.Contains(current))
            {
                listView.SelectedItem = current;
                statusText.Text = $"{models.Count}개 모델 발견. 현재 입력값(\"{current}\")이 목록에 있습니다.";
            }
            else if (!string.IsNullOrEmpty(current))
            {
                statusText.Text = $"{models.Count}개 모델 발견. 현재 입력값(\"{current}\")은 목록에 없으니 다른 모델을 선택하거나 그대로 둘 수 있습니다.";
            }
            else
            {
                statusText.Text = $"{models.Count}개 모델 발견. 사용할 모델을 선택하세요.";
            }
        }
        catch (AiClientException ex)
        {
            statusText.Text = ex.Message;
            statusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        }
        catch (Exception ex)
        {
            statusText.Text = $"예상치 못한 오류: {ex.Message}";
            statusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        }
        finally
        {
            progress.IsIndeterminate = false;
            progress.Visibility = Visibility.Collapsed;
        }
    }

    private async System.Threading.Tasks.Task ShowErrorDialogAsync(string title, string message)
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
