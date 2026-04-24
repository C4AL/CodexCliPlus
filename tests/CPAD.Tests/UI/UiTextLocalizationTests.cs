using System.Text;

namespace CPAD.Tests.UI;

public sealed class UiTextLocalizationTests
{
    [Fact]
    public void DesktopShellDoesNotReintroduceLegacyEnglishTrayLabels()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainWindowXaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "MainWindow.xaml"), Encoding.UTF8);
        var forbiddenPhrases = new[]
        {
            "Open Main Interface",
            "Restart Backend",
            "Check Updates",
            "Exit and Stop Backend"
        };

        foreach (var phrase in forbiddenPhrases)
        {
            Assert.DoesNotContain(phrase, mainWindowXaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DesktopShellTitleUsesChineseDisplayText()
    {
        var repositoryRoot = FindRepositoryRoot();
        var viewModelSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "CPAD.App", "ViewModels", "MainWindowViewModel.cs"), Encoding.UTF8);

        Assert.Contains("CPAD \u684c\u9762\u7248", viewModelSource, StringComparison.Ordinal);
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
