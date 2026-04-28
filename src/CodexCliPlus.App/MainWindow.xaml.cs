using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

using CodexCliPlus.Core.Abstractions.Build;
using CodexCliPlus.Core.Abstractions.Configuration;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Abstractions.Updates;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Services;
using CodexCliPlus.ViewModels;

using Microsoft.Web.WebView2.Core;

using MessageBox = System.Windows.MessageBox;

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
        Blocked
    }

    private const string AppHostName = "codexcliplus-webui.local";
    private const int FirstRunConfirmationSeconds = 5;
    private static readonly TimeSpan MinimumPreparationDisplayDuration = TimeSpan.FromMilliseconds(2500);
    private static readonly Uri AppEntryUri = new($"http://{AppHostName}/index.html");

    private readonly MainWindowViewModel _viewModel;
    private readonly BackendProcessManager _backendProcessManager;
    private readonly BackendConfigWriter _backendConfigWriter;
    private readonly IManagementSessionService _sessionService;
    private readonly IAppConfigurationService _appConfigurationService;
    private readonly IPathService _pathService;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly IBuildInfo _buildInfo;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly WebUiAssetLocator _webUiAssetLocator;

    private AppSettings _settings = new();
    private StartupState _startupState = StartupState.Preparing;
    private string? _bootstrapScriptId;
    private string _firstRunManagementKey = string.Empty;
    private CancellationTokenSource? _firstRunConfirmCountdown;
    private DateTimeOffset? _preparationPanelShownAt;
    private bool _allowClose;
    private bool _isInitializing;
    private bool _webViewConfigured;

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
        WebUiAssetLocator webUiAssetLocator)
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
        _webUiAssetLocator = webUiAssetLocator;

        DataContext = _viewModel;
        InitializeComponent();

        _backendProcessManager.StatusChanged += BackendProcessManager_StatusChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ShowPreparationStep("目录", 5, "正在加载本地配置。", StartupState.Preparing);
            var settingsFileExists = File.Exists(_pathService.Directories.SettingsFilePath);
            _settings = await _appConfigurationService.LoadAsync();
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
                exception.Message);
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _backendProcessManager.StatusChanged -= BackendProcessManager_StatusChanged;
        _firstRunConfirmCountdown?.Cancel();
        _firstRunConfirmCountdown?.Dispose();
        TrayIcon.Unregister();
    }

    public void Dispose()
    {
        _backendProcessManager.StatusChanged -= BackendProcessManager_StatusChanged;
        _firstRunConfirmCountdown?.Cancel();
        _firstRunConfirmCountdown?.Dispose();
        GC.SuppressFinalize(this);
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

        await InitializeHostAsync(restartBackend: _backendProcessManager.CurrentStatus.State != BackendStateKind.Running);
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
            ShowBlocker(
                "更新确认失败",
                "无法保存本次更新确认状态。",
                exception.Message);
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

    private async void ManagementKeyPasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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
            ShowFirstRunStatus("安全密钥已复制。");
        }
        catch (Exception exception)
        {
            ShowFirstRunStatus($"复制失败：{exception.Message}", isError: true);
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
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            ShowFirstRunStatus($"已保存到桌面：{Path.GetFileName(filePath)}");
        }
        catch (Exception exception)
        {
            ShowFirstRunStatus($"保存失败：{BuildDesktopSaveErrorMessage(exception)}", isError: true);
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
            MessageBoxImage.Warning);
        if (firstConfirm != MessageBoxResult.Yes)
        {
            return;
        }

        var secondConfirm = MessageBox.Show(
            this,
            "确认重置后，现有账号与配置无法通过桌面壳恢复。是否继续？",
            "确认重置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
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

        if (string.IsNullOrWhiteSpace(_settings.ManagementKey) && _backendProcessManager.CurrentStatus.State != BackendStateKind.Running)
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
            MessageBox.Show(this, exception.Message, "检查更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
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
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
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
                ValidatePort = false
            });

        FirstRunSecurityKeyTextBox.Text = _firstRunManagementKey;
        FirstRunSecurityKeyTextBox.CaretIndex = 0;
        FirstRunSecurityKeyTextBox.ScrollToHome();
        FirstRunRememberSecurityKeyCheckBox.IsChecked = false;
        FirstRunActionStatusText.Visibility = Visibility.Collapsed;
        FirstRunConfirmPanel.Visibility = Visibility.Collapsed;
        FirstRunEnterManagementButton.IsEnabled = true;
        await EnsureMinimumPreparationDisplayAsync();
        ShowFirstRunKeyReveal();
    }

    private async Task BeginFirstRunConfirmationAsync()
    {
        if (string.IsNullOrWhiteSpace(_firstRunManagementKey))
        {
            ShowFirstRunStatus("安全密钥尚未生成，请重试。", isError: true);
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
        catch (TaskCanceledException)
        {
        }
    }

    private async Task CompleteFirstRunAsync()
    {
        if (string.IsNullOrWhiteSpace(_firstRunManagementKey))
        {
            ShowFirstRunStatus("安全密钥尚未生成，请重试。", isError: true);
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
            await InitializeHostAsync(restartBackend: false);

            _firstRunManagementKey = string.Empty;
            FirstRunSecurityKeyTextBox.Text = string.Empty;
        }
        catch (Exception exception)
        {
            FirstRunConfirmContinueButton.IsEnabled = true;
            FirstRunConfirmCloseButton.IsEnabled = true;
            ShowFirstRunStatus($"初始化失败：{exception.Message}", isError: true);
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
            DeleteDirectoryIfExistsInsideRoot(Path.Combine(_pathService.Directories.ConfigDirectory, AppConstants.SecretsDirectoryName));
            DeleteDirectoryIfExistsInsideRoot(Path.Combine(_pathService.Directories.BackendDirectory, "auth"));
            if (!string.IsNullOrWhiteSpace(configuredAuthDirectory) && IsPathInsideAppRoot(configuredAuthDirectory))
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
            ShowBlocker(
                "安全密钥重置失败",
                "未能完成本地配置和认证状态清理。",
                exception.Message);
        }
    }

    private async void BackendProcessManager_StatusChanged(object? sender, BackendStatusSnapshot snapshot)
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
                snapshot.LastError ?? snapshot.Message);
        });
    }

    private async Task InitializeHostAsync(bool restartBackend)
    {
        if (_isInitializing)
        {
            return;
        }

        _isInitializing = true;
        ShowPreparationStep("目录", 10, "正在准备本地目录。", StartupState.Preparing);

        try
        {
            await _pathService.EnsureCreatedAsync();

            ShowPreparationStep("WebView2", 25, "正在检查 WebView2 运行时。", StartupState.Preparing);
            EnsureWebView2Runtime();

            ShowPreparationStep("后端资产", 40, "正在定位管理界面和后端资产。", StartupState.Preparing);
            var bundle = _webUiAssetLocator.GetRequiredBundle();

            ShowPreparationStep("配置", 55, "正在确认后端配置。", StartupState.Preparing);
            if (restartBackend && _backendProcessManager.CurrentStatus.State == BackendStateKind.Running)
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
                ManagementKey = connection.ManagementKey
            };

            ShowPreparationStep("管理桥接", 95, "正在打开管理界面。", StartupState.LoadingManagement);
            await EnsureWebViewAsync(bundle, payload);
            await EnsureMinimumPreparationDisplayAsync();
            ShowWebView();
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            ShowBlocker(
                "缺少 WebView2 运行时",
                "当前系统未安装 Microsoft Edge WebView2 Runtime，桌面宿主无法承载管理界面。",
                exception.Message);
        }
        catch (FileNotFoundException exception)
        {
            ShowBlocker(
                "缺少管理界面资源",
                "前端静态资源未找到，桌面宿主无法继续启动。",
                exception.Message);
        }
        catch (Exception exception)
        {
            ShowBlocker(
                "桌面宿主启动失败",
                "管理界面宿主未能完成初始化，请先修复阻断错误。",
                exception.Message);
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
            await ManagementWebView.EnsureCoreWebView2Async();
            ConfigureWebView(bundle);
            _webViewConfigured = true;
        }

        await UpdateBootstrapScriptAsync(payload);
        ManagementWebView.CoreWebView2.Navigate(AppEntryUri.ToString());
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
            CoreWebView2HostResourceAccessKind.Allow);

        core.NavigationStarting += CoreWebView2_NavigationStarting;
        core.NewWindowRequested += CoreWebView2_NewWindowRequested;
        core.WebMessageReceived += CoreWebView2_WebMessageReceived;
        core.ProcessFailed += CoreWebView2_ProcessFailed;
    }

    private async Task UpdateBootstrapScriptAsync(DesktopBootstrapPayload payload)
    {
        if (_bootstrapScriptId is not null)
        {
            ManagementWebView.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_bootstrapScriptId);
        }

        _bootstrapScriptId = await ManagementWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            DesktopBridgeScriptFactory.CreateInitializationScript(payload));
    }

    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Uri) || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (uri.Host.Equals(AppHostName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        OpenExternal(uri.ToString());
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(e.Uri))
        {
            OpenExternal(e.Uri);
        }
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
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
                    var url = root.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        OpenExternal(url);
                    }

                    break;

                case "requestNativeLogin":
                    var message = root.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString()
                        : null;
                    ShowLogin(string.IsNullOrWhiteSpace(message)
                        ? "登录状态已失效，请重新输入安全密钥。"
                        : message);
                    break;
            }
        }
        catch
        {
        }
    }

    private void CoreWebView2_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ShowBlocker(
                "WebView2 进程异常退出",
                "管理界面渲染进程发生故障，桌面宿主已停止继续渲染空白页面。",
                e.ProcessFailedKind.ToString());
        });
    }

    private void ShowPreparationStep(string step, double progress, string description, StartupState state)
    {
        _startupState = state;
        if (LoadingPanel.Visibility != Visibility.Visible || _preparationPanelShownAt is null)
        {
            _preparationPanelShownAt = DateTimeOffset.UtcNow;
        }

        LoadingTitleText.Text = state == StartupState.LoadingManagement
            ? "正在进入管理界面"
            : "正在准备桌面管理界面";
        LoadingDescriptionText.Text = description;
        LoadingStatusText.Text = BuildPreparationStatus(progress, state);
        PreparationStepText.Text = $"当前步骤：{step}";
        var normalizedProgress = Math.Clamp(progress, 0, 100);
        PreparationProgressBar.BeginAnimation(
            RangeBase.ValueProperty,
            new DoubleAnimation(normalizedProgress, new Duration(TimeSpan.FromMilliseconds(320)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });

        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
    }

    private void ShowUpgradeNotice()
    {
        _startupState = StartupState.UpgradeNotice;
        _preparationPanelShownAt = null;
        var previousVersion = string.IsNullOrWhiteSpace(_settings.LastSeenApplicationVersion)
            ? "旧版本"
            : _settings.LastSeenApplicationVersion.Trim();
        UpgradeNoticeVersionText.Text = $"已从 {previousVersion} 升级到 {CurrentApplicationVersion}";

        UpgradeNoticePanel.Visibility = Visibility.Visible;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
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
        FirstRunSecurityKeyTextBox.Focus();
    }

    private void ShowLogin(string? errorMessage = null)
    {
        _startupState = StartupState.NativeLogin;
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
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
    }

    private void ShowWebView()
    {
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Visible;
    }

    private void ShowFirstRunStatus(string message, bool isError = false)
    {
        FirstRunActionStatusText.Text = message;
        FirstRunActionStatusText.Visibility = Visibility.Visible;
        if (isError)
        {
            FirstRunActionStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        else
        {
            FirstRunActionStatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "SecondaryTextBrush");
        }
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
            StringComparison.OrdinalIgnoreCase);
    }

    private string CurrentApplicationVersion =>
        string.IsNullOrWhiteSpace(_buildInfo.ApplicationVersion)
            ? "当前版本"
            : _buildInfo.ApplicationVersion.Trim();

    private static string GenerateSecurityKey()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    }

    private static string BuildSecurityKeyFileContent(string securityKey)
    {
        return
            "CodexCliPlus 安全密钥" + Environment.NewLine +
            Environment.NewLine +
            securityKey + Environment.NewLine +
            Environment.NewLine +
            "请妥善保存。完整安全密钥只会在首次初始化页面显示一次。" + Environment.NewLine +
            "不要把此文件发送给不受信任的人。" + Environment.NewLine;
    }

    private static string BuildDesktopSecurityKeyFilePath()
    {
        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopDirectory))
        {
            throw new InvalidOperationException("无法定位桌面目录。");
        }

        string normalizedDesktopDirectory;
        try
        {
            normalizedDesktopDirectory = Path.GetFullPath(desktopDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
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
            _ => exception.Message
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
                """);
        }
        catch
        {
        }
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

        return target.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
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

            var authDirectory = match.Groups["path"].Value
                .Replace("\\\"", "\"", StringComparison.Ordinal)
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

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(uri.ToString())
        {
            UseShellExecute = true
        });
    }
}
