using System.Text;

namespace CPAD.Tests.UI;

public sealed class ManagementDesignSystemTests
{
    [Fact]
    public void AppProjectReferencesWebView2AndPublishesVendoredWebUiAssets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var csproj = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "CPAD.App.csproj"), Encoding.UTF8);

        Assert.Contains("Microsoft.Web.WebView2", csproj, StringComparison.Ordinal);
        Assert.Contains("resources\\webui\\upstream\\dist\\**\\*", csproj, StringComparison.Ordinal);
        Assert.Contains("resources\\webui\\upstream\\sync.json", csproj, StringComparison.Ordinal);
        Assert.Contains("CliProxyApiManagementCenter.LICENSE.txt", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void AppResourcesStillMergeDesignSystemForLegacyCompileCompatibility()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appXaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "App.xaml"), Encoding.UTF8);

        Assert.Contains("CPAD.Management.DesignSystem;component/Themes/DesignSystem.xaml", appXaml, StringComparison.Ordinal);
        Assert.Contains("Views/Controls/WpfUi/FaFontIconStyle.xaml", appXaml, StringComparison.Ordinal);
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
