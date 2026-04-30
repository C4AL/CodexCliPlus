using System.Diagnostics;
using System.Globalization;
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
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Persistence;
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

namespace CodexCliPlus;

public partial class MainWindow
{
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
            MarkStartupPhase("paths-ready");

            ShowPreparationStep(
                "WebView2",
                25,
                "正在检查 WebView2 运行时。",
                StartupState.Preparing
            );
            EnsureWebView2Runtime();
            MarkStartupPhase("webview2-runtime-ready");

            ShowPreparationStep(
                "后端资产",
                40,
                "正在定位管理界面和后端资产。",
                StartupState.Preparing
            );
            var bundle = _webUiAssetLocator.GetRequiredBundle();
            MarkStartupPhase("webui-assets-ready");

            ShowPreparationStep("配置", 55, "正在确认后端配置。", StartupState.Preparing);
            if (
                restartBackend
                && _backendProcessManager.CurrentStatus.State == BackendStateKind.Running
            )
            {
                ShowPreparationStep("核心启动", 68, "正在重启本地后端。", StartupState.Preparing);
                await SyncPersistenceBeforeExitAsync();
                await _backendProcessManager.RestartAsync();
            }

            ShowPreparationStep("核心启动", 72, "正在启动本地后端。", StartupState.Preparing);
            var connection = await _sessionService.GetConnectionAsync();
            MarkStartupPhase("backend-ready");

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
            MarkStartupPhase("webview-navigation-started");
            await EnsureMinimumPreparationDisplayAsync();
            ShowWebView();
            MarkStartupPhase("webview-visible");
            StartPostStartupPersistenceImport();
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
        core.NavigationCompleted += CoreWebView2_NavigationCompleted;
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

    private void CoreWebView2_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e
    )
    {
        MarkStartupPhase(
            e.IsSuccess ? "webview-navigation-completed" : "webview-navigation-failed"
        );
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
                    if (navigationHoverZoneActive && CanShowNavigationDockPopup())
                    {
                        ShowNavigationDockIconsFromEdgeIntent();
                    }
                    else
                    {
                        CollapseNavigationDockWithDelay(shortDelay: false);
                    }

                    break;

                case "usageStatsRefreshed":
                    ScheduleUsageStatsRefreshedSync();
                    break;

                case "importAccountConfig":
                    var importMode =
                        root.TryGetProperty("mode", out var importModeElement)
                        && importModeElement.ValueKind == JsonValueKind.String
                            ? importModeElement.GetString()
                            : "json";
                    _ = Dispatcher.InvokeAsync(async () =>
                        await ImportAccountConfigAsync(importMode)
                    );
                    break;

                case "exportAccountConfig":
                    var exportMode =
                        root.TryGetProperty("mode", out var exportModeElement)
                        && exportModeElement.ValueKind == JsonValueKind.String
                            ? exportModeElement.GetString()
                            : "json";
                    _ = Dispatcher.InvokeAsync(async () =>
                        await ExportAccountConfigAsync(exportMode)
                    );
                    break;

                case "importSacPackage":
                    _ = Dispatcher.InvokeAsync(async () => await ImportSacPackageAsync());
                    break;

                case "exportSacPackage":
                    _ = Dispatcher.InvokeAsync(async () => await ExportSacPackageAsync());
                    break;

                case "clearUsageStats":
                    _ = Dispatcher.InvokeAsync(async () => await ClearUsageStatsAsync());
                    break;

                case "checkDesktopUpdate":
                    Dispatcher.InvokeAsync(() =>
                        SettingsCheckUpdateButton_Click(
                            SettingsCheckUpdateButton,
                            new RoutedEventArgs()
                        )
                    );
                    break;

                case "applyDesktopUpdate":
                    Dispatcher.InvokeAsync(() =>
                        SettingsApplyUpdateButton_Click(
                            SettingsApplyUpdateButton,
                            new RoutedEventArgs()
                        )
                    );
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
}
