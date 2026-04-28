using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

using CodexCliPlus.Core.Abstractions.Build;
using CodexCliPlus.Core.Abstractions.Configuration;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Abstractions.Updates;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Services;
using CodexCliPlus.ViewModels;

using Microsoft.Web.WebView2.Core;

using MessageBox = System.Windows.MessageBox;

namespace CodexCliPlus;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private const string AppHostName = "codexcliplus-webui.local";
    private static readonly Uri AppEntryUri = new($"http://{AppHostName}/index.html");

    private readonly MainWindowViewModel _viewModel;
    private readonly BackendProcessManager _backendProcessManager;
    private readonly IManagementSessionService _sessionService;
    private readonly IAppConfigurationService _appConfigurationService;
    private readonly IBuildInfo _buildInfo;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly WebUiAssetLocator _webUiAssetLocator;

    private AppSettings _settings = new();
    private string? _bootstrapScriptId;
    private bool _allowClose;
    private bool _isInitializing;
    private bool _webViewConfigured;

    public MainWindow(
        MainWindowViewModel viewModel,
        BackendProcessManager backendProcessManager,
        IManagementSessionService sessionService,
        IAppConfigurationService appConfigurationService,
        IBuildInfo buildInfo,
        IUpdateCheckService updateCheckService,
        WebUiAssetLocator webUiAssetLocator)
    {
        _viewModel = viewModel;
        _backendProcessManager = backendProcessManager;
        _sessionService = sessionService;
        _appConfigurationService = appConfigurationService;
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
            _settings = await _appConfigurationService.LoadAsync();
            InitializeTrayIcon();
            RememberManagementKeyCheckBox.IsChecked = _settings.RememberManagementKey;

            if (_settings.RememberManagementKey && !string.IsNullOrWhiteSpace(_settings.ManagementKey))
            {
                await InitializeHostAsync(restartBackend: false);
                return;
            }

            ShowLogin();
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
        TrayIcon.Unregister();
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
        await InitializeHostAsync(restartBackend: _backendProcessManager.CurrentStatus.State != BackendStateKind.Running);
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

    private async Task SignInAsync()
    {
        var managementKey = ManagementKeyPasswordBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(managementKey))
        {
            LoginErrorText.Text = "请输入管理密钥。";
            LoginErrorText.Visibility = Visibility.Visible;
            ManagementKeyPasswordBox.Focus();
            return;
        }

        LoginButton.IsEnabled = false;
        LoginErrorText.Visibility = Visibility.Collapsed;

        try
        {
            _settings.ManagementKey = managementKey;
            _settings.RememberManagementKey = RememberManagementKeyCheckBox.IsChecked == true;
            await _appConfigurationService.SaveAsync(_settings);
            await InitializeHostAsync(restartBackend: false);
        }
        catch (Exception exception)
        {
            LoginErrorText.Text = exception.Message;
            LoginErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (LoginPanel.Visibility == Visibility.Visible)
            {
                LoginButton.IsEnabled = true;
            }
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
        ShowLoading("正在准备本地后端和桌面桥接。");

        try
        {
            if (restartBackend && _backendProcessManager.CurrentStatus.State == BackendStateKind.Running)
            {
                await _backendProcessManager.RestartAsync();
            }

            EnsureWebView2Runtime();
            var bundle = _webUiAssetLocator.GetRequiredBundle();
            var connection = await _sessionService.GetConnectionAsync();
            var payload = new DesktopBootstrapPayload
            {
                DesktopMode = true,
                ApiBase = connection.BaseUrl,
                ManagementKey = connection.ManagementKey
            };

            await EnsureWebViewAsync(bundle, payload);
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
                        ? "登录状态已失效，请重新输入管理密钥。"
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

    private void ShowLoading(string description)
    {
        LoadingDescriptionText.Text = description;
        LoginPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
    }

    private void ShowLogin(string? errorMessage = null)
    {
        LoginPanel.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        LoginButton.IsEnabled = true;
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            LoginErrorText.Visibility = Visibility.Collapsed;
        }
        else
        {
            LoginErrorText.Text = errorMessage;
            LoginErrorText.Visibility = Visibility.Visible;
        }

        ManagementKeyPasswordBox.Focus();
    }

    private void ShowBlocker(string title, string description, string detail)
    {
        BlockerTitleText.Text = title;
        BlockerDescriptionText.Text = description;
        BlockerDetailText.Text = detail;
        LoginPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
    }

    private void ShowWebView()
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Visible;
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
