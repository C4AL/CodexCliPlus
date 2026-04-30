using System.Text;

namespace CodexCliPlus.Tests.UI;

public sealed class ManagementDesignSystemTests
{
    [Fact]
    public void AppProjectReferencesWebView2AndBuildToolPublishesGeneratedWebUiAssets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var csproj = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "CodexCliPlus.App.csproj"),
            Encoding.UTF8
        );

        Assert.Contains("Microsoft.Web.WebView2", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("resources\\webui\\upstream\\dist", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "resources\\webui\\upstream\\sync.json",
            csproj,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "CliProxyApiManagementCenter.LICENSE.txt",
            csproj,
            StringComparison.Ordinal
        );

        var buildTool = ReadBuildToolSources(repositoryRoot);
        Assert.Contains("context.WebUiAssetsRoot", buildTool, StringComparison.Ordinal);
        Assert.Contains("assets\", \"webui", buildTool, StringComparison.Ordinal);
    }

    [Fact]
    public void WebUiBuildAndDebugHostUseVendoredRepositoryDist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var viteConfig = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "vite.config.ts"
            ),
            Encoding.UTF8
        );
        var buildTool = ReadBuildToolSources(repositoryRoot);
        var locator = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Services",
                "WebUiAssetLocator.cs"
            ),
            Encoding.UTF8
        );

        Assert.Contains(
            "outDir: path.resolve(__dirname, '../dist')",
            viteConfig,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "WebUiBuildDistRoot => Path.Combine(WebUiBuildRoot, \"dist\")",
            buildTool,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "WebUiGeneratedDistRoot => Path.Combine(WebUiGeneratedRoot, \"dist\")",
            buildTool,
            StringComparison.Ordinal
        );
        Assert.True(
            locator.IndexOf("TryResolveFromBaseDirectory", StringComparison.Ordinal)
                < locator.IndexOf("TryResolveFromGeneratedAssets", StringComparison.Ordinal)
        );
        Assert.True(
            locator.IndexOf("TryResolveFromGeneratedAssets", StringComparison.Ordinal)
                < locator.IndexOf("TryResolveFromRepository", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void LegacyNativeManagementDesignSystemIsNotReferencedByRuntimeShell()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appXaml = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "App.xaml"),
            Encoding.UTF8
        );
        var appProject = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "CodexCliPlus.App.csproj"),
            Encoding.UTF8
        );
        var solution = File.ReadAllText(
            Path.Combine(repositoryRoot, "CodexCliPlus.sln"),
            Encoding.UTF8
        );

        Assert.DoesNotContain(
            "CodexCliPlus.Management.DesignSystem;component/Themes/DesignSystem.xaml",
            appXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "CodexCliPlus.Management.DesignSystem",
            appProject,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "CodexCliPlus.Management.DesignSystem",
            solution,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Views/Controls/WpfUi/FaFontIconStyle.xaml",
            appXaml,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void LegacyNativeManagementPageLayerIsRemovedFromRepository()
    {
        var repositoryRoot = FindRepositoryRoot();
        string[] removedPaths =
        [
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "Views", "Pages"),
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "ViewModels", "Pages"),
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "Services", "SecondaryRoutes"),
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.Management.DesignSystem"),
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Services",
                "ManagementNavigationService.cs"
            ),
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Services",
                "ManagementRouteCatalog.cs"
            ),
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.Core",
                "Abstractions",
                "Management",
                "IManagementNavigationService.cs"
            ),
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.Core",
                "Models",
                "Management",
                "ManagementRouteDefinition.cs"
            ),
        ];

        foreach (var removedPath in removedPaths)
        {
            Assert.False(
                Directory.Exists(removedPath) || File.Exists(removedPath),
                $"Legacy native management artifact should stay removed: {removedPath}"
            );
        }
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

    private static string ReadBuildToolSources(string repositoryRoot)
    {
        var buildToolDirectory = Path.Combine(repositoryRoot, "src", "CodexCliPlus.BuildTool");
        var sourceFiles = Directory
            .GetFiles(buildToolDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Split(Path.DirectorySeparatorChar).Contains("bin")
                && !path.Split(Path.DirectorySeparatorChar).Contains("obj")
            )
            .OrderBy(path => path, StringComparer.Ordinal);

        return string.Join(
            Environment.NewLine,
            sourceFiles.Select(path => File.ReadAllText(path, Encoding.UTF8))
        );
    }
}
