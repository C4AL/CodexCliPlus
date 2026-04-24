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

        Assert.Contains("SetVirtualHostNameToFolderMapping", hostSource, StringComparison.Ordinal);
        Assert.Contains("AddScriptToExecuteOnDocumentCreatedAsync", hostSource, StringComparison.Ordinal);
        Assert.Contains("WebMessageReceived", hostSource, StringComparison.Ordinal);
        Assert.Contains("openExternal", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("__CPAD_DESKTOP_BRIDGE__", bridgeSource, StringComparison.Ordinal);
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
}
