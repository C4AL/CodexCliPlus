using System.Text;

namespace CPAD.Tests.UI;

public sealed class ManagementDesignSystemTests
{
    [Fact]
    public void AppReferencesLocalDesignSystemProjectAndMergedResources()
    {
        var repositoryRoot = FindRepositoryRoot();
        var csproj = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "CPAD.App.csproj"), Encoding.UTF8);
        var appXaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "App.xaml"), Encoding.UTF8);

        Assert.Contains("CPAD.Management.DesignSystem.csproj", csproj, StringComparison.Ordinal);
        Assert.Contains("CPAD.Management.DesignSystem;component/Themes/DesignSystem.xaml", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignSystemContainsRequiredWrappedControls()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controlsRoot = Path.Combine(repositoryRoot, "src", "CPAD.Management.DesignSystem", "Controls");
        var expectedFiles = new[]
        {
            "ManagementTabs.cs",
            "SecondaryPageShell.xaml",
            "FloatingActionBar.xaml",
            "BatchActionBar.xaml",
            "FilterToolbar.xaml",
            "StatusBadge.xaml",
            "DiffDialog.xaml",
            "ProviderCard.xaml",
            "AuthFileCard.xaml",
            "ManagementFormSection.xaml",
            "ManagementFieldRow.xaml",
            "EditableKeyValueList.xaml",
            "EditableStringList.xaml",
            "EditableModelAliasList.xaml",
            "ManagementEmptyState.xaml",
            "ManagementConfirmDialog.xaml",
            "ManagementLogViewer.xaml"
        };

        foreach (var file in expectedFiles)
        {
            Assert.True(File.Exists(Path.Combine(controlsRoot, file)), file);
        }
    }

    [Fact]
    public void BusinessPagesReferenceOnlyLocalDesignSystemNamespace()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pagesRoot = Path.Combine(repositoryRoot, "src", "CPAD.App", "Views", "Pages");
        var files = Directory.EnumerateFiles(pagesRoot, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var requiredNamespace = "CPAD.Management.DesignSystem";
        var forbiddenTokens = new[]
        {
            "ICSharpCode.AvalonEdit",
            "LiveChartsCore",
            "HandyControl",
            "AvalonEdit",
            "CartesianChart"
        };

        var missingNamespace = new List<string>();
        var findings = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file, Encoding.UTF8);
            if (file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) &&
                !text.Contains(requiredNamespace, StringComparison.Ordinal) &&
                !Path.GetFileName(file).Equals("ManagementPageSupport.cs", StringComparison.OrdinalIgnoreCase))
            {
                missingNamespace.Add(Path.GetRelativePath(repositoryRoot, file));
            }

            foreach (var token in forbiddenTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    findings.Add($"{Path.GetRelativePath(repositoryRoot, file)} => {token}");
                }
            }
        }

        Assert.True(missingNamespace.Count == 0, string.Join(Environment.NewLine, missingNamespace));
        Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));
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
