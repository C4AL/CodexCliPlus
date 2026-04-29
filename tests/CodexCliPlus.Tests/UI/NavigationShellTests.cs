using System.Text;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Services;

namespace CodexCliPlus.Tests.UI;

public sealed class NavigationShellTests
{
    [Fact]
    public void MainWindowHostsSingleWebViewAndTrayMenuContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml"), Encoding.UTF8);

        Assert.Contains("<wv2:WebView2", xaml, StringComparison.Ordinal);
        Assert.Contains("MouseLeftButtonDown=\"DragRegion_MouseLeftButtonDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LoginPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UpgradeNoticePanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FirstRunKeyPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ManagementKeyPasswordBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RememberManagementKeyCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FirstRunSecurityKeyTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FirstRunRememberSecurityKeyCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinimizeWindowButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("CloseWindowButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("复制密钥", xaml, StringComparison.Ordinal);
        Assert.Contains("保存到桌面", xaml, StringComparison.Ordinal);
        Assert.Contains("进入管理界面", xaml, StringComparison.Ordinal);
        Assert.Contains("忘记安全密钥/重置", xaml, StringComparison.Ordinal);
        Assert.Contains("当前步骤：", File.ReadAllText(Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml.cs"), Encoding.UTF8), StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TitleBarButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderThickness\" Value=\"0\"", xaml, StringComparison.Ordinal);
        Assert.True(CountOccurrences(xaml, "Style=\"{StaticResource TitleBarButtonStyle}\"") >= 2);
        Assert.Contains("x:Name=\"ShellSidebarButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellBrandDockButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellBrandDockPopup\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellDockAppVersionText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellDockCoreVersionText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellDockConnectionStatusText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellDockBackendAddressText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellDockCopyBackendAddressButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.AutomationId=\"ShellConnectionStatusPill\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"桌面宿主\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellRefreshButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellThemeButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PackIconLucide Kind=\"SunMoon\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("M12,3 C7.029,3 3,7.029 3,12", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellSettingsButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsOverlay\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"SettingsAppVersionText\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"SettingsBackendVersionText\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"SettingsConnectionText\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ui:NavigationView", xaml, StringComparison.Ordinal);
        Assert.Contains("TrayOpenMenuItem_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("TrayRestartBackendMenuItem_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("TrayCheckUpdatesMenuItem_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("TrayExitMenuItem_Click", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRegistersMinimalShellInsteadOfRuntimeManagementPages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "App.xaml.cs"), Encoding.UTF8);

        Assert.Contains("AddSingleton<WebUiAssetLocator>()", source, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<MainWindow>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IManagementNavigationService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DashboardPageViewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DashboardPage", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopHostInjectsBootstrapAndRedirectsExternalLinks()
    {
        var repositoryRoot = FindRepositoryRoot();
        var hostSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml.cs"), Encoding.UTF8);
        var bridgeSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "Services", "DesktopBridgeScriptFactory.cs"), Encoding.UTF8);

        Assert.Contains("http://{AppHostName}/index.html", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("https://{AppHostName}/index.html", hostSource, StringComparison.Ordinal);
        Assert.Contains("SetVirtualHostNameToFolderMapping", hostSource, StringComparison.Ordinal);
        Assert.Contains("AddScriptToExecuteOnDocumentCreatedAsync", hostSource, StringComparison.Ordinal);
        Assert.Contains("WebMessageReceived", hostSource, StringComparison.Ordinal);
        Assert.Contains("openExternal", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("requestNativeLogin", hostSource, StringComparison.Ordinal);
        Assert.Contains("requestNativeLogin", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("shellStateChanged", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("refreshAll", hostSource, StringComparison.Ordinal);
        Assert.Contains("setTheme", hostSource, StringComparison.Ordinal);
        Assert.Contains("toggleSidebarCollapsed", hostSource, StringComparison.Ordinal);
        Assert.Contains("__CODEXCLIPLUS_DESKTOP_BRIDGE__", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("StartupState", hostSource, StringComparison.Ordinal);
        Assert.Contains("UpgradeNotice", hostSource, StringComparison.Ordinal);
        Assert.Contains("FirstRunKeyReveal", hostSource, StringComparison.Ordinal);
        Assert.Contains("NativeLogin", hostSource, StringComparison.Ordinal);
        Assert.Contains("VerifyManagementKey", hostSource, StringComparison.Ordinal);
        Assert.Contains("LastSeenApplicationVersion", hostSource, StringComparison.Ordinal);
        Assert.Contains("SecurityKeyOnboardingCompleted", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopBridgePayloadUsesWebUiCamelCaseContract()
    {
        var script = DesktopBridgeScriptFactory.CreateInitializationScript(new DesktopBootstrapPayload
        {
            DesktopMode = true,
            ApiBase = "http://127.0.0.1:15345",
            ManagementKey = "secret-key",
            Theme = "white",
            ResolvedTheme = "light",
            SidebarCollapsed = true
        });

        Assert.Contains("\"desktopMode\":true", script, StringComparison.Ordinal);
        Assert.Contains("\"apiBase\":\"http://127.0.0.1:15345\"", script, StringComparison.Ordinal);
        Assert.Contains("\"managementKey\":\"secret-key\"", script, StringComparison.Ordinal);
        Assert.Contains("\"theme\":\"white\"", script, StringComparison.Ordinal);
        Assert.Contains("\"resolvedTheme\":\"light\"", script, StringComparison.Ordinal);
        Assert.Contains("\"sidebarCollapsed\":true", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\"DesktopMode\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ApiBase\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ManagementKey\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstRunOnboardingUsesIconActionsSingleLineKeyAndCountdownConfirm()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml"), Encoding.UTF8);
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml.cs"), Encoding.UTF8);
        var appProject = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "CodexCliPlus.App.csproj"), Encoding.UTF8);

        Assert.Contains("x:Key=\"ShellRaisedPanelStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MahApps.Metro.IconPacks.Lucide", appProject, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource ShellRaisedPanelStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FirstRunSaveToDesktopButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"保存到桌面\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FirstRunCopyKeyButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"复制密钥\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"静默登录\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"本机记住安全密钥\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FirstRunSilentLoginRiskIndicator\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LoginSilentLoginRiskIndicator\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Kind=\"CircleAlert\"", xaml, StringComparison.Ordinal);
        Assert.Contains("仅在受信任的个人设备上使用", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ShellInlineIconButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Padding=\"10,8,48,8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ShellIconViewboxStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Stretch\" Value=\"Uniform\"", xaml, StringComparison.Ordinal);
        Assert.Contains("M12,5 L18,11 L12,17", xaml, StringComparison.Ordinal);
        Assert.Contains("M8,8 L18,8 L18,18 L8,18 Z", xaml, StringComparison.Ordinal);
        Assert.Contains("StrokeThickness=\"1.8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ShellTextToolTipStyle\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Segoe MDL2 Assets", xaml, StringComparison.Ordinal);
        Assert.True(xaml.IndexOf("x:Name=\"FirstRunCopyKeyButton\"", StringComparison.Ordinal) <
            xaml.IndexOf("x:Name=\"FirstRunSaveToDesktopButton\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"FirstRunSaveToDesktopButton\"", StringComparison.Ordinal) <
            xaml.IndexOf("x:Name=\"FirstRunRememberSecurityKeyCheckBox\"", StringComparison.Ordinal));
        Assert.Contains("<ColumnDefinition Width=\"360\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"Auto\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ColumnDefinition Width=\"76\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"FirstRunSecurityKeyTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"NoWrap\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Hidden\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FirstRunSecurityKeyTextBox.ScrollToHome()", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FirstRunConfirmCloseButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource DialogCloseButtonStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"0,16,0,8\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"FirstRunConfirmCancelButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"返回\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"确认进入\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FirstRunConfirmContinueButton.Content = $\"确认 ({seconds})\"", source, StringComparison.Ordinal);
        Assert.Contains("_settings.ManagementKey = string.Empty;", source, StringComparison.Ordinal);
        Assert.Contains("ShowLogin(\"初始化已完成，请输入安全密钥登录。\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstRunActionStatusText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowFirstRunStatus", source, StringComparison.Ordinal);
        Assert.DoesNotContain("可以继续。", source, StringComparison.Ordinal);
        Assert.Contains("MinimumPreparationDisplayDuration = TimeSpan.FromMilliseconds(2500)", source, StringComparison.Ordinal);
        Assert.Contains("DoubleAnimation", source, StringComparison.Ordinal);
        Assert.Contains("LoadingBrandBadge", xaml, StringComparison.Ordinal);
        Assert.Contains("codexcliplus-display.png", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Source=\"pack://application:,,,/Resources/Icons/codexcliplus.ico\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PreparationProgressTrack\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ClipToBounds=\"True\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellNotificationsProvideAutoAndManualPlacementContracts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml"), Encoding.UTF8);
        var hostSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml.cs"), Encoding.UTF8);
        var serviceSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "Services", "Notifications", "ShellNotificationService.cs"), Encoding.UTF8);
        var requestSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "Services", "Notifications", "ShellNotificationRequest.cs"), Encoding.UTF8);

        Assert.Contains("x:Name=\"AutoNotificationStack\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ManualNotificationStack\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BottomCenterAuto", requestSource, StringComparison.Ordinal);
        Assert.Contains("BottomRightManual", requestSource, StringComparison.Ordinal);
        Assert.Contains("ShowAuto", serviceSource, StringComparison.Ordinal);
        Assert.Contains("ShowManual", serviceSource, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(2)", hostSource, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment.Right", hostSource, StringComparison.Ordinal);
        Assert.Contains("FadeOutAndRemoveAsync", hostSource, StringComparison.Ordinal);
        Assert.Contains("_notificationService.ShowAuto", hostSource, StringComparison.Ordinal);
        Assert.Contains("_notificationService.ShowManual", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstRunDesktopSavePathUsesOnlySystemDesktopDirectory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml.cs"), Encoding.UTF8);

        Assert.Contains("Environment.SpecialFolder.DesktopDirectory", source, StringComparison.Ordinal);
        Assert.Contains("Directory.Exists(normalizedDesktopDirectory)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SpecialFolder.UserProfile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("USERPROFILE", source, StringComparison.Ordinal);

        var method = typeof(CodexCliPlus.MainWindow).GetMethod(
            "BuildDesktopSecurityKeyFilePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopDirectory) || !Directory.Exists(desktopDirectory))
        {
            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, null));
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            return;
        }

        var filePath = Assert.IsType<string>(method.Invoke(null, null));
        var expectedDirectory = Path.GetFullPath(desktopDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var actualDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath))!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Assert.True(
            string.Equals(expectedDirectory, actualDirectory, StringComparison.OrdinalIgnoreCase),
            $"Expected '{actualDirectory}' to match the system desktop '{expectedDirectory}'.");
    }

    [Fact]
    public void DesktopWebUiRoutesLoginThroughNativeShell()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "App.tsx"),
            Encoding.UTF8);
        var protectedRouteSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "router", "ProtectedRoute.tsx"),
            Encoding.UTF8);
        var bridgeSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "desktop", "bridge.ts"),
            Encoding.UTF8);
        var authStoreSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "stores", "useAuthStore.ts"),
            Encoding.UTF8);
        var constantsSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "utils", "constants.ts"),
            Encoding.UTF8);
        var mainLayoutSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "components", "layout", "MainLayout.tsx"),
            Encoding.UTF8);
        var themeStoreSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "stores", "useThemeStore.ts"),
            Encoding.UTF8);
        var commonTypesSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "types", "common.ts"),
            Encoding.UTF8);

        Assert.Contains("desktopMode", appSource, StringComparison.Ordinal);
        Assert.Contains("element: <Navigate to=\"/\" replace />", appSource, StringComparison.Ordinal);
        Assert.Contains("restoreSession", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("isDesktopMode()", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("桌面登录已失效", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("返回登录", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("setRetryAttempt", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("requestNativeLogin", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("requestNativeLogin?:", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("shellStateChanged?:", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("subscribeDesktopShellCommand", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("sendShellStateChanged", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("!desktopMode &&", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("desktop-shell", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("toggleSidebarCollapsed", mainLayoutSource, StringComparison.Ordinal);
        Assert.DoesNotContain("key: 'light'", mainLayoutSource, StringComparison.Ordinal);
        Assert.DoesNotContain("export type Theme = 'light'", commonTypesSource, StringComparison.Ordinal);
        Assert.Contains("const order: Theme[] = ['auto', 'white', 'dark']", themeStoreSource, StringComparison.Ordinal);
        Assert.Contains("if (theme === 'light')", themeStoreSource, StringComparison.Ordinal);
        Assert.Contains("__CODEXCLIPLUS_DESKTOP_BRIDGE__", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("restoreSessionPromise = null", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("if (!desktopBootstrap)", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("normalizeApiBase(desktopBootstrap.apiBase)", authStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("currentState.apiBase", authStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("desktopBootstrap?.apiBase ||", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("STORAGE_KEY_AUTH = 'codexcliplus-auth'", constantsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("STORAGE_KEY_AUTH = 'cli-proxy-auth'", constantsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void VendoredWebUiDistContainsDesktopRecoveryBridge()
    {
        var repositoryRoot = FindRepositoryRoot();
        var distIndex = File.ReadAllText(
            Path.Combine(repositoryRoot, "resources", "webui", "upstream", "dist", "index.html"),
            Encoding.UTF8);

        Assert.Contains("requestNativeLogin", distIndex, StringComparison.Ordinal);
        Assert.Contains("shellStateChanged", distIndex, StringComparison.Ordinal);
        Assert.Contains("toggleSidebarCollapsed", distIndex, StringComparison.Ordinal);
        Assert.Contains("桌面登录已失效", distIndex, StringComparison.Ordinal);
    }

    [Fact]
    public void VendoredWebUiMetadataPinsUpstreamCommit()
    {
        var repositoryRoot = FindRepositoryRoot();
        var metadataPath = Path.Combine(repositoryRoot, "resources", "webui", "upstream", "sync.json");
        var metadata = File.ReadAllText(metadataPath, Encoding.UTF8);

        Assert.Contains("router-for-me/Cli-Proxy-API-Management-Center.git", metadata, StringComparison.Ordinal);
        Assert.Contains("b45639aa0169de8441bc964fb765f2405c10ccf4", metadata, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodexCliPlus.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
