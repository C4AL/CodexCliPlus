using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
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
using CodexCliPlus.Core.Exceptions;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.LocalEnvironment;
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
        ShowPreparationStep(10, "正在准备本地目录。", StartupState.Preparing);

        try
        {
            await _pathService.EnsureCreatedAsync();
            _changeBroadcastService.Start();
            MarkStartupPhase("paths-ready");

            ShowPreparationStep(25, "正在检查 WebView2 运行时。", StartupState.Preparing);
            EnsureWebView2Runtime();
            MarkStartupPhase("webview2-runtime-ready");

            ShowPreparationStep(40, "正在定位管理界面和后端资产。", StartupState.Preparing);
            var bundle = _webUiAssetLocator.GetRequiredBundle();
            MarkStartupPhase("webui-assets-ready");

            ShowPreparationStep(55, "正在确认后端配置。", StartupState.Preparing);
            if (
                restartBackend
                && _backendProcessManager.CurrentStatus.State == BackendStateKind.Running
            )
            {
                ShowPreparationStep(68, "正在重启本地后端。", StartupState.Preparing);
                await SyncPersistenceBeforeExitAsync();
                await _backendProcessManager.RestartAsync();
            }

            ShowPreparationStep(72, "正在启动本地后端。", StartupState.Preparing);
            var connection = await _sessionService.GetConnectionAsync();
            MarkStartupPhase("backend-ready");

            ShowPreparationStep(86, "本地后端健康检查已通过。", StartupState.Preparing);
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

            ShowPreparationStep(95, "正在打开管理界面。", StartupState.LoadingManagement);
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

                case "requestCodexRouteState":
                    _ = Dispatcher.InvokeAsync(async () =>
                        await SendCodexRouteStateAsync(ReadRequestId(root))
                    );
                    break;

                case "switchCodexRoute":
                    var targetMode =
                        root.TryGetProperty("targetMode", out var targetModeElement)
                        && targetModeElement.ValueKind == JsonValueKind.String
                            ? targetModeElement.GetString()
                            : null;
                    _ = Dispatcher.InvokeAsync(async () =>
                        await SwitchCodexRouteAsync(ReadRequestId(root), targetMode)
                    );
                    break;

                case "requestLocalDependencySnapshot":
                    _ = SendLocalDependencySnapshotAsync(ReadRequestId(root));
                    break;

                case "runLocalDependencyRepair":
                    var actionId =
                        root.TryGetProperty("actionId", out var actionIdElement)
                        && actionIdElement.ValueKind == JsonValueKind.String
                            ? actionIdElement.GetString()
                            : null;
                    _ = Dispatcher.InvokeAsync(async () =>
                        await RunLocalDependencyRepairAsync(ReadRequestId(root), actionId)
                    );
                    break;

                case "managementRequest":
                    var managementRequest = ReadDesktopManagementRequest(root);
                    _ = Dispatcher.InvokeAsync(async () =>
                        await HandleDesktopManagementRequestAsync(managementRequest)
                    );
                    break;
            }
        }
        catch { }
    }

    private async Task HandleDesktopManagementRequestAsync(DesktopManagementBridgeRequest request)
    {
        var requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? Guid.NewGuid().ToString("N")
            : request.RequestId;

        try
        {
            var method = CreateHttpMethod(request.Method);
            var path = NormalizeManagementPath(request.Path);
            var accept = string.IsNullOrWhiteSpace(request.Accept)
                ? "application/json"
                : NormalizeMediaType(request.Accept);
            var contentType = NormalizeMediaType(request.ContentType);

            _logger.Info($"Desktop management request {requestId}: {method.Method} {path}");

            ManagementApiResponse<string> response;
            if (request.Files.Count > 0)
            {
                var files = await ProtectDesktopManagementFilesAsync(method, path, request.Files);
                response = await _managementApiClient.SendManagementMultipartAsync(
                    method,
                    path,
                    files,
                    request.Fields,
                    accept
                );
            }
            else
            {
                var body = await ProtectDesktopManagementBodyAsync(
                    method,
                    path,
                    request.Body,
                    contentType
                );
                response = await _managementApiClient.SendManagementAsync(
                    method,
                    path,
                    body,
                    contentType,
                    accept
                );
            }

            PostWebUiCommand(
                new
                {
                    type = "managementResponse",
                    requestId,
                    ok = true,
                    status = (int)response.StatusCode,
                    body = response.Value,
                    metadata = response.Metadata,
                }
            );

            if (!IsReadOnlyMethod(method))
            {
                _changeBroadcastService.Broadcast(ResolveManagementChangeScopes(method, path));
            }
        }
        catch (ManagementApiException exception)
        {
            _logger.Warn($"Desktop management request {requestId} failed: {exception.Message}");
            PostWebUiCommand(
                new
                {
                    type = "managementResponse",
                    requestId,
                    ok = false,
                    status = exception.StatusCode ?? 500,
                    body = string.IsNullOrWhiteSpace(exception.ResponseBody)
                        ? JsonSerializer.Serialize(
                            new { message = exception.Message },
                            WebMessageJsonOptions
                        )
                        : exception.ResponseBody,
                    error = exception.Message,
                }
            );
        }
        catch (Exception exception)
        {
            _logger.Warn(
                $"Desktop management request {requestId} failed before forwarding: {exception.Message}"
            );
            PostWebUiCommand(
                new
                {
                    type = "managementResponse",
                    requestId,
                    ok = false,
                    status = 500,
                    body = JsonSerializer.Serialize(
                        new { message = exception.Message },
                        WebMessageJsonOptions
                    ),
                    error = exception.Message,
                }
            );
        }
    }

    private async Task<IReadOnlyList<ManagementMultipartFile>> ProtectDesktopManagementFilesAsync(
        HttpMethod method,
        string path,
        IReadOnlyList<DesktopManagementBridgeFile> files
    )
    {
        var protectedFiles = new List<ManagementMultipartFile>(files.Count);
        foreach (var file in files)
        {
            var content = file.Content;
            if (
                !IsReadOnlyMethod(method)
                && IsSensitiveWritePath(path)
                && IsTextualContentType(file.ContentType)
            )
            {
                var text = Encoding.UTF8.GetString(content);
                var migrated = await _configMigrationService.MigrateJsonAsync(
                    text,
                    $"desktop-management:{path.TrimStart('/')}:{file.FileName}"
                );
                content = Encoding.UTF8.GetBytes(migrated.Content);
            }

            protectedFiles.Add(
                new ManagementMultipartFile
                {
                    FieldName = string.IsNullOrWhiteSpace(file.FieldName) ? "file" : file.FieldName,
                    FileName = string.IsNullOrWhiteSpace(file.FileName)
                        ? "upload.json"
                        : file.FileName,
                    Content = content,
                    ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                        ? "application/octet-stream"
                        : NormalizeMediaType(file.ContentType),
                }
            );
        }

        return protectedFiles;
    }

    private async Task<string?> ProtectDesktopManagementBodyAsync(
        HttpMethod method,
        string path,
        string? body,
        string contentType
    )
    {
        if (
            string.IsNullOrWhiteSpace(body)
            || IsReadOnlyMethod(method)
            || string.Equals(method.Method, "DELETE", StringComparison.OrdinalIgnoreCase)
            || !IsSensitiveWritePath(path)
        )
        {
            return body;
        }

        var source = $"desktop-management:{path.TrimStart('/')}";
        if (
            path.StartsWith("/config.yaml", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("yaml", StringComparison.OrdinalIgnoreCase)
        )
        {
            var migrated = await _configMigrationService.MigrateYamlAsync(body, source);
            return migrated.Content;
        }

        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || LooksLikeJson(body))
        {
            var migrated = await _configMigrationService.MigrateJsonAsync(body, source);
            return migrated.Content;
        }

        return body;
    }

    private static DesktopManagementBridgeRequest ReadDesktopManagementRequest(JsonElement root)
    {
        var files = new List<DesktopManagementBridgeFile>();
        if (
            root.TryGetProperty("files", out var filesElement)
            && filesElement.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var item in filesElement.EnumerateArray())
            {
                var base64 = ReadString(item, "contentBase64");
                byte[] content;
                try
                {
                    content = string.IsNullOrWhiteSpace(base64)
                        ? []
                        : Convert.FromBase64String(base64);
                }
                catch (FormatException)
                {
                    content = [];
                }

                files.Add(
                    new DesktopManagementBridgeFile(
                        ReadString(item, "fieldName") ?? "file",
                        ReadString(item, "fileName") ?? "upload.json",
                        ReadString(item, "contentType") ?? "application/octet-stream",
                        content
                    )
                );
            }
        }

        Dictionary<string, string>? fields = null;
        if (
            root.TryGetProperty("fields", out var fieldsElement)
            && fieldsElement.ValueKind == JsonValueKind.Object
        )
        {
            fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in fieldsElement.EnumerateObject())
            {
                fields[property.Name] =
                    property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.ToString();
            }
        }

        return new DesktopManagementBridgeRequest(
            ReadString(root, "requestId"),
            ReadString(root, "method") ?? "GET",
            ReadString(root, "path") ?? "/",
            ReadString(root, "body"),
            ReadString(root, "contentType") ?? "application/json",
            ReadString(root, "accept"),
            files,
            fields
        );
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return
            element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static HttpMethod CreateHttpMethod(string method)
    {
        var normalized = string.IsNullOrWhiteSpace(method)
            ? "GET"
            : method.Trim().ToUpperInvariant();
        return normalized switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            "HEAD" => HttpMethod.Head,
            "OPTIONS" => HttpMethod.Options,
            _ => throw new InvalidOperationException("不支持的管理请求方法。"),
        };
    }

    private static string NormalizeManagementPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("管理请求路径不能为空。");
        }

        var trimmed = path.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("桌面管理代理只允许相对管理接口路径。");
        }

        var normalized = trimmed[0] == '/' ? trimmed : $"/{trimmed}";
        return normalized.Replace(
            "/generative-language-api-key",
            "/gemini-api-key",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string NormalizeMediaType(string? value)
    {
        var rawValue = value ?? "application/json";
        var separatorIndex = rawValue.IndexOf(';', StringComparison.Ordinal);
        var mediaType = (separatorIndex >= 0 ? rawValue[..separatorIndex] : rawValue).Trim();
        return string.IsNullOrWhiteSpace(mediaType) ? "application/json" : mediaType;
    }

    private static bool IsReadOnlyMethod(HttpMethod method)
    {
        return method == HttpMethod.Get
            || method == HttpMethod.Head
            || method == HttpMethod.Options;
    }

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[') || trimmed.StartsWith('"');
    }

    private static bool IsTextualContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSensitiveWritePath(string path)
    {
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        var normalized = (queryIndex >= 0 ? path[..queryIndex] : path).TrimEnd('/');
        return normalized.Equals("/config.yaml", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/api-keys", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("/auth-files", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/gemini-api-key", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/codex-api-key", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/claude-api-key", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/vertex-api-key", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/openai-compatibility", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/ampcode/upstream-api-key", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/ampcode/upstream-api-keys", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ResolveManagementChangeScopes(HttpMethod method, string path)
    {
        if (IsReadOnlyMethod(method))
        {
            return [];
        }

        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        var normalized = (queryIndex >= 0 ? path[..queryIndex] : path).TrimEnd('/');
        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "config" };

        if (
            normalized.StartsWith("/api-keys", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("api-key", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("/openai-compatibility", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("/ampcode", StringComparison.OrdinalIgnoreCase)
        )
        {
            scopes.Add("providers");
            scopes.Add("quota");
        }

        if (
            normalized.StartsWith("/auth-files", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("/oauth-", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("/model-definitions", StringComparison.OrdinalIgnoreCase)
        )
        {
            scopes.Add("auth-files");
            scopes.Add("quota");
            scopes.Add("providers");
        }

        if (normalized.StartsWith("/usage", StringComparison.OrdinalIgnoreCase))
        {
            scopes.Add("usage");
            scopes.Add("persistence");
        }

        if (normalized.StartsWith("/logs", StringComparison.OrdinalIgnoreCase))
        {
            scopes.Add("logs");
        }

        return scopes.ToArray();
    }

    private async Task SendLocalDependencySnapshotAsync(string? requestId)
    {
        try
        {
            var snapshot = await GetLocalDependencySnapshotAsync();
            await Dispatcher.InvokeAsync(() =>
                PostWebUiCommand(
                    new
                    {
                        type = "localDependencySnapshot",
                        requestId,
                        snapshot,
                    }
                )
            );
        }
        catch (Exception exception)
        {
            await Dispatcher.InvokeAsync(() =>
                PostWebUiCommand(
                    new
                    {
                        type = "localDependencySnapshot",
                        requestId,
                        error = exception.Message,
                    }
                )
            );
        }
    }

    private Task<LocalDependencySnapshot> GetLocalDependencySnapshotAsync()
    {
        lock (_localDependencySnapshotLock)
        {
            if (_localDependencySnapshotTask is { IsCompleted: false })
            {
                return _localDependencySnapshotTask;
            }

            _localDependencySnapshotTask = Task.Run(() =>
                _localDependencyHealthService.CheckAsync()
            );
            _ = _localDependencySnapshotTask.ContinueWith(
                completedTask =>
                {
                    lock (_localDependencySnapshotLock)
                    {
                        if (ReferenceEquals(_localDependencySnapshotTask, completedTask))
                        {
                            _localDependencySnapshotTask = null;
                        }
                    }
                },
                TaskScheduler.Default
            );

            return _localDependencySnapshotTask;
        }
    }

    private async Task RunLocalDependencyRepairAsync(string? requestId, string? actionId)
    {
        if (
            string.IsNullOrWhiteSpace(actionId) || !LocalDependencyRepairActionIds.IsKnown(actionId)
        )
        {
            PostWebUiCommand(
                new
                {
                    type = "localDependencyRepairResult",
                    requestId,
                    result = new
                    {
                        actionId = actionId ?? string.Empty,
                        succeeded = false,
                        summary = "未知修复动作。",
                        detail = "桌面端只接受内置白名单 action id。",
                    },
                }
            );
            return;
        }

        var confirm = MessageBox.Show(
            this,
            "即将以管理员权限启动受控修复进程。修复过程只会执行内置白名单动作，是否继续？",
            "确认本地环境修复",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning
        );
        if (confirm != MessageBoxResult.OK)
        {
            PostWebUiCommand(
                new
                {
                    type = "localDependencyRepairResult",
                    requestId,
                    result = new
                    {
                        actionId,
                        succeeded = false,
                        summary = "用户取消了修复。",
                        detail = "未执行任何修复动作。",
                    },
                }
            );
            return;
        }

        PostWebUiCommand(
            new
            {
                type = "localDependencyRepairStarted",
                requestId,
                actionId,
            }
        );

        var result = await _localDependencyRepairService.RunElevatedRepairAsync(actionId);
        var snapshot = await _localDependencyHealthService.CheckAsync();
        PostWebUiCommand(
            new
            {
                type = "localDependencyRepairResult",
                requestId,
                result,
                snapshot,
            }
        );

        if (result.Succeeded)
        {
            _changeBroadcastService.Broadcast("local-environment");
            _notificationService.ShowAuto(result.Summary);
        }
        else
        {
            _notificationService.ShowManual(result.Summary, result.Detail);
        }
    }

    private static string? ReadRequestId(JsonElement root)
    {
        return
            root.TryGetProperty("requestId", out var requestIdElement)
            && requestIdElement.ValueKind == JsonValueKind.String
            ? requestIdElement.GetString()
            : null;
    }

    private async Task SendCodexRouteStateAsync(string? requestId)
    {
        try
        {
            var state = await _codexConfigService.GetCodexRouteStateAsync();
            PostWebUiCommand(
                new
                {
                    type = "codexRouteResponse",
                    requestId,
                    ok = true,
                    state,
                }
            );
        }
        catch (Exception exception)
        {
            PostWebUiCommand(
                new
                {
                    type = "codexRouteResponse",
                    requestId,
                    ok = false,
                    error = exception.Message,
                }
            );
        }
    }

    private async Task SwitchCodexRouteAsync(string? requestId, string? targetMode)
    {
        try
        {
            var result = await _codexConfigService.SwitchCodexRouteAsync(
                targetMode ?? string.Empty,
                AppConstants.DefaultBackendPort
            );

            PostWebUiCommand(
                new
                {
                    type = "codexRouteResponse",
                    requestId,
                    ok = result.Succeeded,
                    state = result.State,
                    configBackupPath = result.ConfigBackupPath,
                    authBackupPath = result.AuthBackupPath,
                    officialAuthBackupPath = result.OfficialAuthBackupPath,
                    error = result.ErrorMessage,
                }
            );

            if (result.Succeeded)
            {
                _changeBroadcastService.Broadcast("config", "providers", "quota", "auth-files");
                _notificationService.ShowAuto(
                    result.State.CurrentMode == "cpa"
                        ? "已切换到 CPA 模式。"
                        : "已切换到官方模式。"
                );
                return;
            }

            _notificationService.ShowManual(
                "切换 Codex 路由失败",
                result.ErrorMessage ?? result.State.StatusMessage
            );
        }
        catch (Exception exception)
        {
            PostWebUiCommand(
                new
                {
                    type = "codexRouteResponse",
                    requestId,
                    ok = false,
                    error = exception.Message,
                }
            );
            _notificationService.ShowManual("切换 Codex 路由失败", exception.Message);
        }
    }

    private void ManagementChangeBroadcastService_DataChanged(
        object? sender,
        ManagementDataChangedEventArgs e
    )
    {
        Dispatcher.InvokeAsync(() =>
            PostWebUiCommand(
                new
                {
                    type = "dataChanged",
                    scopes = e.Scopes,
                    sequence = e.Sequence,
                }
            )
        );
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

    private sealed record DesktopManagementBridgeRequest(
        string? RequestId,
        string Method,
        string Path,
        string? Body,
        string ContentType,
        string? Accept,
        IReadOnlyList<DesktopManagementBridgeFile> Files,
        IReadOnlyDictionary<string, string>? Fields
    );

    private sealed record DesktopManagementBridgeFile(
        string FieldName,
        string FileName,
        string ContentType,
        byte[] Content
    );
}
