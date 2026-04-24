using System.Text;

namespace CPAD.Tests.UI;

public sealed class NavigationShellTests
{
    [Fact]
    public void MainWindowHostsSingleWebViewAndTrayMenuContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "MainWindow.xaml"), Encoding.UTF8);

        Assert.Contains("<wv2:WebView2", xaml, StringComparison.Ordinal);
        Assert.Contains("MouseLeftButtonDown=\"DragRegion_MouseLeftButtonDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LoginPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ManagementKeyPasswordBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RememberManagementKeyCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinimizeWindowButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("CloseWindowButton_Click", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"CPAD\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TitleBarButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderThickness\" Value=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(xaml, "Style=\"{StaticResource TitleBarButtonStyle}\""));
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
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "App.xaml.cs"), Encoding.UTF8);

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
        var hostSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "MainWindow.xaml.cs"), Encoding.UTF8);
        var bridgeSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "Services", "DesktopBridgeScriptFactory.cs"), Encoding.UTF8);

        Assert.Contains("http://{AppHostName}/index.html", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("https://{AppHostName}/index.html", hostSource, StringComparison.Ordinal);
        Assert.Contains("SetVirtualHostNameToFolderMapping", hostSource, StringComparison.Ordinal);
        Assert.Contains("AddScriptToExecuteOnDocumentCreatedAsync", hostSource, StringComparison.Ordinal);
        Assert.Contains("WebMessageReceived", hostSource, StringComparison.Ordinal);
        Assert.Contains("openExternal", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("requestNativeLogin", hostSource, StringComparison.Ordinal);
        Assert.Contains("requestNativeLogin", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("__CPAD_DESKTOP_BRIDGE__", bridgeSource, StringComparison.Ordinal);
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

        Assert.Contains("desktopMode", appSource, StringComparison.Ordinal);
        Assert.Contains("element: <Navigate to=\"/\" replace />", appSource, StringComparison.Ordinal);
        Assert.Contains("restoreSession", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("isDesktopMode()", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("桌面登录已失效", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("返回登录", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("setRetryAttempt", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("requestNativeLogin", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("requestNativeLogin?:", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("restoreSessionPromise = null", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("if (!desktopBootstrap)", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("normalizeApiBase(desktopBootstrap.apiBase)", authStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("currentState.apiBase", authStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("desktopBootstrap?.apiBase ||", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("STORAGE_KEY_AUTH = 'cpad-auth'", constantsSource, StringComparison.Ordinal);
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
            if (File.Exists(Path.Combine(directory.FullName, "CliProxyApiDesktop.sln")))
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
