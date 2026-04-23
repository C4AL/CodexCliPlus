using System.Text;
using System.Text.RegularExpressions;

using CPAD.Services;

namespace CPAD.Tests.UI;

public sealed class NavigationShellTests
{
    [Fact]
    public void MainWindowNavigationUsesOfficialManagementPagesOnly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "MainWindow.xaml"), Encoding.UTF8);
        var expectedPageTypes = new[]
        {
            "DashboardPage",
            "ConfigPage",
            "AiProvidersPage",
            "AuthFilesPage",
            "OAuthPage",
            "QuotaPage",
            "UsagePage",
            "LogsPage",
            "SystemPage"
        };

        var targetMatches = Regex.Matches(xaml, "TargetPageType=\"\\{x:Type pages:(?<page>[A-Za-z0-9_]+)\\}\"");
        var actualPageTypes = targetMatches
            .Select(match => match.Groups["page"].Value)
            .ToArray();

        Assert.Equal(expectedPageTypes, actualPageTypes);
        Assert.Contains("IsPaneToggleVisible=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("检查更新", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ui:NavigationView.FooterMenuItems>", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRegistersOfficialPagesAndManagementShellServices()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "App.xaml.cs"), Encoding.UTF8);
        var expectedRegistrations = new[]
        {
            "IManagementNavigationService",
            "ManagementNavigationService",
            "IUnsavedChangesGuard",
            "UnsavedChangesGuard",
            "DashboardPageViewModel",
            "ConfigPageViewModel",
            "AiProvidersPageViewModel",
            "AuthFilesPageViewModel",
            "OAuthPageViewModel",
            "QuotaPageViewModel",
            "UsagePageViewModel",
            "LogsPageViewModel",
            "SystemPageViewModel",
            "DashboardPage",
            "ConfigPage",
            "AiProvidersPage",
            "AuthFilesPage",
            "OAuthPage",
            "QuotaPage",
            "UsagePage",
            "LogsPage",
            "SystemPage"
        };

        foreach (var registration in expectedRegistrations)
        {
            Assert.Contains(registration, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ManagementRouteCatalogMatchesOfficialPrimaryAndSecondaryRoutes()
    {
        var expectedPrimaryPaths = new[]
        {
            "/",
            "/config",
            "/ai-providers",
            "/auth-files",
            "/oauth",
            "/quota",
            "/usage",
            "/logs",
            "/system"
        };

        var expectedSecondaryPaths = new[]
        {
            "/ai-providers/gemini/new",
            "/ai-providers/gemini/:index",
            "/ai-providers/codex/new",
            "/ai-providers/codex/:index",
            "/ai-providers/claude/new",
            "/ai-providers/claude/:index",
            "/ai-providers/claude/models",
            "/ai-providers/vertex/new",
            "/ai-providers/vertex/:index",
            "/ai-providers/openai/new",
            "/ai-providers/openai/:index",
            "/ai-providers/openai/models",
            "/ai-providers/ampcode",
            "/auth-files/oauth-excluded",
            "/auth-files/oauth-model-alias"
        };

        Assert.Equal(expectedPrimaryPaths, ManagementRouteCatalog.Primary.Select(route => route.Path));
        Assert.Equal(expectedSecondaryPaths, ManagementRouteCatalog.All.Where(route => !route.IsPrimary).Select(route => route.Path));
    }

    [Fact]
    public void OldDynamicSectionUiHasBeenRemoved()
    {
        var repositoryRoot = FindRepositoryRoot();
        Assert.False(File.Exists(Path.Combine(repositoryRoot, "src", "CPAD.App", "Views", "Pages", "ManagementPageUi.cs")));
        Assert.False(File.Exists(Path.Combine(repositoryRoot, "src", "CPAD.App", "Views", "Pages", "ManagementPages.xaml.cs")));
        Assert.False(File.Exists(Path.Combine(repositoryRoot, "src", "CPAD.App", "Ui", "UiText.cs")));

        var appRoot = Path.Combine(repositoryRoot, "src", "CPAD.App");
        var forbiddenTokens = new[]
        {
            "ManagementPageUi",
            "CreateAutoGrid",
            "CreateCodeEditor",
            "PageRoot.Children.Add",
            "SectionPageFrame",
            "ISectionPageHost",
            "BuildOverviewContent(",
            "BuildAccountsContent(",
            "BuildConfigurationContent(",
            "BuildLogsContent(",
            "BuildQuotaContent(",
            "BuildSystemContent("
        };

        var findings = new List<string>();
        foreach (var file in Directory.EnumerateFiles(appRoot, "*.*", SearchOption.AllDirectories))
        {
            if (!IsSourceFile(file) || IsGeneratedFile(file))
            {
                continue;
            }

            var text = File.ReadAllText(file, Encoding.UTF8);
            foreach (var token in forbiddenTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    findings.Add($"{Path.GetRelativePath(repositoryRoot, file)} => {token}");
                }
            }
        }

        Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));
    }

    [Fact]
    public void SecondaryRouteInfrastructureExistsForNativeManagementPages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var expectedFiles = new[]
        {
            Path.Combine("src", "CPAD.App", "Services", "SecondaryRoutes", "ManagementSecondaryRouteHost.cs"),
            Path.Combine("src", "CPAD.App", "Services", "SecondaryRoutes", "ManagementSecondaryRouteDescriptor.cs"),
            Path.Combine("src", "CPAD.App", "Services", "SecondaryRoutes", "IManagementSecondaryRouteViewFactory.cs"),
            Path.Combine("src", "CPAD.App", "Services", "SecondaryRoutes", "AiProvidersRouteState.cs"),
            Path.Combine("src", "CPAD.App", "Services", "SecondaryRoutes", "AuthFilesRouteState.cs"),
            Path.Combine("src", "CPAD.App", "Views", "Pages", "AiProvidersSecondaryRouteViewFactory.cs"),
            Path.Combine("src", "CPAD.App", "Views", "Pages", "AuthFilesSecondaryRouteViewFactory.cs")
        };

        foreach (var file in expectedFiles)
        {
            Assert.True(File.Exists(Path.Combine(repositoryRoot, file)), file);
        }
    }

    [Fact]
    public void AiProvidersAndAuthFilesUseRouteHostsInsteadOfInlineEditors()
    {
        var repositoryRoot = FindRepositoryRoot();
        var aiProvidersSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "Views", "Pages", "AiProvidersPage.xaml.cs"), Encoding.UTF8);
        var authFilesSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "Views", "Pages", "AuthFilesPage.xaml.cs"), Encoding.UTF8);
        var aiRouteFactory = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "Views", "Pages", "AiProvidersSecondaryRouteViewFactory.cs"), Encoding.UTF8);
        var authRouteFactory = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "Views", "Pages", "AuthFilesSecondaryRouteViewFactory.cs"), Encoding.UTF8);

        Assert.Contains("ManagementSecondaryRouteHost", aiProvidersSource, StringComparison.Ordinal);
        Assert.Contains("AiProvidersRouteState", aiProvidersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ManagementCodeEditor", aiProvidersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildDetailShell", aiProvidersSource, StringComparison.Ordinal);

        Assert.Contains("ManagementSecondaryRouteHost", authFilesSource, StringComparison.Ordinal);
        Assert.Contains("AuthFilesRouteState", authFilesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ManagementTabs", authFilesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildDetailShell", authFilesSource, StringComparison.Ordinal);

        Assert.Contains("ai-providers-openai-models", aiRouteFactory, StringComparison.Ordinal);
        Assert.Contains("ai-providers-ampcode", aiRouteFactory, StringComparison.Ordinal);
        Assert.Contains("auth-files-oauth-excluded", authRouteFactory, StringComparison.Ordinal);
        Assert.Contains("auth-files-oauth-model-alias", authRouteFactory, StringComparison.Ordinal);
    }

    private static bool IsSourceFile(string path)
    {
        return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedFile(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
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
