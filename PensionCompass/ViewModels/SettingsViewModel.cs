using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using PensionCompass.Core.Ai;
using PensionCompass.Services;

namespace PensionCompass.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private SettingsService Settings => AppState.Instance.Settings;

    // Captured on construction (UI thread) so background events (cloud refresh) can marshal back.
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();

    public SettingsViewModel()
    {
        AppState.Instance.SyncProviderChanged += OnSyncProviderChanged;
        // Fires when AppState's startup background refresh has just rewritten the cloud-cache
        // vault slots. We only need to nudge the View — the key getters will pick up the new
        // values on re-read.
        AppState.Instance.Settings.ApiKeysReloadedFromCloud += OnApiKeysReloadedFromCloud;
        // v1.3.0: cloud key encryption state (locked / needs passphrase / loaded) drives the
        // passphrase sub-section. May fire on a background thread → marshal to the UI thread.
        AppState.Instance.CloudKeyStatusChanged += OnCloudKeyStatusChanged;
    }

    private void OnSyncProviderChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SyncModeIndex));
        OnPropertyChanged(nameof(IsFolderMode));
        OnPropertyChanged(nameof(IsGoogleMode));
        OnPropertyChanged(nameof(IsGoogleConnected));
        OnPropertyChanged(nameof(GoogleConnectionStatus));
        OnPropertyChanged(nameof(SyncFolder));
        // When the active sync source flips, ClaudeApiKey/etc. now resolve through a different
        // vault resource (local ↔ cloud cache), so the bound PasswordBoxes need to re-read.
        OnPropertyChanged(nameof(ClaudeApiKey));
        OnPropertyChanged(nameof(GeminiApiKey));
        OnPropertyChanged(nameof(GptApiKey));
        RefreshPassphraseDisplay();
    }

    private void OnApiKeysReloadedFromCloud(object? sender, EventArgs e)
        => MarshalToUi(() =>
        {
            OnPropertyChanged(nameof(ClaudeApiKey));
            OnPropertyChanged(nameof(GeminiApiKey));
            OnPropertyChanged(nameof(GptApiKey));
        });

    private void OnCloudKeyStatusChanged(object? sender, EventArgs e)
        => MarshalToUi(RefreshPassphraseDisplay);

    private void MarshalToUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
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
            // WinUI RadioButtons transiently writes back SelectedIndex = -1 when sibling elements
            // toggle visibility (e.g. the passphrase section appearing after an unlock). With a TwoWay
            // binding that -1 would fall through to "Claude" and silently clobber the user's real
            // choice — ignore any out-of-range write so only genuine user selections take effect, and
            // re-raise PropertyChanged so the control snaps back to the real selection (else it can
            // be left visually deselected).
            if (value < 0 || value > 2) { OnPropertyChanged(); return; }
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
            // Same RadioButtons -1-writeback guard as ProviderIndex: ignore out-of-range writes so a
            // transient re-layout glitch can't reset the user's reasoning-effort choice.
            if (value < 0 || value > 3) { OnPropertyChanged(); return; }
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
        PassphraseMessage = string.Empty;
        OnSyncProviderChangedExternally();
    }

    // ──────── Sync passphrase (v1.3.0 — encrypts the cloud API-key bundle) ────────

    /// <summary>The passphrase sub-section is only meaningful once Google is connected.</summary>
    public bool ShowPassphraseSection => IsGoogleConnected;

    /// <summary>"잠금 해제" when an encrypted bundle from another PC is waiting; otherwise "암호구문 설정".</summary>
    public bool IsUnlockMode
        => AppState.Instance.CloudKeys is AppState.CloudKeyStatus.NeedsPassphrase
            or AppState.CloudKeyStatus.WrongPassphrase;

    public string PassphraseButtonText => IsUnlockMode ? "잠금 해제" : "암호구문 설정/변경";

    /// <summary>True when the action is SETTING a passphrase (not unlocking) — drives the "confirm
    /// passphrase" box visibility, since confirmation only makes sense when creating one.</summary>
    public bool IsSetMode => ShowPassphraseSection && !IsUnlockMode;

    /// <summary>Explanatory line under the passphrase box, derived from the current cloud-key state.</summary>
    public string CloudKeyStatusText
    {
        get
        {
            if (!IsGoogleConnected) return string.Empty;
            return AppState.Instance.CloudKeys switch
            {
                AppState.CloudKeyStatus.Loaded
                    => "✓ API 키가 암호구문으로 암호화되어 이 Google 계정에 동기화됩니다. (보호 강도는 암호구문의 강도에 달려 있으니 길고 추측하기 어렵게 정하세요.)",
                AppState.CloudKeyStatus.LoadedPlaintext
                    => "⚠ 클라우드의 API 키가 아직 암호화되지 않았습니다(이전 버전 형식). 아래에 암호구문을 설정하면 즉시 암호화됩니다.",
                AppState.CloudKeyStatus.NeedsPassphrase
                    => "🔒 이 Google 계정에 암호화된 API 키가 있습니다. 다른 PC에서 설정한 암호구문을 입력해 잠금을 해제하세요.",
                AppState.CloudKeyStatus.WrongPassphrase
                    => "⚠ 암호구문이 일치하지 않습니다. 다시 입력해 주세요.",
                AppState.CloudKeyStatus.Corrupt
                    => "⚠ 클라우드의 키 파일이 손상되었습니다. 위에서 API 키를 입력한 상태로 암호구문을 설정하면 그 키로 다시 만듭니다.",
                _ => Settings.HasSyncPassphrase
                    ? "✓ 암호구문이 설정되어 있습니다. 입력하는 API 키는 암호화되어 동기화됩니다."
                    : "암호구문이 설정되지 않았습니다. 설정하면 API 키가 암호화되어 다른 PC와 안전하게 공유됩니다. 설정 전까지 키는 이 PC에만 저장됩니다(클라우드에 올라가지 않음). 암호구문을 잊으면 복구할 수 없으니, 잊었을 때는 키와 암호구문을 다시 설정하면 됩니다.",
            };
        }
    }

    [ObservableProperty]
    private string _passphraseMessage = string.Empty;

    /// <summary>Minimum length enforced when SETTING a passphrase. The cloud envelope is offline-
    /// attackable (salt + ciphertext are downloadable), so passphrase entropy — not the KDF cost — is
    /// the load-bearing control. 8 is the floor; the UI recommends 12+.</summary>
    private const int MinPassphraseLength = 8;

    /// <summary>
    /// Sets a new passphrase (encrypting + uploading current keys) or unlocks an existing encrypted
    /// bundle, depending on the cloud state. The single button covers both — see
    /// <see cref="AppState.ApplyCloudKeyPassphraseAsync"/>. On the SET path we enforce a strength floor
    /// and a confirmation match (a typo would silently bind an unrecoverable key); on the UNLOCK path
    /// neither is enforced — an existing passphrase must be accepted verbatim.
    /// </summary>
    public async Task ApplyPassphraseAsync(string passphrase, string confirm)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            PassphraseMessage = "암호구문을 입력해 주세요.";
            return;
        }

        // Unlock = matching an existing encrypted bundle. Setting = creating/replacing one.
        var isSetting = !IsUnlockMode;
        if (isSetting)
        {
            if (passphrase.Length < MinPassphraseLength)
            {
                PassphraseMessage = $"암호구문은 최소 {MinPassphraseLength}자 이상으로 정해 주세요. 이 암호구문이 노출되면 클라우드의 API 키가 복호화될 수 있으니 길고 추측하기 어렵게(12자 이상 권장) 정하는 것이 안전합니다.";
                return;
            }
            if (!string.Equals(passphrase, confirm, StringComparison.Ordinal))
            {
                PassphraseMessage = "확인란의 암호구문이 일치하지 않습니다. 오타가 있으면 다른 PC에서 키를 풀 수 없게 되니 다시 확인해 주세요.";
                return;
            }
        }

        var result = await AppState.Instance.ApplyCloudKeyPassphraseAsync(passphrase).ConfigureAwait(true);
        PassphraseMessage = result switch
        {
            AppState.PassphraseApplyResult.Unlocked => "✓ 잠금을 해제하고 API 키를 불러왔습니다.",
            AppState.PassphraseApplyResult.Set => "✓ 암호구문을 설정하고 API 키를 암호화해 업로드했습니다.",
            AppState.PassphraseApplyResult.WrongPassphrase => "⚠ 암호구문이 일치하지 않습니다.",
            AppState.PassphraseApplyResult.CorruptNoLocalKeys => "⚠ 클라우드 키 파일이 손상되었고 이 PC에 복구할 키가 없습니다. 위에서 API 키를 먼저 입력한 뒤 다시 설정해 주세요.",
            AppState.PassphraseApplyResult.TransientError => "⚠ 네트워크 오류로 클라우드 키를 확인하지 못했습니다. 잠시 후 다시 시도해 주세요.",
            AppState.PassphraseApplyResult.NotConnected => "먼저 Google 계정을 연결해 주세요.",
            _ => string.Empty,
        };
        RefreshPassphraseDisplay();
        // Unlock/Set may have repopulated the key cache → refresh the PasswordBoxes too.
        OnPropertyChanged(nameof(ClaudeApiKey));
        OnPropertyChanged(nameof(GeminiApiKey));
        OnPropertyChanged(nameof(GptApiKey));
    }

    private void RefreshPassphraseDisplay()
    {
        OnPropertyChanged(nameof(ShowPassphraseSection));
        OnPropertyChanged(nameof(IsUnlockMode));
        OnPropertyChanged(nameof(IsSetMode));
        OnPropertyChanged(nameof(PassphraseButtonText));
        OnPropertyChanged(nameof(CloudKeyStatusText));
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
        RefreshPassphraseDisplay();
    }

    private void OnSyncProviderChangedExternally() => RefreshSyncModeDisplay();
}
