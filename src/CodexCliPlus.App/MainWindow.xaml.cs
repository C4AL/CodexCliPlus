using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CodexCliPlus.Core.Abstractions.Build;
using CodexCliPlus.Core.Abstractions.Configuration;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Abstractions.Updates;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Services;
using CodexCliPlus.Services.Notifications;
using CodexCliPlus.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace CodexCliPlus;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow, IDisposable
{
    private enum StartupState
    {
        UpgradeNotice,
        Preparing,
        FirstRunKeyReveal,
        NativeLogin,
        LoadingManagement,
        Blocked,
    }

    private enum NavigationDockVisualState
    {
        Resting,
        Icons,
        Expanded,
    }

    private const string AppHostName = "codexcliplus-webui.local";
    private const string UiTestModeEnvironmentVariable = "CODEXCLIPLUS_UI_TEST_MODE";
    private const string UiTestWebViewUserDataFolderEnvironmentVariable =
        "CODEXCLIPLUS_WEBVIEW2_USER_DATA_FOLDER";
    private const string UiTestWebViewRemoteDebuggingPortEnvironmentVariable =
        "CODEXCLIPLUS_WEBVIEW2_REMOTE_DEBUGGING_PORT";
    private const int FirstRunConfirmationSeconds = 5;
    private const double NavigationDockRestingWidth = 56;
    private const double NavigationDockIconsWidth = 110;
    private const double NavigationDockExpandedWidth = 264;
    private const double NavigationDockPanelIconsWidth = 58;
    private const double NavigationDockPanelExpandedWidth = 208;
    private const double NavigationDockPanelRestingHeight = 132;
    private const double NavigationDockPanelOpenHeight = 346;
    private const double NavigationDockLabelExpandedWidth = 124;
    private static readonly TimeSpan MinimumPreparationDisplayDuration = TimeSpan.FromMilliseconds(
        2500
    );
    private static readonly Uri AppEntryUri = new($"http://{AppHostName}/index.html");
    private static readonly JsonSerializerOptions WebMessageJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly MainWindowViewModel _viewModel;
    private readonly BackendProcessManager _backendProcessManager;
    private readonly BackendConfigWriter _backendConfigWriter;
    private readonly IManagementSessionService _sessionService;
    private readonly IAppConfigurationService _appConfigurationService;
    private readonly IPathService _pathService;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly IBuildInfo _buildInfo;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly IManagementOverviewService _managementOverviewService;
    private readonly IManagementConfigurationService _managementConfigurationService;
    private readonly WebUiAssetLocator _webUiAssetLocator;
    private readonly ShellNotificationService _notificationService;

    private AppSettings _settings = new();
    private StartupState _startupState = StartupState.Preparing;
    private string? _bootstrapScriptId;
    private string _firstRunManagementKey = string.Empty;
    private string _shellConnectionStatus = "disconnected";
    private string _shellApiBase = string.Empty;
    private string _shellBackendVersion = BackendReleaseMetadata.Version;
    private string _shellTheme = "auto";
    private string _shellResolvedTheme = "light";
    private string _activeWebUiPath = "/";
    private CancellationTokenSource? _firstRunConfirmCountdown;
    private DateTimeOffset? _preparationPanelShownAt;
    private ManagementOverviewSnapshot? _settingsOverview;
    private Window? _settingsWindow;
    private Grid? _settingsWindowRoot;
    private CancellationTokenSource? _settingsOverviewRefreshCts;
    private int _settingsOverviewRefreshRequestId;
    private NavigationDockVisualState _navigationDockState = NavigationDockVisualState.Resting;
    private bool _suppressFollowSystemChange;
    private readonly DispatcherTimer _navigationDockCollapseTimer;
    private bool _allowClose;
    private bool _isInitializing;
    private bool _webViewConfigured;
    private bool _settingsOverlayOpen;
    private bool _isShellBrandDockClosing;
    private bool _sidebarCollapsed;

    public MainWindow(
        MainWindowViewModel viewModel,
        BackendProcessManager backendProcessManager,
        BackendConfigWriter backendConfigWriter,
        IManagementSessionService sessionService,
        IAppConfigurationService appConfigurationService,
        IPathService pathService,
        ISecureCredentialStore credentialStore,
        IBuildInfo buildInfo,
        IUpdateCheckService updateCheckService,
        IManagementOverviewService managementOverviewService,
        IManagementConfigurationService managementConfigurationService,
        WebUiAssetLocator webUiAssetLocator,
        ShellNotificationService notificationService
    )
    {
        _viewModel = viewModel;
        _backendProcessManager = backendProcessManager;
        _backendConfigWriter = backendConfigWriter;
        _sessionService = sessionService;
        _appConfigurationService = appConfigurationService;
        _pathService = pathService;
        _credentialStore = credentialStore;
        _buildInfo = buildInfo;
        _updateCheckService = updateCheckService;
        _managementOverviewService = managementOverviewService;
        _managementConfigurationService = managementConfigurationService;
        _webUiAssetLocator = webUiAssetLocator;
        _notificationService = notificationService;

        DataContext = _viewModel;
        InitializeComponent();

        _navigationDockCollapseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(520),
        };
        _navigationDockCollapseTimer.Tick += NavigationDockCollapseTimer_Tick;

        _backendProcessManager.StatusChanged += BackendProcessManager_StatusChanged;
        _notificationService.NotificationRequested +=
            ShellNotificationService_NotificationRequested;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ShowPreparationStep("目录", 5, "正在加载本地配置。", StartupState.Preparing);
            var settingsFileExists = File.Exists(_pathService.Directories.SettingsFilePath);
            _settings = await _appConfigurationService.LoadAsync();
            ApplyShellTheme(_settings.ThemeMode);
            UpdateShellThemePresentation();
            UpdateShellConnectionPresentation();
            InitializeTrayIcon();

            if (!_settings.SecurityKeyOnboardingCompleted)
            {
                await BeginFirstRunKeyRevealAsync();
                return;
            }

            if (settingsFileExists && IsUpgradeNoticePending())
            {
                await EnsureMinimumPreparationDisplayAsync();
                ShowUpgradeNotice();
                return;
            }

            await ContinueAfterStartupGateAsync();
        }
        catch (Exception exception)
        {
            ShowBlocker(
                "桌面宿主启动失败",
                "无法加载桌面配置或初始化宿主环境。",
                exception.Message
            );
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _backendProcessManager.StatusChanged -= BackendProcessManager_StatusChanged;
        _notificationService.NotificationRequested -=
            ShellNotificationService_NotificationRequested;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _navigationDockCollapseTimer.Tick -= NavigationDockCollapseTimer_Tick;
        _firstRunConfirmCountdown?.Cancel();
        _firstRunConfirmCountdown?.Dispose();
        _settingsOverviewRefreshCts?.Cancel();
        _settingsOverviewRefreshCts?.Dispose();
        CloseSettingsWindow();
        TrayIcon.Unregister();
    }

    public void Dispose()
    {
        _backendProcessManager.StatusChanged -= BackendProcessManager_StatusChanged;
        _notificationService.NotificationRequested -=
            ShellNotificationService_NotificationRequested;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _navigationDockCollapseTimer.Tick -= NavigationDockCollapseTimer_Tick;
        _firstRunConfirmCountdown?.Cancel();
        _firstRunConfirmCountdown?.Dispose();
        _settingsOverviewRefreshCts?.Cancel();
        _settingsOverviewRefreshCts?.Dispose();
        CloseSettingsWindow();
        GC.SuppressFinalize(this);
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_settings.ThemeMode != AppThemeMode.System)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            ApplyShellTheme(_settings.ThemeMode);
            UpdateShellThemePresentation();
            PostWebUiCommand(
                new
                {
                    type = "setTheme",
                    theme = ToWebTheme(_settings.ThemeMode),
                    resolvedTheme = ToWebResolvedTheme(_settings.ThemeMode),
                }
            );
        });
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (_settings.EnableTrayIcon && _settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.SecurityKeyOnboardingCompleted)
        {
            await BeginFirstRunKeyRevealAsync();
            return;
        }

        await InitializeHostAsync(
            restartBackend: _backendProcessManager.CurrentStatus.State != BackendStateKind.Running
        );
    }

    private async void UpgradeContinueButton_Click(object sender, RoutedEventArgs e)
    {
        UpgradeContinueButton.IsEnabled = false;
        try
        {
            _settings.LastSeenApplicationVersion = CurrentApplicationVersion;
            await _appConfigurationService.SaveAsync(_settings);
            await ContinueAfterStartupGateAsync();
        }
        catch (Exception exception)
        {
            ShowBlocker("更新确认失败", "无法保存本次更新确认状态。", exception.Message);
        }
        finally
        {
            UpgradeContinueButton.IsEnabled = true;
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await SignInAsync();
    }

    private async void ManagementKeyPasswordBox_KeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e
    )
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await SignInAsync();
    }

    private void FirstRunCopyKeyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_firstRunManagementKey);
            _notificationService.ShowAuto("安全密钥已复制。");
        }
        catch (Exception exception)
        {
            _notificationService.ShowManual("复制失败", exception.Message);
        }
    }

    private async void FirstRunSaveToDesktopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var filePath = BuildDesktopSecurityKeyFilePath();
            var content = BuildSecurityKeyFileContent(_firstRunManagementKey);
            await File.WriteAllTextAsync(
                filePath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
            _notificationService.ShowAuto($"已保存到桌面：{Path.GetFileName(filePath)}");
        }
        catch (Exception exception)
        {
            _notificationService.ShowManual("保存失败", BuildDesktopSaveErrorMessage(exception));
        }
    }

    private async void FirstRunEnterManagementButton_Click(object sender, RoutedEventArgs e)
    {
        await BeginFirstRunConfirmationAsync();
    }

    private async void FirstRunConfirmContinueButton_Click(object sender, RoutedEventArgs e)
    {
        await CompleteFirstRunAsync();
    }

    private void FirstRunConfirmCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CancelFirstRunConfirmation();
    }

    private async void ForgotSecurityKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var firstConfirm = MessageBox.Show(
            this,
            "重置会清空后端配置、Provider 配置、OAuth/Auth 文件、本机保存的安全密钥和 WebUI 本地登录状态。日志、诊断文件和 backend 资产会保留。",
            "重置安全密钥",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        if (firstConfirm != MessageBoxResult.Yes)
        {
            return;
        }

        var secondConfirm = MessageBox.Show(
            this,
            "确认重置后，现有账号与配置无法通过桌面壳恢复。是否继续？",
            "确认重置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        if (secondConfirm != MessageBoxResult.Yes)
        {
            return;
        }

        await ResetSecurityKeyAsync();
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShellSidebarButton_Click(object sender, RoutedEventArgs e)
    {
        ExpandNavigationDock(showLabels: true);
    }

    private void ShellRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        PostWebUiCommand(new { type = "refreshAll" });
        _notificationService.ShowAuto("已请求刷新。");
    }

    private async void ShellBrandDockButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateShellDockPresentation();
        if (ShellBrandDockPopup.IsOpen)
        {
            await HideShellBrandDockPopupAsync();
            return;
        }

        ShellBrandDockPopup.IsOpen = true;
        if (ShellBrandDockPopup.IsOpen)
        {
            await RefreshShellDockOverviewAsync();
        }
    }

    private void ShellBrandDockPopup_Opened(object sender, EventArgs e)
    {
        AnimateShellBrandDockPopupIn();
    }

    private void ShellBrandDockPopup_Closed(object sender, EventArgs e)
    {
        _isShellBrandDockClosing = false;
        ResetShellBrandDockPopupVisual();
    }

    private void ShellDockCopyBackendAddressButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_shellApiBase))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(_shellApiBase);
            _notificationService.ShowAuto("后端地址已复制。");
        }
        catch (Exception exception)
        {
            _notificationService.ShowManual("复制后端地址失败", exception.Message);
        }
    }

    private async void ShellThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var next = IsEffectiveDarkTheme(_settings.ThemeMode)
            ? AppThemeMode.White
            : AppThemeMode.Dark;

        await SetShellThemeAsync(next);
    }

    private async void ShellSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await HideShellBrandDockPopupAsync();
        if (_settingsOverlayOpen)
        {
            await HideSettingsOverlayAsync();
            return;
        }

        await ShowSettingsOverlayAsync();
    }

    private async void SettingsOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SettingsOverlay))
        {
            await HideSettingsOverlayAsync();
        }
    }

    private void SettingsDialogCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void SilentLoginLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WpfCheckBox checkBox })
        {
            checkBox.IsChecked = checkBox.IsChecked != true;
            e.Handled = true;
        }
    }

    private async void SettingsFollowSystemCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressFollowSystemChange)
        {
            return;
        }

        var followSystem = SettingsFollowSystemCheckBox.IsChecked == true;
        if (followSystem)
        {
            await SetShellThemeAsync(AppThemeMode.System);
            return;
        }

        var explicitTheme = IsEffectiveDarkTheme(AppThemeMode.System)
            ? AppThemeMode.Dark
            : AppThemeMode.White;
        await SetShellThemeAsync(explicitTheme);
    }

    private void SettingsOpenMainRepoButton_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal("https://github.com/router-for-me/CLIProxyAPI");
    }

    private void SettingsOpenWebUiRepoButton_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal("https://github.com/router-for-me/Cli-Proxy-API-Management-Center");
    }

    private void SettingsOpenDocsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal("https://help.router-for.me/");
    }

    private async void SettingsClearLoginButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsClearLoginButton.IsEnabled = false;
        try
        {
            await ResetWebUiLocalAuthStateAsync();
            _settings.ManagementKey = string.Empty;
            _settings.RememberManagementKey = false;
            await _appConfigurationService.SaveAsync(_settings);
            RememberManagementKeyCheckBox.IsChecked = false;
            await HideSettingsOverlayAsync();
            ShowLogin("本地登录信息已清理，请重新输入安全密钥。");
            _notificationService.ShowAuto("本地登录信息已清理。");
        }
        catch (Exception exception)
        {
            _notificationService.ShowManual("清理登录失败", exception.Message);
        }
        finally
        {
            SettingsClearLoginButton.IsEnabled = true;
        }
    }

    private void HideToTrayButton_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void TrayIcon_LeftDoubleClick(object sender, RoutedEventArgs e)
    {
        RestoreFromTray();
    }

    private void TrayOpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RestoreFromTray();
    }

    private async void TrayRestartBackendMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.SecurityKeyOnboardingCompleted)
        {
            await BeginFirstRunKeyRevealAsync();
            return;
        }

        if (
            string.IsNullOrWhiteSpace(_settings.ManagementKey)
            && _backendProcessManager.CurrentStatus.State != BackendStateKind.Running
        )
        {
            ShowLogin("请先输入安全密钥。");
            return;
        }

        await InitializeHostAsync(restartBackend: true);
    }

    private async void TrayCheckUpdatesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _updateCheckService.CheckAsync(_buildInfo.ApplicationVersion);
            var title = result.IsUpdateAvailable ? "发现更新" : "检查更新";
            var message = result.IsUpdateAvailable
                ? $"检测到新版本：{result.LatestVersion}{Environment.NewLine}{Environment.NewLine}{result.Detail}"
                : $"{result.Status}{Environment.NewLine}{Environment.NewLine}{result.Detail}";
            MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "检查更新失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private async void TrayExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        TrayIcon.Unregister();
        await _backendProcessManager.StopAsync();
        Close();
    }

    private void DragRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        e.Handled = true;

        if (e.ClickCount == 2)
        {
            WindowState =
                WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException) { }
    }

    private async Task ContinueAfterStartupGateAsync()
    {
        RememberManagementKeyCheckBox.IsChecked = _settings.RememberManagementKey;

        if (_settings.RememberManagementKey && !string.IsNullOrWhiteSpace(_settings.ManagementKey))
        {
            if (_backendConfigWriter.VerifyManagementKey(_settings.ManagementKey))
            {
                await InitializeHostAsync(restartBackend: false);
                return;
            }

            _settings.ManagementKey = string.Empty;
            await EnsureMinimumPreparationDisplayAsync();
            ShowLogin("本机保存的安全密钥无法通过验证，请重新输入。");
            return;
        }

        await EnsureMinimumPreparationDisplayAsync();
        ShowLogin();
    }

    private async Task BeginFirstRunKeyRevealAsync()
    {
        ShowPreparationStep("配置", 35, "正在生成首次安全密钥。", StartupState.Preparing);

        _firstRunManagementKey = GenerateSecurityKey();
        _settings.ManagementKey = _firstRunManagementKey;
        _settings.RememberManagementKey = false;
        _settings.SecurityKeyOnboardingCompleted = false;

        await _backendConfigWriter.WriteAsync(
            _settings,
            new BackendConfigWriteOptions
            {
                AllowManagementKeyRotation = true,
                ValidatePort = false,
            }
        );

        FirstRunSecurityKeyTextBox.Text = _firstRunManagementKey;
        FirstRunSecurityKeyTextBox.CaretIndex = 0;
        FirstRunSecurityKeyTextBox.ScrollToHome();
        FirstRunRememberSecurityKeyCheckBox.IsChecked = false;
        FirstRunConfirmPanel.Visibility = Visibility.Collapsed;
        FirstRunEnterManagementButton.IsEnabled = true;
        await EnsureMinimumPreparationDisplayAsync();
        ShowFirstRunKeyReveal();
    }

    private async Task BeginFirstRunConfirmationAsync()
    {
        if (string.IsNullOrWhiteSpace(_firstRunManagementKey))
        {
            _notificationService.ShowManual("安全密钥尚未生成", "请重试。");
            return;
        }

        FirstRunConfirmPanel.Visibility = Visibility.Visible;
        FirstRunConfirmContinueButton.IsEnabled = false;
        FirstRunConfirmCloseButton.IsEnabled = true;
        FirstRunConfirmContinueButton.Content = $"确认 ({FirstRunConfirmationSeconds})";

        _firstRunConfirmCountdown?.Cancel();
        _firstRunConfirmCountdown?.Dispose();
        _firstRunConfirmCountdown = new CancellationTokenSource();
        var token = _firstRunConfirmCountdown.Token;

        try
        {
            for (var seconds = FirstRunConfirmationSeconds; seconds > 0; seconds--)
            {
                FirstRunConfirmContinueButton.Content = $"确认 ({seconds})";
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }

            FirstRunConfirmContinueButton.Content = "确认";
            FirstRunConfirmContinueButton.IsEnabled = true;
        }
        catch (TaskCanceledException) { }
    }

    private async Task CompleteFirstRunAsync()
    {
        if (string.IsNullOrWhiteSpace(_firstRunManagementKey))
        {
            _notificationService.ShowManual("安全密钥尚未生成", "请重试。");
            return;
        }

        FirstRunConfirmContinueButton.IsEnabled = false;
        FirstRunConfirmCloseButton.IsEnabled = false;

        try
        {
            _firstRunConfirmCountdown?.Cancel();
            _settings.ManagementKey = _firstRunManagementKey;
            _settings.RememberManagementKey = FirstRunRememberSecurityKeyCheckBox.IsChecked == true;
            _settings.SecurityKeyOnboardingCompleted = true;
            _settings.LastSeenApplicationVersion = CurrentApplicationVersion;
            await _appConfigurationService.SaveAsync(_settings);

            RememberManagementKeyCheckBox.IsChecked = _settings.RememberManagementKey;
            FirstRunConfirmPanel.Visibility = Visibility.Collapsed;

            _firstRunManagementKey = string.Empty;
            FirstRunSecurityKeyTextBox.Text = string.Empty;
            if (_settings.RememberManagementKey)
            {
                await InitializeHostAsync(restartBackend: false);
                return;
            }

            _settings.ManagementKey = string.Empty;
            await _appConfigurationService.SaveAsync(_settings);
            ShowLogin("初始化已完成，请输入安全密钥登录。");
        }
        catch (Exception exception)
        {
            FirstRunConfirmContinueButton.IsEnabled = true;
            FirstRunConfirmCloseButton.IsEnabled = true;
            _notificationService.ShowManual("初始化失败", exception.Message);
            FirstRunConfirmPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelFirstRunConfirmation()
    {
        _firstRunConfirmCountdown?.Cancel();
        FirstRunConfirmPanel.Visibility = Visibility.Collapsed;
        FirstRunConfirmContinueButton.IsEnabled = false;
        FirstRunConfirmContinueButton.Content = "确认";
        FirstRunConfirmCloseButton.IsEnabled = true;
    }

    private async Task SignInAsync()
    {
        var managementKey = ManagementKeyPasswordBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(managementKey))
        {
            ShowLoginError("请输入安全密钥。");
            ManagementKeyPasswordBox.Focus();
            return;
        }

        LoginButton.IsEnabled = false;
        ForgotSecurityKeyButton.IsEnabled = false;
        LoginErrorText.Visibility = Visibility.Collapsed;

        try
        {
            if (!_backendConfigWriter.HasExistingManagementKeyHash())
            {
                ShowLoginError("未找到后端安全密钥配置，请重置后重新初始化。");
                return;
            }

            if (!_backendConfigWriter.VerifyManagementKey(managementKey))
            {
                ShowLoginError("安全密钥不正确。");
                return;
            }

            _settings.ManagementKey = managementKey;
            _settings.RememberManagementKey = RememberManagementKeyCheckBox.IsChecked == true;
            _settings.SecurityKeyOnboardingCompleted = true;
            await _appConfigurationService.SaveAsync(_settings);
            await InitializeHostAsync(restartBackend: false);
        }
        catch (Exception exception)
        {
            ShowLoginError(exception.Message);
        }
        finally
        {
            if (LoginPanel.Visibility == Visibility.Visible)
            {
                LoginButton.IsEnabled = true;
                ForgotSecurityKeyButton.IsEnabled = true;
            }
        }
    }

    private async Task ResetSecurityKeyAsync()
    {
        LoginButton.IsEnabled = false;
        ForgotSecurityKeyButton.IsEnabled = false;
        ShowPreparationStep("配置", 20, "正在重置安全密钥和本地认证状态。", StartupState.Preparing);

        try
        {
            var configuredAuthDirectory = TryReadConfiguredAuthDirectory();

            await _backendProcessManager.StopAsync();
            await ResetWebUiLocalAuthStateAsync();

            await _credentialStore.DeleteSecretAsync(_settings.ManagementKeyReference);
            await _credentialStore.DeleteSecretAsync(AppConstants.DefaultManagementKeyReference);

            DeleteFileIfExistsInsideRoot(_pathService.Directories.BackendConfigFilePath);
            DeleteFileIfExistsInsideRoot(_pathService.Directories.SettingsFilePath);
            DeleteDirectoryIfExistsInsideRoot(
                Path.Combine(
                    _pathService.Directories.ConfigDirectory,
                    AppConstants.SecretsDirectoryName
                )
            );
            DeleteDirectoryIfExistsInsideRoot(
                Path.Combine(_pathService.Directories.BackendDirectory, "auth")
            );
            if (
                !string.IsNullOrWhiteSpace(configuredAuthDirectory)
                && IsPathInsideAppRoot(configuredAuthDirectory)
            )
            {
                DeleteDirectoryIfExistsInsideRoot(configuredAuthDirectory);
            }

            _firstRunManagementKey = string.Empty;
            ManagementKeyPasswordBox.Password = string.Empty;
            _settings = await _appConfigurationService.LoadAsync();
            await BeginFirstRunKeyRevealAsync();
        }
        catch (Exception exception)
        {
            ShowBlocker("安全密钥重置失败", "未能完成本地配置和认证状态清理。", exception.Message);
        }
    }

    private async void BackendProcessManager_StatusChanged(
        object? sender,
        BackendStatusSnapshot snapshot
    )
    {
        if (_isInitializing || snapshot.State != BackendStateKind.Error)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            ShowBlocker(
                "后端不可用",
                "本地后端未能保持运行，桌面宿主已暂停管理界面。",
                snapshot.LastError ?? snapshot.Message
            );
        });
    }

    private async Task InitializeHostAsync(bool restartBackend)
    {
        if (_isInitializing)
        {
            return;
        }

        _isInitializing = true;
        _shellConnectionStatus = "connecting";
        UpdateShellConnectionPresentation();
        ShowPreparationStep("目录", 10, "正在准备本地目录。", StartupState.Preparing);

        try
        {
            await _pathService.EnsureCreatedAsync();

            ShowPreparationStep(
                "WebView2",
                25,
                "正在检查 WebView2 运行时。",
                StartupState.Preparing
            );
            EnsureWebView2Runtime();

            ShowPreparationStep(
                "后端资产",
                40,
                "正在定位管理界面和后端资产。",
                StartupState.Preparing
            );
            var bundle = _webUiAssetLocator.GetRequiredBundle();

            ShowPreparationStep("配置", 55, "正在确认后端配置。", StartupState.Preparing);
            if (
                restartBackend
                && _backendProcessManager.CurrentStatus.State == BackendStateKind.Running
            )
            {
                ShowPreparationStep("核心启动", 68, "正在重启本地后端。", StartupState.Preparing);
                await _backendProcessManager.RestartAsync();
            }

            ShowPreparationStep("核心启动", 72, "正在启动本地后端。", StartupState.Preparing);
            var connection = await _sessionService.GetConnectionAsync();

            ShowPreparationStep("健康检查", 86, "本地后端健康检查已通过。", StartupState.Preparing);
            var payload = new DesktopBootstrapPayload
            {
                DesktopMode = true,
                ApiBase = connection.BaseUrl,
                ManagementKey = connection.ManagementKey,
                Theme = ToWebTheme(_settings.ThemeMode),
                ResolvedTheme = ToWebResolvedTheme(_settings.ThemeMode),
                SidebarCollapsed = _sidebarCollapsed,
            };

            _shellApiBase = connection.BaseUrl;
            _shellConnectionStatus = "connected";
            UpdateShellConnectionPresentation();

            ShowPreparationStep(
                "管理桥接",
                95,
                "正在打开管理界面。",
                StartupState.LoadingManagement
            );
            await EnsureWebViewAsync(bundle, payload);
            await EnsureMinimumPreparationDisplayAsync();
            ShowWebView();
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            ShowBlocker(
                "缺少 WebView2 运行时",
                "当前系统未安装 Microsoft Edge WebView2 Runtime，桌面宿主无法承载管理界面。",
                exception.Message
            );
        }
        catch (FileNotFoundException exception)
        {
            ShowBlocker(
                "缺少管理界面资源",
                "前端静态资源未找到，桌面宿主无法继续启动。",
                exception.Message
            );
        }
        catch (Exception exception)
        {
            ShowBlocker(
                "桌面宿主启动失败",
                "管理界面宿主未能完成初始化，请先修复阻断错误。",
                exception.Message
            );
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private async Task EnsureWebViewAsync(WebUiBundleInfo bundle, DesktopBootstrapPayload payload)
    {
        if (!_webViewConfigured)
        {
            var environment = await CreateWebViewEnvironmentAsync();
            if (environment is null)
            {
                await ManagementWebView.EnsureCoreWebView2Async();
            }
            else
            {
                await ManagementWebView.EnsureCoreWebView2Async(environment);
            }

            ConfigureWebView(bundle);
            _webViewConfigured = true;
        }

        await UpdateBootstrapScriptAsync(payload);
        ManagementWebView.CoreWebView2.Navigate(AppEntryUri.ToString());
    }

    private async Task<CoreWebView2Environment?> CreateWebViewEnvironmentAsync()
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable(UiTestModeEnvironmentVariable),
                "1",
                StringComparison.Ordinal
            )
        )
        {
            return null;
        }

        var userDataFolder = Environment.GetEnvironmentVariable(
            UiTestWebViewUserDataFolderEnvironmentVariable
        );
        if (string.IsNullOrWhiteSpace(userDataFolder))
        {
            userDataFolder = Path.Combine(
                _pathService.Directories.RuntimeDirectory,
                "webview2-test-profile"
            );
        }

        Directory.CreateDirectory(userDataFolder);

        var options = new CoreWebView2EnvironmentOptions();
        var remoteDebuggingPort = Environment.GetEnvironmentVariable(
            UiTestWebViewRemoteDebuggingPortEnvironmentVariable
        );
        if (int.TryParse(remoteDebuggingPort, out var port) && port is > 0 and <= 65535)
        {
            options.AdditionalBrowserArguments = $"--remote-debugging-port={port}";
        }

        return await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder,
            options: options
        );
    }

    private void ConfigureWebView(WebUiBundleInfo bundle)
    {
        var core = ManagementWebView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = _settings.EnableDebugTools;
        core.Settings.AreDefaultContextMenusEnabled = _settings.EnableDebugTools;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;
        core.SetVirtualHostNameToFolderMapping(
            AppHostName,
            bundle.DistDirectory,
            CoreWebView2HostResourceAccessKind.Allow
        );

        core.NavigationStarting += CoreWebView2_NavigationStarting;
        core.NewWindowRequested += CoreWebView2_NewWindowRequested;
        core.WebMessageReceived += CoreWebView2_WebMessageReceived;
        core.ProcessFailed += CoreWebView2_ProcessFailed;
    }

    private async Task UpdateBootstrapScriptAsync(DesktopBootstrapPayload payload)
    {
        if (_bootstrapScriptId is not null)
        {
            ManagementWebView.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(
                _bootstrapScriptId
            );
        }

        _bootstrapScriptId =
            await ManagementWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                DesktopBridgeScriptFactory.CreateInitializationScript(payload)
            );
    }

    private void CoreWebView2_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e
    )
    {
        if (
            string.IsNullOrWhiteSpace(e.Uri) || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
        )
        {
            return;
        }

        if (uri.Host.Equals(AppHostName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (
            !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        e.Cancel = true;
        OpenExternal(uri.ToString());
    }

    private void CoreWebView2_NewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e
    )
    {
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(e.Uri))
        {
            OpenExternal(e.Uri);
        }
    }

    private void CoreWebView2_WebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e
    )
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            switch (typeElement.GetString())
            {
                case "openExternal":
                    var url = root.TryGetProperty("url", out var urlElement)
                        ? urlElement.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        OpenExternal(url);
                    }

                    break;

                case "requestNativeLogin":
                    var message = root.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString()
                        : null;
                    ShowLogin(
                        string.IsNullOrWhiteSpace(message)
                            ? "登录状态已失效，请重新输入安全密钥。"
                            : message
                    );
                    break;

                case "shellStateChanged":
                    ApplyWebUiShellState(root);
                    break;

                case "navigationHoverZone":
                    var navigationHoverZoneActive =
                        root.TryGetProperty("active", out var activeElement)
                        && activeElement.ValueKind is JsonValueKind.True;
                    if (navigationHoverZoneActive)
                    {
                        ExpandNavigationDock(showLabels: false);
                    }
                    else
                    {
                        CollapseNavigationDockWithDelay(shortDelay: false);
                    }

                    break;
            }
        }
        catch { }
    }

    private void ApplyWebUiShellState(JsonElement root)
    {
        if (root.TryGetProperty("connectionStatus", out var connectionStatusElement))
        {
            _shellConnectionStatus = NormalizeConnectionStatus(connectionStatusElement.GetString());
        }

        if (root.TryGetProperty("apiBase", out var apiBaseElement))
        {
            _shellApiBase = apiBaseElement.GetString()?.Trim() ?? string.Empty;
        }

        if (root.TryGetProperty("theme", out var themeElement))
        {
            _shellTheme = NormalizeWebTheme(themeElement.GetString());
        }

        if (root.TryGetProperty("resolvedTheme", out var resolvedThemeElement))
        {
            _shellResolvedTheme = NormalizeResolvedTheme(resolvedThemeElement.GetString());
        }

        if (
            root.TryGetProperty("sidebarCollapsed", out var sidebarCollapsedElement)
            && (sidebarCollapsedElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
        )
        {
            _sidebarCollapsed = sidebarCollapsedElement.GetBoolean();
        }

        if (root.TryGetProperty("pathname", out var pathnameElement))
        {
            _activeWebUiPath = NormalizeRoutePath(pathnameElement.GetString());
        }

        UpdateShellConnectionPresentation();
        UpdateNavigationActiveState();
    }

    private void CoreWebView2_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ShowBlocker(
                "WebView2 进程异常退出",
                "管理界面渲染进程发生故障，桌面宿主已停止继续渲染空白页面。",
                e.ProcessFailedKind.ToString()
            );
        });
    }

    private void ShowPreparationStep(
        string step,
        double progress,
        string description,
        StartupState state
    )
    {
        _startupState = state;
        if (LoadingPanel.Visibility != Visibility.Visible || _preparationPanelShownAt is null)
        {
            _preparationPanelShownAt = DateTimeOffset.UtcNow;
        }

        LoadingTitleText.Text =
            state == StartupState.LoadingManagement ? "正在进入管理界面" : "正在准备桌面管理界面";
        LoadingDescriptionText.Text = description;
        LoadingStatusText.Text = BuildPreparationStatus(progress, state);
        PreparationStepText.Text = $"当前步骤：{step}";
        var normalizedProgress = Math.Clamp(progress, 0, 100);
        PreparationProgressBar.BeginAnimation(
            RangeBase.ValueProperty,
            new DoubleAnimation(normalizedProgress, new Duration(TimeSpan.FromMilliseconds(320)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            }
        );
        PreparationProgressFillScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(
                normalizedProgress / 100,
                new Duration(TimeSpan.FromMilliseconds(420))
            )
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            }
        );

        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
    }

    private void ShowUpgradeNotice()
    {
        _startupState = StartupState.UpgradeNotice;
        _preparationPanelShownAt = null;
        var previousVersion = string.IsNullOrWhiteSpace(_settings.LastSeenApplicationVersion)
            ? "旧版本"
            : _settings.LastSeenApplicationVersion.Trim();
        UpgradeNoticeVersionText.Text =
            $"已从 {previousVersion} 升级到 {CurrentApplicationVersion}";

        UpgradeNoticePanel.Visibility = Visibility.Visible;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
    }

    private void ShowFirstRunKeyReveal()
    {
        _startupState = StartupState.FirstRunKeyReveal;
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        FirstRunKeyPanel.Visibility = Visibility.Visible;
        LoginPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
        FirstRunSecurityKeyTextBox.Focus();
    }

    private void ShowLogin(string? errorMessage = null)
    {
        _startupState = StartupState.NativeLogin;
        _shellConnectionStatus = "disconnected";
        UpdateShellConnectionPresentation();
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
        LoginButton.IsEnabled = true;
        ForgotSecurityKeyButton.IsEnabled = true;
        RememberManagementKeyCheckBox.IsChecked = _settings.RememberManagementKey;
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            LoginErrorText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ShowLoginError(errorMessage);
        }

        ManagementKeyPasswordBox.Focus();
    }

    private void ShowLoginError(string message)
    {
        LoginErrorText.Text = message;
        LoginErrorText.Visibility = Visibility.Visible;
    }

    private void ShowBlocker(string title, string description, string detail)
    {
        _startupState = StartupState.Blocked;
        _shellConnectionStatus = "error";
        UpdateShellConnectionPresentation();
        _preparationPanelShownAt = null;
        BlockerTitleText.Text = title;
        BlockerDescriptionText.Text = description;
        BlockerDetailText.Text = detail;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
    }

    private void ShowWebView()
    {
        _shellConnectionStatus = "connected";
        UpdateShellConnectionPresentation();
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Visible;
        UpdateNavigationDockPopupVisibility();
    }

    private void UpdateNavigationDockPopupVisibility()
    {
        SetNavigationDockPopupOpen(
            ManagementWebView.Visibility == Visibility.Visible && !_settingsOverlayOpen
        );
    }

    private void SetNavigationDockPopupOpen(bool isOpen)
    {
        if (!isOpen)
        {
            AnimateNavigationDock(NavigationDockVisualState.Resting);
        }

        if (ShellNavigationDockPopup.IsOpen != isOpen)
        {
            ShellNavigationDockPopup.IsOpen = isOpen;
        }
    }

    private void UpdateShellConnectionPresentation()
    {
        UpdateShellDockPresentation();

        if (_settingsOverlayOpen)
        {
            UpdateSettingsOverlayBaseline();
        }
    }

    private void UpdateShellDockPresentation()
    {
        var statusBrush = GetConnectionStatusBrush();
        ShellBrandStatusDot.Fill = statusBrush;
        ShellDockConnectionStatusDot.Fill = statusBrush;
        if (ShellBrandStatusDotGlow is DropShadowEffect glow)
        {
            glow.Color = GetConnectionStatusGlowColor();
        }

        ShellDockAppVersionText.Text = CurrentApplicationVersion;
        ShellDockCoreVersionText.Text = FormatUnknown(_shellBackendVersion);
        ShellDockConnectionStatusText.Text = ShellConnectionStatusLabel;
        ShellDockBackendAddressText.Text = string.IsNullOrWhiteSpace(_shellApiBase)
            ? "-"
            : _shellApiBase;
        ShellDockCopyBackendAddressButton.IsEnabled = !string.IsNullOrWhiteSpace(_shellApiBase);
    }

    private void AnimateShellBrandDockPopupIn()
    {
        _isShellBrandDockClosing = false;
        ShellBrandDockCard.Opacity = 0;
        ShellBrandDockScaleTransform.ScaleX = 0.98;
        ShellBrandDockScaleTransform.ScaleY = 0.98;
        ShellBrandDockTranslateTransform.Y = -6;

        ShellBrandDockCard.BeginAnimation(UIElement.OpacityProperty, CreateEaseAnimation(1, 150));
        ShellBrandDockScaleTransform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            CreateEaseAnimation(1, 180)
        );
        ShellBrandDockScaleTransform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            CreateEaseAnimation(1, 180)
        );
        ShellBrandDockTranslateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            CreateEaseAnimation(0, 180)
        );
    }

    private async Task HideShellBrandDockPopupAsync()
    {
        if (!ShellBrandDockPopup.IsOpen || _isShellBrandDockClosing)
        {
            return;
        }

        _isShellBrandDockClosing = true;
        ShellBrandDockCard.BeginAnimation(UIElement.OpacityProperty, CreateEaseAnimation(0, 120));
        ShellBrandDockScaleTransform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            CreateEaseAnimation(0.985, 130)
        );
        ShellBrandDockScaleTransform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            CreateEaseAnimation(0.985, 130)
        );
        ShellBrandDockTranslateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            CreateEaseAnimation(-5, 130)
        );

        await Task.Delay(TimeSpan.FromMilliseconds(135));
        ShellBrandDockPopup.IsOpen = false;
    }

    private void ResetShellBrandDockPopupVisual()
    {
        ShellBrandDockCard.BeginAnimation(UIElement.OpacityProperty, null);
        ShellBrandDockScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ShellBrandDockScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ShellBrandDockTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
        ShellBrandDockCard.Opacity = 0;
        ShellBrandDockScaleTransform.ScaleX = 0.98;
        ShellBrandDockScaleTransform.ScaleY = 0.98;
        ShellBrandDockTranslateTransform.Y = -6;
    }

    private SolidColorBrush GetConnectionStatusBrush()
    {
        return _shellConnectionStatus switch
        {
            "connected" => new SolidColorBrush(WpfColor.FromRgb(20, 184, 166)),
            "connecting" => new SolidColorBrush(WpfColor.FromRgb(245, 158, 11)),
            "error" => new SolidColorBrush(WpfColor.FromRgb(244, 63, 94)),
            _ => new SolidColorBrush(WpfColor.FromRgb(148, 163, 184)),
        };
    }

    private WpfColor GetConnectionStatusGlowColor()
    {
        return _shellConnectionStatus switch
        {
            "connected" => WpfColor.FromRgb(20, 184, 166),
            "connecting" => WpfColor.FromRgb(245, 158, 11),
            "error" => WpfColor.FromRgb(244, 63, 94),
            _ => WpfColor.FromRgb(148, 163, 184),
        };
    }

    private async Task SetShellThemeAsync(AppThemeMode themeMode)
    {
        _settings.ThemeMode = themeMode;
        ApplyShellTheme(themeMode);
        UpdateShellThemePresentation();
        await _appConfigurationService.SaveAsync(_settings);
        PostWebUiCommand(
            new
            {
                type = "setTheme",
                theme = ToWebTheme(themeMode),
                resolvedTheme = ToWebResolvedTheme(themeMode),
            }
        );
    }

    private void ApplyShellTheme(AppThemeMode themeMode)
    {
        var dark = IsEffectiveDarkTheme(themeMode);
        if (dark)
        {
            SetBrushResource("ApplicationBackgroundBrush", "#111317");
            SetBrushResource("SurfaceBrush", "#181B20");
            SetBrushResource("SurfaceAltBrush", "#23272F");
            SetBrushResource("AccentBrush", "#2DD4BF");
            SetBrushResource("AccentSoftBrush", "#123A36");
            SetBrushResource("PrimaryTextBrush", "#EEF2F7");
            SetBrushResource("SecondaryTextBrush", "#AAB4C0");
            SetBrushResource("BorderBrush", "#343A46");
            SetBrushResource("ShellDockGlassBrush", "#D01E242D");
            SetBrushResource("ShellDockGlassBorderBrush", "#55FFFFFF");
            SetBrushResource("ShellDockGlassHighlightBrush", "#75FFFFFF");
            SetBrushResource("NavigationDockPanelBrush", "#D01E242D");
            SetBrushResource("NavigationDockBorderBrush", "#55FFFFFF");
            SetBrushResource("NavigationDockInnerHighlightBrush", "#66FFFFFF");
            SetBrushResource("NavigationDockRailBrush", "#F22DD4BF");
            SetBrushResource("NavigationDockRailTrackBrush", "#2438BDF8");
            SetBrushResource("NavigationDockButtonHoverBrush", "#2438BDF8");
        }
        else
        {
            SetBrushResource("ApplicationBackgroundBrush", "#F4F6FA");
            SetBrushResource("SurfaceBrush", "#FFFFFF");
            SetBrushResource("SurfaceAltBrush", "#EEF2F7");
            SetBrushResource("AccentBrush", "#0F766E");
            SetBrushResource("AccentSoftBrush", "#D9F1EE");
            SetBrushResource("PrimaryTextBrush", "#17202B");
            SetBrushResource("SecondaryTextBrush", "#526070");
            SetBrushResource("BorderBrush", "#D7DCE5");
            SetBrushResource("ShellDockGlassBrush", "#EAF8FAFC");
            SetBrushResource("ShellDockGlassBorderBrush", "#BFFFFFFF");
            SetBrushResource("ShellDockGlassHighlightBrush", "#FFFFFFFF");
            SetBrushResource("NavigationDockPanelBrush", "#EAF8FAFC");
            SetBrushResource("NavigationDockBorderBrush", "#BFFFFFFF");
            SetBrushResource("NavigationDockInnerHighlightBrush", "#FFFFFFFF");
            SetBrushResource("NavigationDockRailBrush", "#EA14B8A6");
            SetBrushResource("NavigationDockRailTrackBrush", "#320F766E");
            SetBrushResource("NavigationDockButtonHoverBrush", "#2A14B8A6");
        }
    }

    private void UpdateShellThemePresentation()
    {
        _shellTheme = ToWebTheme(_settings.ThemeMode);
        _shellResolvedTheme = ToWebResolvedTheme(_settings.ThemeMode);
        var label = _settings.ThemeMode switch
        {
            AppThemeMode.White => "纯白",
            AppThemeMode.Dark => "暗色",
            _ => "跟随系统",
        };

        ShellThemeButton.ToolTip = $"主题：{label}";
        _suppressFollowSystemChange = true;
        SettingsFollowSystemCheckBox.IsChecked = _settings.ThemeMode == AppThemeMode.System;
        _suppressFollowSystemChange = false;
    }

    private void PostWebUiCommand(object message)
    {
        if (!_webViewConfigured || ManagementWebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            ManagementWebView.CoreWebView2.PostWebMessageAsJson(
                JsonSerializer.Serialize(message, WebMessageJsonOptions)
            );
        }
        catch { }
    }

    private void ShellNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.CommandParameter is not string path)
        {
            return;
        }

        NavigateWebUiRoute(path);
        CollapseNavigationDockWithDelay(shortDelay: true);
    }

    private void NavigateWebUiRoute(string path)
    {
        _activeWebUiPath = NormalizeRoutePath(path);
        UpdateNavigationActiveState();
        PostWebUiCommand(new { type = "navigate", path = _activeWebUiPath });
    }

    private void ShellNavigationDockHost_MouseEnter(
        object sender,
        System.Windows.Input.MouseEventArgs e
    )
    {
        ExpandNavigationDock(showLabels: false);
    }

    private void ShellNavigationDockHost_MouseMove(
        object sender,
        System.Windows.Input.MouseEventArgs e
    )
    {
        ExpandNavigationDock(showLabels: ShellNavigationPanel.IsMouseOver);
    }

    private void ShellNavigationPanel_MouseEnter(
        object sender,
        System.Windows.Input.MouseEventArgs e
    )
    {
        ExpandNavigationDock(showLabels: true);
    }

    private void ShellNavigationPanel_MouseLeave(
        object sender,
        System.Windows.Input.MouseEventArgs e
    )
    {
        if (ShellNavigationDockHost.IsMouseOver)
        {
            ExpandNavigationDock(showLabels: false);
        }
    }

    private void ShellNavigationDockHost_MouseLeave(
        object sender,
        System.Windows.Input.MouseEventArgs e
    )
    {
        CollapseNavigationDockWithDelay(shortDelay: false);
    }

    private void NavigationDockCollapseTimer_Tick(object? sender, EventArgs e)
    {
        _navigationDockCollapseTimer.Stop();
        if (ShellNavigationDockHost.IsMouseOver)
        {
            ExpandNavigationDock(showLabels: ShellNavigationPanel.IsMouseOver);
            return;
        }

        AnimateNavigationDock(NavigationDockVisualState.Resting);
    }

    private void ExpandNavigationDock(bool showLabels)
    {
        _navigationDockCollapseTimer.Stop();
        AnimateNavigationDock(
            showLabels ? NavigationDockVisualState.Expanded : NavigationDockVisualState.Icons
        );
    }

    private void CollapseNavigationDockWithDelay(bool shortDelay)
    {
        _navigationDockCollapseTimer.Stop();
        _navigationDockCollapseTimer.Interval = TimeSpan.FromMilliseconds(shortDelay ? 110 : 170);
        _navigationDockCollapseTimer.Start();
    }

    private void AnimateNavigationDock(NavigationDockVisualState state)
    {
        if (_navigationDockState == state)
        {
            ShellNavigationPanel.IsHitTestVisible = state != NavigationDockVisualState.Resting;
            ApplyNavigationDockLabelState(
                expanded: state == NavigationDockVisualState.Expanded,
                durationMilliseconds: 0
            );
            return;
        }

        _navigationDockState = state;
        var hostWidth = state switch
        {
            NavigationDockVisualState.Expanded => NavigationDockExpandedWidth,
            NavigationDockVisualState.Icons => NavigationDockIconsWidth,
            _ => NavigationDockRestingWidth,
        };
        var panelWidth =
            state == NavigationDockVisualState.Expanded
                ? NavigationDockPanelExpandedWidth
                : NavigationDockPanelIconsWidth;
        var panelHeight =
            state == NavigationDockVisualState.Resting
                ? NavigationDockPanelRestingHeight
                : NavigationDockPanelOpenHeight;
        var panelOpacity = state == NavigationDockVisualState.Resting ? 0 : 1;
        var railOpacity = state == NavigationDockVisualState.Resting ? 1 : 0.38;
        var railTrackOpacity = state == NavigationDockVisualState.Resting ? 0.58 : 0.12;
        var panelOffset = state == NavigationDockVisualState.Resting ? -6 : 0;
        var duration = state == NavigationDockVisualState.Resting ? 130 : 240;
        var panelDuration = state == NavigationDockVisualState.Resting ? 110 : 210;
        ShellNavigationPanel.IsHitTestVisible = state != NavigationDockVisualState.Resting;
        ApplyNavigationDockLabelState(
            expanded: state == NavigationDockVisualState.Expanded,
            durationMilliseconds: panelDuration
        );
        ShellNavigationDockHost.BeginAnimation(
            FrameworkElement.WidthProperty,
            new DoubleAnimation(hostWidth, new Duration(TimeSpan.FromMilliseconds(duration)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            }
        );
        ShellNavigationPanel.BeginAnimation(
            FrameworkElement.WidthProperty,
            new DoubleAnimation(panelWidth, new Duration(TimeSpan.FromMilliseconds(duration)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            }
        );
        ShellNavigationPanel.BeginAnimation(
            FrameworkElement.HeightProperty,
            new DoubleAnimation(panelHeight, new Duration(TimeSpan.FromMilliseconds(duration)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            }
        );
        ShellNavigationPanel.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(
                panelOpacity,
                new Duration(TimeSpan.FromMilliseconds(panelDuration))
            )
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            }
        );
        ShellNavigationRail.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(railOpacity, new Duration(TimeSpan.FromMilliseconds(panelDuration)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            }
        );
        ShellNavigationRailTrack.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(
                railTrackOpacity,
                new Duration(TimeSpan.FromMilliseconds(panelDuration))
            )
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            }
        );
        if (ShellNavigationPanel.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(panelOffset, new Duration(TimeSpan.FromMilliseconds(duration)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                }
            );
        }
    }

    private void ApplyNavigationDockLabelState(bool expanded, int durationMilliseconds)
    {
        foreach (var button in ShellNavigationItemsHost.Children.OfType<WpfButton>())
        {
            button.HorizontalContentAlignment = expanded
                ? System.Windows.HorizontalAlignment.Left
                : System.Windows.HorizontalAlignment.Center;
        }

        foreach (var label in FindVisualChildren<TextBlock>(ShellNavigationItemsHost))
        {
            label.Margin = expanded ? new Thickness(14, 0, 0, 0) : new Thickness(0);
            if (!expanded || durationMilliseconds <= 0)
            {
                label.BeginAnimation(FrameworkElement.WidthProperty, null);
                label.BeginAnimation(UIElement.OpacityProperty, null);
                label.Width = expanded ? NavigationDockLabelExpandedWidth : 0;
                label.Opacity = expanded ? 1 : 0;
                continue;
            }

            label.BeginAnimation(
                FrameworkElement.WidthProperty,
                new DoubleAnimation(
                    expanded ? NavigationDockLabelExpandedWidth : 0,
                    new Duration(TimeSpan.FromMilliseconds(durationMilliseconds))
                )
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                }
            );
            label.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(
                    expanded ? 1 : 0,
                    new Duration(TimeSpan.FromMilliseconds(durationMilliseconds))
                )
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                }
            );
        }
    }

    private void UpdateNavigationActiveState()
    {
        foreach (
            var button in new[]
            {
                ShellNavDashboardButton,
                ShellNavConfigButton,
                ShellNavAccountButton,
                ShellNavAuthFilesButton,
                ShellNavQuotaButton,
                ShellNavUsageButton,
                ShellNavLogsButton,
            }
        )
        {
            if (button.CommandParameter is not string path)
            {
                continue;
            }

            button.Tag = IsRouteActive(path, _activeWebUiPath) ? "Active" : null;
        }
    }

    private static bool IsRouteActive(string route, string pathname)
    {
        var normalizedRoute = NormalizeRoutePath(route);
        var normalizedPath = NormalizeRoutePath(pathname);
        if (normalizedRoute == "/")
        {
            return normalizedPath == "/";
        }

        return normalizedPath.Equals(normalizedRoute, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoute + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRoutePath(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        if (normalized.Length > 1 && normalized.EndsWith('/'))
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized.Equals("/dashboard", StringComparison.OrdinalIgnoreCase)
            ? "/"
            : normalized;
    }

    private async Task ShowSettingsOverlayAsync()
    {
        if (_settingsOverlayOpen)
        {
            PositionSettingsWindow();
            _settingsWindow?.Activate();
            UpdateSettingsOverlayBaseline();
            return;
        }

        try
        {
            _settingsOverlayOpen = true;
            SetNavigationDockPopupOpen(false);
            UpdateSettingsOverlayBaseline();
            EnsureSettingsWindow();
            PositionSettingsWindow();
            SettingsOverlay.Visibility = Visibility.Collapsed;
            if (_settingsWindowRoot is not null)
            {
                _settingsWindowRoot.Opacity = 0;
            }

            if (SettingsDialogCard.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = 0.96;
                scale.ScaleY = 0.96;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateEaseAnimation(1, 180));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateEaseAnimation(1, 180));
            }

            _settingsWindow?.Show();
            _settingsWindow?.Activate();
            _settingsWindowRoot?.BeginAnimation(
                UIElement.OpacityProperty,
                CreateEaseAnimation(1, 180)
            );
            await RefreshSettingsOverlayAsync();
        }
        catch (Exception exception)
        {
            _settingsOverlayOpen = false;
            CloseSettingsWindow();
            UpdateNavigationDockPopupVisibility();
            _notificationService.ShowManual("设置打开失败", exception.Message);
        }
    }

    private async Task HideSettingsOverlayAsync()
    {
        if (!_settingsOverlayOpen && SettingsOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        _settingsOverlayOpen = false;
        CancelSettingsOverviewRefresh();
        _settingsWindowRoot?.BeginAnimation(UIElement.OpacityProperty, CreateEaseAnimation(0, 140));
        if (SettingsDialogCard.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateEaseAnimation(0.96, 140));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateEaseAnimation(0.96, 140));
        }

        await Task.Delay(TimeSpan.FromMilliseconds(150));
        if (!_settingsOverlayOpen)
        {
            CloseSettingsWindow();
            UpdateNavigationDockPopupVisibility();
        }
    }

    private void EnsureSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            return;
        }

        if (SettingsDialogCard.Parent is System.Windows.Controls.Panel currentParent)
        {
            currentParent.Children.Remove(SettingsDialogCard);
        }

        _settingsWindowRoot = new Grid
        {
            Background = new SolidColorBrush(WpfColor.FromArgb(0x55, 0, 0, 0)),
            Opacity = 0,
        };
        _settingsWindowRoot.MouseLeftButtonDown += SettingsWindowRoot_MouseLeftButtonDown;
        _settingsWindowRoot.Children.Add(SettingsDialogCard);

        _settingsWindow = new Window
        {
            Owner = this,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Content = _settingsWindowRoot,
            Topmost = false,
        };
        _settingsWindow.Closed += SettingsWindow_Closed;
    }

    private async void SettingsWindowRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, _settingsWindowRoot))
        {
            await HideSettingsOverlayAsync();
        }
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        if (_settingsWindowRoot is not null)
        {
            _settingsWindowRoot.MouseLeftButtonDown -= SettingsWindowRoot_MouseLeftButtonDown;
            if (SettingsDialogCard.Parent == _settingsWindowRoot)
            {
                _settingsWindowRoot.Children.Remove(SettingsDialogCard);
            }
        }

        if (!SettingsOverlay.Children.Contains(SettingsDialogCard))
        {
            SettingsOverlay.Children.Add(SettingsDialogCard);
        }

        _settingsWindow = null;
        _settingsWindowRoot = null;
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void PositionSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        _settingsWindow.Left = Left;
        _settingsWindow.Top = Top;
        _settingsWindow.Width = Math.Max(ActualWidth, MinWidth);
        _settingsWindow.Height = Math.Max(ActualHeight, MinHeight);
    }

    private void CloseSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        _settingsWindow.Closed -= SettingsWindow_Closed;
        var window = _settingsWindow;
        _settingsWindow = null;
        window.Content = null;

        if (_settingsWindowRoot is not null)
        {
            _settingsWindowRoot.MouseLeftButtonDown -= SettingsWindowRoot_MouseLeftButtonDown;
            if (SettingsDialogCard.Parent == _settingsWindowRoot)
            {
                _settingsWindowRoot.Children.Remove(SettingsDialogCard);
            }
        }

        if (!SettingsOverlay.Children.Contains(SettingsDialogCard))
        {
            SettingsOverlay.Children.Add(SettingsDialogCard);
        }

        _settingsWindowRoot = null;
        window.Close();
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private async Task RefreshShellDockOverviewAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var overview = await _managementOverviewService.GetOverviewAsync(cts.Token);
            _shellBackendVersion = ResolveBackendVersion(
                overview.Value.ServerVersion,
                overview.Metadata.Version
            );
        }
        catch
        {
            _shellBackendVersion = string.IsNullOrWhiteSpace(_shellBackendVersion)
                ? BackendReleaseMetadata.Version
                : _shellBackendVersion;
        }
        finally
        {
            UpdateShellDockPresentation();
        }
    }

    private async Task RefreshSettingsOverlayAsync()
    {
        CancelSettingsOverviewRefresh();
        var requestId = ++_settingsOverviewRefreshRequestId;
        _settingsOverviewRefreshCts = new CancellationTokenSource();
        var cancellationToken = _settingsOverviewRefreshCts.Token;

        UpdateSettingsOverlayBaseline();
        SettingsModelOverviewText.Text = "可用模型：正在加载...";
        SettingsProviderOverviewText.Text = "提供商概览：正在加载...";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    SettingsModelOverviewText.Text = "可用模型：正在重试加载...";
                    SettingsProviderOverviewText.Text = "提供商概览：正在刷新...";
                    await Task.Delay(TimeSpan.FromMilliseconds(420 * attempt), cancellationToken);
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
                var overview = await _managementOverviewService.GetOverviewAsync(timeoutCts.Token);
                if (!IsCurrentSettingsOverviewRequest(requestId, cancellationToken))
                {
                    return;
                }

                _settingsOverview = overview.Value;
                var backendVersion = string.IsNullOrWhiteSpace(overview.Value.ServerVersion)
                    ? overview.Metadata.Version
                    : overview.Value.ServerVersion;
                _shellBackendVersion = ResolveBackendVersion(backendVersion, null);
                UpdateShellDockPresentation();
                SettingsModelOverviewText.Text =
                    overview.Value.AvailableModelCount is { } count ? $"可用模型：{count} 个"
                    : string.IsNullOrWhiteSpace(overview.Value.AvailableModelsError)
                        ? "可用模型：正在等待后端模型数据..."
                    : $"可用模型：{overview.Value.AvailableModelsError}";
                SettingsProviderOverviewText.Text =
                    $"代理密钥 {overview.Value.ApiKeyCount} / 认证文件 {overview.Value.AuthFileCount} / Gemini {overview.Value.GeminiKeyCount} / Codex {overview.Value.CodexKeyCount} / Claude {overview.Value.ClaudeKeyCount} / Vertex {overview.Value.VertexKeyCount} / OpenAI 兼容 {overview.Value.OpenAiCompatibilityCount}";

                var needsRetry =
                    overview.Value.AvailableModelCount is null
                    && string.IsNullOrWhiteSpace(overview.Value.AvailableModelsError)
                    && attempt < 2;
                if (!needsRetry)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (attempt < 2)
            {
                if (!IsCurrentSettingsOverviewRequest(requestId, cancellationToken))
                {
                    return;
                }

                SettingsModelOverviewText.Text =
                    $"可用模型：加载失败，正在重试，{exception.Message}";
                SettingsProviderOverviewText.Text = "提供商概览：正在重试";
            }
            catch (Exception exception)
            {
                if (!IsCurrentSettingsOverviewRequest(requestId, cancellationToken))
                {
                    return;
                }

                _settingsOverview = null;
                _shellBackendVersion = string.IsNullOrWhiteSpace(_shellBackendVersion)
                    ? BackendReleaseMetadata.Version
                    : _shellBackendVersion;
                UpdateShellDockPresentation();
                SettingsModelOverviewText.Text = $"可用模型：加载失败，{exception.Message}";
                SettingsProviderOverviewText.Text = "提供商概览：未加载";
                return;
            }
        }
    }

    private void CancelSettingsOverviewRefresh()
    {
        _settingsOverviewRefreshCts?.Cancel();
        _settingsOverviewRefreshCts?.Dispose();
        _settingsOverviewRefreshCts = null;
    }

    private bool IsCurrentSettingsOverviewRequest(
        int requestId,
        CancellationToken cancellationToken
    )
    {
        return _settingsOverlayOpen
            && !cancellationToken.IsCancellationRequested
            && requestId == _settingsOverviewRefreshRequestId;
    }

    private void UpdateSettingsOverlayBaseline()
    {
        UpdateShellThemePresentation();
    }

    private string ShellConnectionStatusLabel =>
        _shellConnectionStatus switch
        {
            "connected" => "已连接",
            "connecting" => "连接中",
            "error" => "异常",
            _ => "未连接",
        };

    private void ShellNotificationService_NotificationRequested(
        object? sender,
        ShellNotificationRequest request
    )
    {
        Dispatcher.Invoke(() =>
        {
            if (request.Placement == ShellNotificationPlacement.BottomCenterAuto)
            {
                ShowAutoNotification(request.Message);
                return;
            }

            ShowManualNotification(request.Title, request.Message);
        });
    }

    private void ShowAutoNotification(string message)
    {
        var card = CreateNotificationCard(message, title: null, showCloseButton: false);
        card.Opacity = 0;
        card.RenderTransform = new TranslateTransform(0, 18);
        AutoNotificationStack.Children.Add(card);

        var progress = (Border)card.Tag;
        card.Loaded += async (_, _) =>
        {
            AnimateNotificationIn(card);
            progress.Width = card.ActualWidth;
            progress.BeginAnimation(
                FrameworkElement.WidthProperty,
                new DoubleAnimation(0, new Duration(TimeSpan.FromSeconds(2)))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                }
            );
            await Task.Delay(TimeSpan.FromSeconds(2.15));
            await FadeOutAndRemoveAsync(AutoNotificationStack, card);
        };
    }

    private void ShowManualNotification(string title, string message)
    {
        var card = CreateNotificationCard(message, title, showCloseButton: true);
        card.Opacity = 0;
        card.RenderTransform = new TranslateTransform(18, 0);
        ManualNotificationStack.Children.Add(card);
        card.Loaded += (_, _) => AnimateNotificationIn(card);
    }

    private Border CreateNotificationCard(string message, string? title, bool showCloseButton)
    {
        var progress = new Border
        {
            Height = 3,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Background = new SolidColorBrush(WpfColor.FromRgb(37, 99, 235)),
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(
            progress,
            showCloseButton ? "ManualNotificationProgress" : "AutoNotificationProgress"
        );

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var contentGrid = new Grid { Margin = new Thickness(14, 12, 14, 12) };
        if (showCloseButton)
        {
            contentGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        var textStack = new StackPanel();
        if (!string.IsNullOrWhiteSpace(title))
        {
            textStack.Children.Add(
                new TextBlock
                {
                    Text = title,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (WpfBrush)FindResource("PrimaryTextBrush"),
                }
            );
        }

        var messageText = new TextBlock
        {
            Text = message,
            Margin = string.IsNullOrWhiteSpace(title)
                ? new Thickness(0)
                : new Thickness(0, 6, 0, 0),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (WpfBrush)FindResource("SecondaryTextBrush"),
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(
            messageText,
            showCloseButton ? "ManualNotificationMessage" : "AutoNotificationMessage"
        );
        System.Windows.Automation.AutomationProperties.SetName(messageText, message);
        textStack.Children.Add(messageText);
        contentGrid.Children.Add(textStack);

        if (showCloseButton)
        {
            var closeButton = new WpfButton
            {
                Content = "\uE711",
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Margin = new Thickness(10, -4, -6, 0),
                FontFamily = new WpfFontFamily("Segoe MDL2 Assets"),
                ToolTip = "关闭",
            };
            Grid.SetColumn(closeButton, 1);
            contentGrid.Children.Add(closeButton);
        }

        root.Children.Add(contentGrid);
        Grid.SetRow(progress, 1);
        root.Children.Add(progress);

        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Background = (WpfBrush)FindResource("SurfaceBrush"),
            BorderBrush = (WpfBrush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Effect = new DropShadowEffect
            {
                BlurRadius = 16,
                Direction = 270,
                Color = WpfColor.FromArgb(0x10, 0, 0, 0),
                Opacity = 1,
                ShadowDepth = 2,
            },
            Child = root,
            Tag = progress,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(
            card,
            showCloseButton ? "ManualNotificationCard" : "AutoNotificationCard"
        );
        System.Windows.Automation.AutomationProperties.SetName(
            card,
            string.IsNullOrWhiteSpace(title) ? message : $"{title} {message}"
        );

        if (
            showCloseButton
            && contentGrid.Children.OfType<WpfButton>().FirstOrDefault() is { } button
        )
        {
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                button,
                "ManualNotificationCloseButton"
            );
            System.Windows.Automation.AutomationProperties.SetName(button, "关闭");
            button.Click += async (_, _) =>
                await FadeOutAndRemoveAsync(ManualNotificationStack, card);
        }

        return card;
    }

    private static void AnimateNotificationIn(Border card)
    {
        card.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(180)))
        );
        if (card.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(180)))
            );
            transform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(180)))
            );
        }
    }

    private static async Task FadeOutAndRemoveAsync(
        System.Windows.Controls.Panel owner,
        Border card
    )
    {
        if (!owner.Children.Contains(card))
        {
            return;
        }

        card.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(180)))
        );
        await Task.Delay(TimeSpan.FromMilliseconds(190));
        owner.Children.Remove(card);
    }

    private void InitializeTrayIcon()
    {
        if (!_settings.EnableTrayIcon)
        {
            TrayIcon.Unregister();
            return;
        }

        TrayIcon.Register();
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private bool IsUpgradeNoticePending()
    {
        return !string.Equals(
            _settings.LastSeenApplicationVersion?.Trim(),
            CurrentApplicationVersion,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private string CurrentApplicationVersion =>
        string.IsNullOrWhiteSpace(_buildInfo.ApplicationVersion)
            ? "当前版本"
            : _buildInfo.ApplicationVersion.Trim();

    private static string FormatUnknown(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "未知" : value.Trim();
    }

    private static string ResolveBackendVersion(string? preferredVersion, string? fallbackVersion)
    {
        if (!string.IsNullOrWhiteSpace(preferredVersion))
        {
            return preferredVersion.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallbackVersion))
        {
            return fallbackVersion.Trim();
        }

        return BackendReleaseMetadata.Version;
    }

    private static string NormalizeConnectionStatus(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "connected" => "connected",
            "connecting" => "connecting",
            "error" => "error",
            _ => "disconnected",
        };
    }

    private static string NormalizeWebTheme(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "dark" => "dark",
            "white" or "light" => "white",
            _ => "auto",
        };
    }

    private static string NormalizeResolvedTheme(string? value)
    {
        return value?.Trim().ToLowerInvariant() == "dark" ? "dark" : "light";
    }

    private static string ToWebTheme(AppThemeMode themeMode)
    {
        return themeMode switch
        {
            AppThemeMode.White => "white",
            AppThemeMode.Dark => "dark",
            _ => "auto",
        };
    }

    private static string ToWebResolvedTheme(AppThemeMode themeMode)
    {
        return IsEffectiveDarkTheme(themeMode) ? "dark" : "light";
    }

    private static bool IsEffectiveDarkTheme(AppThemeMode themeMode)
    {
        return themeMode == AppThemeMode.Dark
            || (themeMode == AppThemeMode.System && IsSystemDarkTheme());
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
            );
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return false;
        }
    }

    private static DoubleAnimation CreateEaseAnimation(double to, int milliseconds)
    {
        return new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(milliseconds)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void SetThemeButtonState(WpfButton button, bool active)
    {
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        button.Opacity = active ? 1 : 0.72;
    }

    private void SetBrushResource(string key, string color)
    {
        if (WpfColorConverter.ConvertFromString(color) is WpfColor parsed)
        {
            Resources[key] = new SolidColorBrush(parsed);
            System.Windows.Application.Current.Resources[key] = new SolidColorBrush(parsed);
        }
    }

    private static string GenerateSecurityKey()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    }

    private static string BuildSecurityKeyFileContent(string securityKey)
    {
        return "CodexCliPlus 安全密钥"
            + Environment.NewLine
            + Environment.NewLine
            + securityKey
            + Environment.NewLine
            + Environment.NewLine
            + "请妥善保存。完整安全密钥只会在首次初始化页面显示一次。"
            + Environment.NewLine
            + "不要把此文件发送给不受信任的人。"
            + Environment.NewLine;
    }

    private static string BuildDesktopSecurityKeyFilePath()
    {
        var desktopDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory
        );
        if (string.IsNullOrWhiteSpace(desktopDirectory))
        {
            throw new InvalidOperationException("无法定位桌面目录。");
        }

        string normalizedDesktopDirectory;
        try
        {
            normalizedDesktopDirectory = Path.GetFullPath(desktopDirectory);
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException("桌面目录路径无效。", exception);
        }

        if (!Directory.Exists(normalizedDesktopDirectory))
        {
            throw new InvalidOperationException($"桌面目录不可用：{normalizedDesktopDirectory}");
        }

        var fileName = $"CodexCliPlus-安全密钥-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
        return Path.Combine(normalizedDesktopDirectory, fileName);
    }

    private static string BuildDesktopSaveErrorMessage(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => $"无法写入系统桌面目录。{exception.Message}",
            IOException => $"无法写入系统桌面目录。{exception.Message}",
            _ => exception.Message,
        };
    }

    private async Task EnsureMinimumPreparationDisplayAsync()
    {
        if (_preparationPanelShownAt is not { } shownAt)
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - shownAt;
        var remaining = MinimumPreparationDisplayDuration - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }
    }

    private static string BuildPreparationStatus(double progress, StartupState state)
    {
        if (state == StartupState.LoadingManagement || progress >= 90)
        {
            return "正在接入管理界面";
        }

        if (progress >= 65)
        {
            return "正在启动本地后端";
        }

        if (progress >= 35)
        {
            return "正在校验运行资源";
        }

        return "正在启动桌面宿主";
    }

    private async Task ResetWebUiLocalAuthStateAsync()
    {
        if (!_webViewConfigured || ManagementWebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            ManagementWebView.CoreWebView2.CookieManager.DeleteAllCookies();
            await ManagementWebView.CoreWebView2.ExecuteScriptAsync(
                """
                (() => {
                  try {
                    [
                      'codexcliplus-auth',
                      'cli-proxy-auth',
                      'isLoggedIn',
                      'apiBase',
                      'apiUrl',
                      'managementKey'
                    ].forEach((key) => localStorage.removeItem(key));
                    sessionStorage.clear();
                  } catch {
                  }
                })();
                """
            );
        }
        catch { }
    }

    private void DeleteFileIfExistsInsideRoot(string filePath)
    {
        EnsurePathInsideAppRoot(filePath);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private void DeleteDirectoryIfExistsInsideRoot(string directoryPath)
    {
        EnsurePathInsideAppRoot(directoryPath);
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private void EnsurePathInsideAppRoot(string path)
    {
        if (!IsPathInsideAppRoot(path))
        {
            throw new InvalidOperationException("拒绝清理应用数据目录之外的路径。");
        }
    }

    private bool IsPathInsideAppRoot(string path)
    {
        var root = Path.GetFullPath(_pathService.Directories.RootDirectory);
        var normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(path);

        return target.Equals(root, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private string? TryReadConfiguredAuthDirectory()
    {
        try
        {
            if (!File.Exists(_pathService.Directories.BackendConfigFilePath))
            {
                return null;
            }

            var yaml = File.ReadAllText(_pathService.Directories.BackendConfigFilePath);
            var match = Regex.Match(yaml, "(?m)^auth-dir:\\s*\"(?<path>(?:\\\\.|[^\"])*)\"");
            if (!match.Success)
            {
                return null;
            }

            var authDirectory = match
                .Groups["path"]
                .Value.Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal)
                .Trim();

            return string.IsNullOrWhiteSpace(authDirectory) ? null : authDirectory;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureWebView2Runtime()
    {
        var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new WebView2RuntimeNotFoundException();
        }
    }

    private static void OpenExternal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (
            !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }
}
