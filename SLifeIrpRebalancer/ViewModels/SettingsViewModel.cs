using CommunityToolkit.Mvvm.ComponentModel;
using SLifeIrpRebalancer.Core.Ai;
using SLifeIrpRebalancer.Services;

namespace SLifeIrpRebalancer.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private SettingsService Settings => AppState.Instance.Settings;

    /// <summary>0 = Claude, 1 = Gemini, 2 = GPT.</summary>
    public int ProviderIndex
    {
        get => Settings.AiProvider switch
        {
            "Claude" => 0,
            "Gemini" => 1,
            "GPT" => 2,
            _ => 0,
        };
        set
        {
            var label = value switch
            {
                0 => "Claude",
                1 => "Gemini",
                2 => "GPT",
                _ => "Claude",
            };
            if (Settings.AiProvider == label) return;
            Settings.AiProvider = label;
            OnPropertyChanged();
        }
    }

    public string ClaudeModel
    {
        get => Settings.ClaudeModel;
        set { if (Settings.ClaudeModel == value) return; Settings.ClaudeModel = value; OnPropertyChanged(); }
    }

    public string GeminiModel
    {
        get => Settings.GeminiModel;
        set { if (Settings.GeminiModel == value) return; Settings.GeminiModel = value; OnPropertyChanged(); }
    }

    public string GptModel
    {
        get => Settings.GptModel;
        set { if (Settings.GptModel == value) return; Settings.GptModel = value; OnPropertyChanged(); }
    }

    public string ClaudeApiKey
    {
        get => Settings.ClaudeApiKey;
        set { if (Settings.ClaudeApiKey == value) return; Settings.ClaudeApiKey = value; OnPropertyChanged(); }
    }

    public string GeminiApiKey
    {
        get => Settings.GeminiApiKey;
        set { if (Settings.GeminiApiKey == value) return; Settings.GeminiApiKey = value; OnPropertyChanged(); }
    }

    public string GptApiKey
    {
        get => Settings.GptApiKey;
        set { if (Settings.GptApiKey == value) return; Settings.GptApiKey = value; OnPropertyChanged(); }
    }

    /// <summary>0 = Off, 1 = Low, 2 = Medium, 3 = High. Default 3.</summary>
    public int ThinkingLevelIndex
    {
        get => (int)Settings.ThinkingLevel;
        set
        {
            var level = (ThinkingLevel)value;
            if (Settings.ThinkingLevel == level) return;
            Settings.ThinkingLevel = level;
            OnPropertyChanged();
        }
    }

}
