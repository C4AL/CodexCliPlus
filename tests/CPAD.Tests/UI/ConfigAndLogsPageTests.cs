using System.Text;

namespace CPAD.Tests.UI;

public sealed class ConfigAndLogsPageTests
{
    [Fact]
    public void ConfigPageUsesNativeFormSectionsBeforeAdvancedYamlEditor()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "Views", "Pages", "ConfigPage.xaml"), Encoding.UTF8);

        var firstSectionIndex = xaml.IndexOf("ManagementFormSection", StringComparison.Ordinal);
        var editorIndex = xaml.IndexOf("ManagementCodeEditor", StringComparison.Ordinal);

        Assert.True(firstSectionIndex >= 0);
        Assert.True(editorIndex > firstSectionIndex);
        Assert.Contains("AdvancedYamlExpander", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SummaryItems", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LogsPageUsesReadOnlyLogViewerAndDoesNotOpenRequestLogsInNewWindow()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "Views", "Pages", "LogsPage.xaml"), Encoding.UTF8);
        var codeBehind = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "Views", "Pages", "LogsPage.xaml.cs"), Encoding.UTF8);

        Assert.Contains("ManagementLogViewer", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ManagementCodeEditor", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("new Window", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyRequestLogResult", codeBehind, StringComparison.Ordinal);
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
