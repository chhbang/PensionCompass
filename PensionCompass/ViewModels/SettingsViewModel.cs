using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PensionCompass.Core.Ai;
using PensionCompass.Services;

namespace PensionCompass.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private SettingsService Settings => AppState.Instance.Settings;

    public SettingsViewModel()
    {
        AppState.Instance.SyncProviderChanged += OnSyncProviderChanged;
    }

    private void OnSyncProviderChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SyncModeIndex));
        OnPropertyChanged(nameof(IsFolderMode));
        OnPropertyChanged(nameof(IsGoogleMode));
        OnPropertyChanged(nameof(IsGoogleConnected));
        OnPropertyChanged(nameof(GoogleConnectionStatus));
        OnPropertyChanged(nameof(SyncFolder));
    }

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

    /// <summary>
    /// Folder path used in <see cref="SyncMode.FilesystemFolder"/> mode (typically a OneDrive /
    /// Google Drive desktop / Dropbox folder). Setting this property does NOT switch sync mode —
    /// the View calls <see cref="ApplyFilesystemFolder"/> when the user explicitly picks a folder.
    /// </summary>
    public string SyncFolder
    {
        get => Settings.SyncFolder;
        set
        {
            var clean = (value ?? string.Empty).Trim();
            if (Settings.SyncFolder == clean) return;
            Settings.SyncFolder = clean;
            OnPropertyChanged();
        }
    }

    // ──────── Sync mode (radio + connect button state) ────────

    /// <summary>0 = None (LocalState only) / 1 = Filesystem folder / 2 = Google Drive.</summary>
    public int SyncModeIndex
    {
        get => Settings.SyncMode switch
        {
            SyncMode.FilesystemFolder => 1,
            SyncMode.GoogleDrive => 2,
            _ => 0,
        };
        // Setter intentionally a no-op for direct radio binding — switching modes goes through
        // explicit ApplyXxx methods so we can run side effects (OAuth flow, migration). The View
        // wires the radio's SelectionChanged event to call those methods instead.
        set { /* see ApplyNoneMode / ApplyFilesystemFolder / ConnectGoogleAsync */ }
    }

    public bool IsFolderMode => Settings.SyncMode == SyncMode.FilesystemFolder;
    public bool IsGoogleMode => Settings.SyncMode == SyncMode.GoogleDrive;

    public bool IsGoogleConnected
        => Settings.SyncMode == SyncMode.GoogleDrive
           && !string.IsNullOrEmpty(Settings.GoogleDriveConnectedEmail);

    /// <summary>
    /// One-line status for the Google card: "✓ user@example.com 연결됨" / "연결되어 있지 않음" /
    /// connecting/error message during interactive flow.
    /// </summary>
    public string GoogleConnectionStatus
    {
        get
        {
            if (!string.IsNullOrEmpty(_lastGoogleError)) return _lastGoogleError;
            if (IsConnectingGoogle) return "연결 중... 브라우저에서 Google 로그인 후 권한을 승인해 주세요.";
            if (IsGoogleConnected) return $"✓ {Settings.GoogleDriveConnectedEmail} 연결됨";
            return "연결되어 있지 않음";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GoogleConnectionStatus))]
    [NotifyPropertyChangedFor(nameof(CanInteractWithGoogle))]
    private bool _isConnectingGoogle;

    private string? _lastGoogleError;

    public bool CanInteractWithGoogle => !IsConnectingGoogle;

    public void ApplyNoneMode()
    {
        AppState.Instance.SwitchToNoneMode();
        _lastGoogleError = null;
        OnSyncProviderChangedExternally();
    }

    /// <summary>Switches to Filesystem mode using the current <see cref="SyncFolder"/>.</summary>
    public void ApplyFilesystemFolderMode()
    {
        AppState.Instance.SwitchToFilesystemFolderMode(SyncFolder);
        _lastGoogleError = null;
        OnSyncProviderChangedExternally();
    }

    /// <summary>Triggers the OAuth flow + migration. Surfaces errors via <see cref="GoogleConnectionStatus"/>.</summary>
    public async Task ConnectGoogleAsync(CancellationToken ct = default)
    {
        if (IsConnectingGoogle) return;
        IsConnectingGoogle = true;
        _lastGoogleError = null;
        OnPropertyChanged(nameof(GoogleConnectionStatus));

        try
        {
            await AppState.Instance.ConnectGoogleDriveAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _lastGoogleError = "연결이 취소되었습니다.";
        }
        catch (Exception ex)
        {
            _lastGoogleError = $"Google 연결 실패: {ex.Message}";
        }
        finally
        {
            IsConnectingGoogle = false;
            OnSyncProviderChangedExternally();
        }
    }

    public void DisconnectGoogle()
    {
        AppState.Instance.DisconnectGoogleDrive();
        _lastGoogleError = null;
        OnSyncProviderChangedExternally();
    }

    /// <summary>
    /// Fires PropertyChanged on every sync-mode-derived display property. Public so the View
    /// code-behind can poke the binding when it persists a new <see cref="SyncMode"/> directly
    /// (e.g. when the user picks "Google" radio — we update settings, but the actual provider
    /// swap waits for the Connect button).
    /// </summary>
    public void RefreshSyncModeDisplay()
    {
        OnPropertyChanged(nameof(SyncModeIndex));
        OnPropertyChanged(nameof(IsFolderMode));
        OnPropertyChanged(nameof(IsGoogleMode));
        OnPropertyChanged(nameof(IsGoogleConnected));
        OnPropertyChanged(nameof(GoogleConnectionStatus));
        OnPropertyChanged(nameof(SyncFolder));
    }

    private void OnSyncProviderChangedExternally() => RefreshSyncModeDisplay();
}
