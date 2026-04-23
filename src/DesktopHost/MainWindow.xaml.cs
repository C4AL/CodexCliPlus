using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

using Clipboard = System.Windows.Clipboard;
using Forms = System.Windows.Forms;

using DesktopHost.Core.Abstractions.Build;
using DesktopHost.Core.Abstractions.Configuration;
using DesktopHost.Core.Abstractions.Logging;
using DesktopHost.Core.Abstractions.Paths;
using DesktopHost.Core.Enums;
using DesktopHost.Core.Models;
using DesktopHost.Infrastructure.Backend;
using DesktopHost.Infrastructure.Codex;
using DesktopHost.Infrastructure.Diagnostics;
using DesktopHost.Infrastructure.Platform;
using DesktopHost.ViewModels;
using DesktopHost.Services;

using Microsoft.Web.WebView2.Core;

using MessageBox = System.Windows.MessageBox;

namespace DesktopHost;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IBuildInfo _buildInfo;
    private readonly IDesktopConfigurationService _configurationService;
    private readonly IPathService _pathService;
    private readonly IAppLogger _logger;
    private readonly BackendProcessManager _backendProcessManager;
    private readonly DirectoryAccessService _directoryAccessService;
    private readonly WebView2RuntimeService _webView2RuntimeService;
    private readonly StartupRegistrationService _startupRegistrationService;
    private readonly CodexLocator _codexLocator;
    private readonly CodexVersionReader _codexVersionReader;
    private readonly CodexAuthStateReader _codexAuthStateReader;
    private readonly CodexConfigService _codexConfigService;
    private readonly CodexLaunchService _codexLaunchService;
    private readonly DiagnosticsService _diagnosticsService;
    private readonly DispatcherTimer _logTimer;
    private readonly Forms.NotifyIcon _notifyIcon;

    private DesktopSettings _settings = new();
    private DependencyCheckResult _webViewRuntimeStatus = new();
    private CodexStatusSnapshot _codexStatus = new();
    private bool _allowClose;
    private bool _automationWaitForWebView;
    private bool _automationExitRequested;

    public MainWindow(
        MainWindowViewModel viewModel,
        IBuildInfo buildInfo,
        IDesktopConfigurationService configurationService,
        IPathService pathService,
        IAppLogger logger,
        BackendProcessManager backendProcessManager,
        DirectoryAccessService directoryAccessService,
        WebView2RuntimeService webView2RuntimeService,
        StartupRegistrationService startupRegistrationService,
        CodexLocator codexLocator,
        CodexVersionReader codexVersionReader,
        CodexAuthStateReader codexAuthStateReader,
        CodexConfigService codexConfigService,
        CodexLaunchService codexLaunchService,
        DiagnosticsService diagnosticsService)
    {
        _viewModel = viewModel;
        _buildInfo = buildInfo;
        _configurationService = configurationService;
        _pathService = pathService;
        _logger = logger;
        _backendProcessManager = backendProcessManager;
        _directoryAccessService = directoryAccessService;
        _webView2RuntimeService = webView2RuntimeService;
        _startupRegistrationService = startupRegistrationService;
        _codexLocator = codexLocator;
        _codexVersionReader = codexVersionReader;
        _codexAuthStateReader = codexAuthStateReader;
        _codexConfigService = codexConfigService;
        _codexLaunchService = codexLaunchService;
        _diagnosticsService = diagnosticsService;

        InitializeComponent();
        DataContext = _viewModel;

#if DEBUG
        OpenDevToolsButton.Visibility = Visibility.Visible;
#endif

        _backendProcessManager.StatusChanged += BackendProcessManager_StatusChanged;
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;

        ManagementWebView.NavigationCompleted += ManagementWebView_NavigationCompleted;

        _logTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _logTimer.Tick += (_, _) => _viewModel.BackendLogText = _backendProcessManager.RecentLogText;
        _logTimer.Start();

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "CPAD",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildNotifyIconMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private Forms.ContextMenuStrip BuildNotifyIconMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示窗口", null, (_, _) => RestoreFromTray());
        menu.Items.Add("退出", null, async (_, _) =>
        {
            _allowClose = true;
            await _backendProcessManager.StopAsync();
            Dispatcher.Invoke(Close);
        });
        return menu;
    }

    public void ConfigureAutomation(bool waitForWebView)
    {
        _automationWaitForWebView = waitForWebView;
        if (waitForWebView)
        {
            _logger.Info("Automation verify-hosting mode enabled.");
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                if (!_automationExitRequested)
                {
                    _logger.LogError("verify-hosting 超时：30 秒内未完成 WebView2 托管验证。");
                    await Dispatcher.InvokeAsync(() => CloseForAutomationAsync(exitCode: 1));
                }
            });
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _configurationService.LoadAsync();

        _viewModel.AppDataPath = $"应用数据：{_pathService.Directories.RootDirectory}";
        _viewModel.LogsPath = $"日志目录：{_pathService.Directories.LogsDirectory}";
        _viewModel.VersionText = $"应用版本：{_buildInfo.ApplicationVersion}";
        _viewModel.InformationalVersionText = $"信息版本：{_buildInfo.InformationalVersion}";
        _viewModel.RepositoryPath = string.IsNullOrWhiteSpace(_settings.LastRepositoryPath)
            ? TryDetectRepositoryRoot() ?? string.Empty
            : _settings.LastRepositoryPath;
        _viewModel.StartWithWindows = _startupRegistrationService.IsEnabled();
        _viewModel.CurrentSourceText = CodexConfigService.GetSourceName(_settings.PreferredCodexSource);

        if (!ValidateWritableDirectories())
        {
            UpdateDiagnosticsSummary();
            if (_automationWaitForWebView)
            {
                _ = CloseForAutomationAsync(exitCode: 1);
            }

            return;
        }

        _webViewRuntimeStatus = _webView2RuntimeService.Check();
        _logger.Info($"WebView2 runtime check: {_webViewRuntimeStatus.Summary} / {_webViewRuntimeStatus.Detail}");
        _viewModel.WebViewStatusText = _webViewRuntimeStatus.Summary;
        _viewModel.FooterStatus = _webViewRuntimeStatus.Summary;

        if (_webViewRuntimeStatus.IsAvailable)
        {
            await EnsureWebViewReadyAsync();
        }
        else
        {
            ShowWebViewOverlay(_webViewRuntimeStatus.Summary, _webViewRuntimeStatus.Detail);
            await FailAutomationIfNeededAsync();
        }

        await DetectCodexAsync();
        await EnsureCodexProfilesAsync(_settings.PreferredCodexSource);
        await StartBackendAsync();
        BackendProcessManager_StatusChanged(this, _backendProcessManager.CurrentStatus);
        UpdateDiagnosticsSummary();
    }

    private async void BackendProcessManager_StatusChanged(object? sender, BackendStatusSnapshot snapshot)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            _viewModel.BackendBadgeText = snapshot.State switch
            {
                BackendStateKind.Running => "后端运行中",
                BackendStateKind.Starting => "后端启动中",
                BackendStateKind.Error => "后端异常",
                _ => "后端未启动"
            };
            _viewModel.BackendStatusText = snapshot.Message;
            _viewModel.BackendDetailText = snapshot.Runtime is null
                ? snapshot.LastError ?? "等待后端运行时信息。"
                : string.IsNullOrWhiteSpace(snapshot.Runtime.PortMessage)
                    ? $"管理页：{snapshot.Runtime.ManagementPageUrl}"
                    : $"{snapshot.Runtime.PortMessage}{Environment.NewLine}管理页：{snapshot.Runtime.ManagementPageUrl}";
            _viewModel.FooterStatus = snapshot.Message;

            if (snapshot.State == BackendStateKind.Running && snapshot.Runtime is not null)
            {
                _settings.BackendPort = snapshot.Runtime.Port;
                await EnsureCodexProfilesAsync(_settings.PreferredCodexSource);
                await NavigateToManagementPageAsync(snapshot.Runtime);
            }
            else if (snapshot.State != BackendStateKind.Running)
            {
                ShowWebViewOverlay(snapshot.Message, snapshot.LastError);
                if (_automationWaitForWebView && snapshot.State == BackendStateKind.Error)
                {
                    _ = CloseForAutomationAsync(exitCode: 1);
                }
            }

            UpdateDiagnosticsSummary();
        });
    }

    private async Task EnsureWebViewReadyAsync()
    {
        if (ManagementWebView.CoreWebView2 is not null)
        {
            return;
        }

        _logger.Info("开始初始化 WebView2。");
        var webViewUserDataDirectory = Path.Combine(_pathService.Directories.CacheDirectory, "webview2");
        Directory.CreateDirectory(webViewUserDataDirectory);
        var webViewEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: webViewUserDataDirectory);
        await ManagementWebView.EnsureCoreWebView2Async(webViewEnvironment);
        _logger.Info("WebView2 初始化完成。");
        ManagementWebView.Visibility = Visibility.Visible;
        ManagementWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        ManagementWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        ManagementWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
    }

    private async Task NavigateToManagementPageAsync(BackendRuntimeInfo runtime)
    {
        if (!_webViewRuntimeStatus.IsAvailable)
        {
            ShowWebViewOverlay(_webViewRuntimeStatus.Summary, _webViewRuntimeStatus.Detail);
            return;
        }

        await EnsureWebViewReadyAsync();
        await ManagementWebView.CoreWebView2!.AddScriptToExecuteOnDocumentCreatedAsync(
            BuildBootstrapScript(runtime));

        _logger.Info($"导航到 management.html：{runtime.ManagementPageUrl}");
        ShowWebViewOverlay("正在加载原版 management.html ...", runtime.ManagementPageUrl);
        ManagementWebView.Source = new Uri(runtime.ManagementPageUrl);
    }

    private static string BuildBootstrapScript(BackendRuntimeInfo runtime)
    {
        var apiBase = runtime.BaseUrl;
        var managementKey = runtime.ManagementKey;

        return
            "(() => {" +
            $"localStorage.setItem('apiBase', '{JsEscape(apiBase)}');" +
            $"localStorage.setItem('managementKey', '{JsEscape(managementKey)}');" +
            "localStorage.setItem('isLoggedIn', 'true');" +
            "if (window.cpadHost?.invoke) { return; }" +
            "const cpadPending = new Map();" +
            "window.chrome?.webview?.addEventListener('message', event => {" +
            "  const data = event.data;" +
            "  if (!data || data.kind !== 'cpad-response' || !data.requestId) { return; }" +
            "  const pending = cpadPending.get(data.requestId);" +
            "  if (!pending) { return; }" +
            "  cpadPending.delete(data.requestId);" +
            "  if (data.ok === false) { pending.reject(new Error(data.error || 'cpad host error')); return; }" +
            "  pending.resolve(data.payload ?? null);" +
            "});" +
            "const invoke = (command, payload) => new Promise((resolve, reject) => {" +
            "  const requestId = `cpad-${Date.now()}-${Math.random().toString(16).slice(2)}`;" +
            "  cpadPending.set(requestId, { resolve, reject });" +
            "  window.chrome?.webview?.postMessage(JSON.stringify({ requestId, command, payload: payload ?? null }));" +
            "});" +
            "window.cpadHost = Object.freeze({" +
            "  invoke," +
            "  openLogs: () => invoke('openLogs')," +
            "  openConfig: () => invoke('openConfig')," +
            "  openCodexSettings: () => invoke('openCodexSettings')," +
            "  getBackendStatus: () => invoke('getBackendStatus')," +
            "  getDesktopVersion: () => invoke('getDesktopVersion')," +
            "  launchCodex: source => invoke('launchCodex', { source: source ?? null })" +
            "});" +
            "})();";
    }

    private static string JsEscape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
    }

    private void ShowWebViewOverlay(string title, string? detail)
    {
        WebViewOverlay.Visibility = Visibility.Visible;
        ManagementWebView.Visibility = Visibility.Collapsed;
        _viewModel.WebViewStatusText = string.IsNullOrWhiteSpace(detail) ? title : $"{title}{Environment.NewLine}{detail}";
    }

    private void HideWebViewOverlay()
    {
        WebViewOverlay.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Visible;
    }

    private async Task StartBackendAsync()
    {
        _viewModel.FooterStatus = "正在启动后端...";
        await _backendProcessManager.StartAsync();
    }

    private async Task DetectCodexAsync()
    {
        var executablePath = await _codexLocator.LocateAsync();
        var version = executablePath is null ? null : await _codexVersionReader.ReadAsync();
        var authState = executablePath is null ? "未找到 Codex CLI。" : await _codexAuthStateReader.ReadAsync();
        _codexStatus = await _codexConfigService.InspectAsync(_viewModel.RepositoryPath, executablePath, version, authState);

        _viewModel.CodexBadgeText = _codexStatus.IsInstalled ? "Codex 已安装" : "Codex 缺失";
        _viewModel.CodexSummaryText = _codexStatus.IsInstalled
            ? $"已找到 Codex：{_codexStatus.Version ?? "版本未知"}"
            : "未找到 Codex CLI，可继续完成桌面宿主安装和配置。";
        _viewModel.CodexDetailText =
            $"默认 profile：{_codexStatus.DefaultProfile}{Environment.NewLine}" +
            $"用户配置：{(_codexStatus.HasUserConfig ? "存在" : "不存在")}{Environment.NewLine}" +
            $"项目配置：{(_codexStatus.HasProjectConfig ? "存在" : "不存在")}{Environment.NewLine}" +
            $"认证状态：{_codexStatus.AuthenticationState}";
        _viewModel.CurrentSourceText = _codexStatus.EffectiveSource;
        UpdateCommandPreview();
    }

    private async Task EnsureCodexProfilesAsync(CodexSourceKind defaultSource)
    {
        var port = _backendProcessManager.CurrentStatus.Runtime?.Port ?? _settings.BackendPort;
        await _codexConfigService.ApplyDesktopModeAsync(port, defaultSource);
        _settings.PreferredCodexSource = defaultSource;
        _settings.LastRepositoryPath = _viewModel.RepositoryPath;
        await _configurationService.SaveAsync(_settings);
        _viewModel.CurrentSourceText = CodexConfigService.GetSourceName(defaultSource);
        UpdateCommandPreview();
    }

    private void UpdateCommandPreview()
    {
        _viewModel.CommandPreviewText = _codexConfigService.BuildLaunchCommand(
            _settings.PreferredCodexSource,
            _viewModel.RepositoryPath);
    }

    private void UpdateDiagnosticsSummary()
    {
        _viewModel.DiagnosticsSummaryText =
            $"WebView2：{_webViewRuntimeStatus.Summary} | 后端：{_backendProcessManager.CurrentStatus.State} | Codex：{(_codexStatus.IsInstalled ? "已安装" : "未安装")}";
    }

    private async Task PersistRepositoryPathAsync()
    {
        _settings.LastRepositoryPath = _viewModel.RepositoryPath;
        await _configurationService.SaveAsync(_settings);
        await DetectCodexAsync();
    }

    private string BuildDiagnosticsText()
    {
        return _diagnosticsService.BuildReport(
            _backendProcessManager.CurrentStatus,
            _codexStatus,
            _webViewRuntimeStatus);
    }

    private bool ValidateWritableDirectories()
    {
        var checks = new[]
        {
            ("应用数据目录", _pathService.Directories.RootDirectory),
            ("日志目录", _directoryAccessService.GetLogsDirectory()),
            ("配置目录", _directoryAccessService.GetConfigDirectory()),
            ("后端目录", _directoryAccessService.GetBackendDirectory())
        };

        foreach (var (label, path) in checks)
        {
            var error = _directoryAccessService.GetWriteAccessError(path);
            if (error is null)
            {
                continue;
            }

            var detail = $"{label} 无法写入：{path}{Environment.NewLine}{error}";
            _logger.LogError(detail);
            _viewModel.FooterStatus = detail;
            _viewModel.BackendStatusText = "目录初始化失败。";
            _viewModel.BackendDetailText = detail;
            _viewModel.WebViewStatusText = detail;
            ShowWebViewOverlay("目录初始化失败。", detail);
            MessageBox.Show(detail, "CPAD 目录错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        return true;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public async Task CloseForAutomationAsync()
    {
        await CloseForAutomationAsync(0);
    }

    public async Task CloseForAutomationAsync(int exitCode)
    {
        if (_automationExitRequested)
        {
            return;
        }

        _automationExitRequested = true;
        _allowClose = true;
        _notifyIcon.Visible = false;
        Environment.ExitCode = exitCode;
        await _backendProcessManager.StopAsync();
        Close();
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _backendProcessManager.Dispose();
            return;
        }

        var result = MessageBox.Show(
            "选择“是”退出全部并关闭后端，选择“否”仅隐藏到托盘。",
            "关闭 CPAD",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        if (result == MessageBoxResult.No)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _allowClose = true;
        await _backendProcessManager.StopAsync();
        Close();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private async void ManagementWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            _logger.Info("WebView2 导航成功。");
            HideWebViewOverlay();
            _viewModel.WebViewStatusText = "管理页已加载。";
            if (_automationWaitForWebView)
            {
                var bridgeReady = await VerifyBridgeAsync();
                await CloseForAutomationAsync(bridgeReady ? 0 : 1);
            }
        }
        else
        {
            _logger.LogError($"WebView2 导航失败：{e.WebErrorStatus}");
            ShowWebViewOverlay("管理页加载失败。", e.WebErrorStatus.ToString());
            _ = FailAutomationIfNeededAsync();
        }
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? message;
        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var requestId = root.TryGetProperty("requestId", out var requestIdNode)
                ? requestIdNode.GetString()
                : null;
            var command = root.TryGetProperty("command", out var commandNode)
                ? commandNode.GetString()
                : null;
            var payload = root.TryGetProperty("payload", out var payloadNode)
                ? payloadNode
                : default;

            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            object? responsePayload = command switch
            {
                "openLogs" => OpenDirectoryAndReturn(_directoryAccessService.GetLogsDirectory()),
                "openConfig" => OpenDirectoryAndReturn(_directoryAccessService.GetConfigDirectory()),
                "openCodexSettings" => OpenDirectoryAndReturn(_codexConfigService.GetUserConfigDirectory()),
                "getBackendStatus" => new
                {
                    state = _backendProcessManager.CurrentStatus.State.ToString(),
                    message = _backendProcessManager.CurrentStatus.Message,
                    port = _backendProcessManager.CurrentStatus.Runtime?.Port,
                    baseUrl = _backendProcessManager.CurrentStatus.Runtime?.BaseUrl,
                    managementPageUrl = _backendProcessManager.CurrentStatus.Runtime?.ManagementPageUrl
                },
                "getDesktopVersion" => new
                {
                    version = _buildInfo.ApplicationVersion,
                    informationalVersion = _buildInfo.InformationalVersion
                },
                "launchCodex" => LaunchCodexFromBridge(payload),
                _ => throw new InvalidOperationException($"未支持的桥接命令：{command}")
            };

            PostBridgeResponse(requestId, true, responsePayload, null);
        }
        catch (Exception exception)
        {
            _logger.LogError("处理 WebView2 桥接消息失败。", exception);
            try
            {
                using var document = JsonDocument.Parse(message);
                var requestId = document.RootElement.TryGetProperty("requestId", out var requestIdNode)
                    ? requestIdNode.GetString()
                    : null;
                PostBridgeResponse(requestId, false, null, exception.Message);
            }
            catch
            {
                // Ignore malformed bridge messages.
            }
        }
    }

    private static string? TryDetectRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }
        }

        return null;
    }

    private async void StartBackendButton_Click(object sender, RoutedEventArgs e)
    {
        await StartBackendAsync();
    }

    private async void StopBackendButton_Click(object sender, RoutedEventArgs e)
    {
        await _backendProcessManager.StopAsync();
    }

    private async void RestartBackendButton_Click(object sender, RoutedEventArgs e)
    {
        await _backendProcessManager.RestartAsync();
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        _directoryAccessService.OpenDirectory(_directoryAccessService.GetLogsDirectory());
    }

    private void OpenConfigButton_Click(object sender, RoutedEventArgs e)
    {
        _directoryAccessService.OpenDirectory(_directoryAccessService.GetConfigDirectory());
    }

    private void OpenBackendButton_Click(object sender, RoutedEventArgs e)
    {
        _directoryAccessService.OpenDirectory(_directoryAccessService.GetBackendDirectory());
    }

    private async void DetectCodexButton_Click(object sender, RoutedEventArgs e)
    {
        await DetectCodexAsync();
        UpdateDiagnosticsSummary();
    }

    private async void SetOfficialDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        await EnsureCodexProfilesAsync(CodexSourceKind.Official);
        await DetectCodexAsync();
    }

    private async void SetCpaDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        await EnsureCodexProfilesAsync(CodexSourceKind.Cpa);
        await DetectCodexAsync();
    }

    private async void RestoreOfficialButton_Click(object sender, RoutedEventArgs e)
    {
        await EnsureCodexProfilesAsync(CodexSourceKind.Official);
        await DetectCodexAsync();
    }

    private void CopyCommandButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_viewModel.CommandPreviewText);
        _viewModel.FooterStatus = "已复制启动命令。";
    }

    private void LaunchCurrentButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchCodex(_settings.PreferredCodexSource);
    }

    private void LaunchOfficialButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchCodex(CodexSourceKind.Official);
    }

    private void LaunchCpaButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchCodex(CodexSourceKind.Cpa);
    }

    private void OpenRepoTerminalButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchCodex(_settings.PreferredCodexSource);
    }

    private void OpenCodexConfigButton_Click(object sender, RoutedEventArgs e)
    {
        _directoryAccessService.OpenDirectory(_codexConfigService.GetUserConfigDirectory());
    }

    private async void RepositoryPathTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        await PersistRepositoryPathAsync();
    }

    private async void BrowseRepositoryButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            SelectedPath = string.IsNullOrWhiteSpace(_viewModel.RepositoryPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : _viewModel.RepositoryPath,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            _viewModel.RepositoryPath = dialog.SelectedPath;
            await PersistRepositoryPathAsync();
        }
    }

    private async void StartupCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _startupRegistrationService.SetEnabled(_viewModel.StartWithWindows);
        _settings.StartWithWindows = _viewModel.StartWithWindows;
        await _configurationService.SaveAsync(_settings);
    }

    private void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(BuildDiagnosticsText());
        _viewModel.FooterStatus = "已复制诊断信息。";
    }

    private async void ResetDesktopConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var managementKey = _settings.ManagementKey;
        var backendPort = _settings.BackendPort;
        _settings = new DesktopSettings
        {
            ManagementKey = managementKey,
            BackendPort = backendPort,
            PreferredCodexSource = CodexSourceKind.Official,
            LastRepositoryPath = _viewModel.RepositoryPath
        };

        await _configurationService.SaveAsync(_settings);
        await EnsureCodexProfilesAsync(_settings.PreferredCodexSource);
        _viewModel.FooterStatus = "桌面配置已重置。";
    }

    private async void RetryWebViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProcessManager.CurrentStatus.Runtime is not null)
        {
            await NavigateToManagementPageAsync(_backendProcessManager.CurrentStatus.Runtime);
        }
        else
        {
            await StartBackendAsync();
        }
    }

    private async void RefreshWebViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (ManagementWebView.CoreWebView2 is null)
        {
            await RetryWebViewButton_ClickAsync();
            return;
        }

        ManagementWebView.Reload();
    }

    private void OpenExternalBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        var url = _backendProcessManager.CurrentStatus.Runtime?.ManagementPageUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("后端尚未启动，无法打开管理页。", "CPAD", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private async void ClearWebViewCacheButton_Click(object sender, RoutedEventArgs e)
    {
        if (ManagementWebView.CoreWebView2?.Profile is null)
        {
            return;
        }

        await ManagementWebView.CoreWebView2.Profile.ClearBrowsingDataAsync();
        _viewModel.FooterStatus = "已清理 WebView2 缓存。";
    }

    private void OpenDevToolsButton_Click(object sender, RoutedEventArgs e)
    {
        ManagementWebView.CoreWebView2?.OpenDevToolsWindow();
    }

    private async Task RetryWebViewButton_ClickAsync()
    {
        if (_backendProcessManager.CurrentStatus.Runtime is not null)
        {
            await NavigateToManagementPageAsync(_backendProcessManager.CurrentStatus.Runtime);
        }
    }

    private Task FailAutomationIfNeededAsync()
    {
        return _automationWaitForWebView
            ? CloseForAutomationAsync(exitCode: 1)
            : Task.CompletedTask;
    }

    private string OpenDirectoryAndReturn(string path)
    {
        _directoryAccessService.OpenDirectory(path);
        return path;
    }

    private object LaunchCodexFromBridge(JsonElement payload)
    {
        var source = _settings.PreferredCodexSource;
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("source", out var sourceNode)
            && sourceNode.ValueKind == JsonValueKind.String)
        {
            var requestedSource = sourceNode.GetString();
            source = string.Equals(requestedSource, "cpa", StringComparison.OrdinalIgnoreCase)
                ? CodexSourceKind.Cpa
                : string.Equals(requestedSource, "official", StringComparison.OrdinalIgnoreCase)
                    ? CodexSourceKind.Official
                    : _settings.PreferredCodexSource;
        }

        var result = _codexLaunchService.LaunchInTerminal(source, _viewModel.RepositoryPath);
        if (!result.IsSuccess)
        {
            TryCreateErrorSnapshot("启动 Codex 失败", result.ErrorMessage);
            throw new InvalidOperationException(result.ErrorMessage ?? "启动 Codex 失败。");
        }

        return new
        {
            source = CodexConfigService.GetSourceName(source),
            command = result.Command
        };
    }

    private void PostBridgeResponse(string? requestId, bool ok, object? payload, string? error)
    {
        if (ManagementWebView.CoreWebView2 is null || string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var json = JsonSerializer.Serialize(new
        {
            kind = "cpad-response",
            requestId,
            ok,
            payload,
            error
        });
        ManagementWebView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private async Task<bool> VerifyBridgeAsync()
    {
        if (ManagementWebView.CoreWebView2 is null)
        {
            _logger.LogError("桥接验证失败：CoreWebView2 未初始化。");
            return false;
        }

        try
        {
            var desktopVersionJson = await ManagementWebView.CoreWebView2.ExecuteScriptAsync(
                "window.cpadHost.getDesktopVersion().then(result => JSON.stringify(result))");
            var backendStatusJson = await ManagementWebView.CoreWebView2.ExecuteScriptAsync(
                "window.cpadHost.getBackendStatus().then(result => JSON.stringify(result))");
            _logger.Info($"桥接验证成功：desktopVersion={desktopVersionJson}; backendStatus={backendStatusJson}");
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError("桥接验证失败。", exception);
            return false;
        }
    }

    private void LaunchCodex(CodexSourceKind source)
    {
        var result = _codexLaunchService.LaunchInTerminal(source, _viewModel.RepositoryPath);
        if (result.IsSuccess)
        {
            _viewModel.FooterStatus = $"已在终端启动 {CodexConfigService.GetSourceName(source)}。";
            return;
        }

        var detail = result.ErrorMessage ?? "启动 Codex 失败。";
        _viewModel.FooterStatus = detail;
        TryCreateErrorSnapshot("启动 Codex 失败", detail);
        MessageBox.Show(detail, "CPAD", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void TryCreateErrorSnapshot(string title, string? detail, Exception? exception = null)
    {
        try
        {
            _diagnosticsService.CreateErrorSnapshot(
                title,
                detail,
                exception,
                _backendProcessManager.CurrentStatus,
                _codexStatus,
                _webViewRuntimeStatus);
        }
        catch (Exception snapshotException)
        {
            _logger.LogError("写入错误快照失败。", snapshotException);
        }
    }
}
