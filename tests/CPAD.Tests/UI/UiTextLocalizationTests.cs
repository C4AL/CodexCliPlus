using System.Text;

namespace CPAD.Tests.UI;

public sealed class UiTextLocalizationTests
{
    [Fact]
    public void AppSourceDoesNotContainLegacyEnglishSectionHeadings()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "CPAD.App");
        var forbiddenPhrases = new[]
        {
            "Version Information",
            "Component Sources",
            "Diagnostics Entry",
            "Editable Settings",
            "Appearance & Shell",
            "Channel Summary",
            "Log Browser",
            "Management Key",
            "Usage Summary",
            "System Status",
            "Source Switching"
        };

        var findings = FindForbiddenPhrases(appRoot, forbiddenPhrases, repositoryRoot);
        Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));
    }

    [Fact]
    public void AppSourceDoesNotContainKnownMojibakeFragments()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "CPAD.App");
        var forbiddenFragments = new[]
        {
            "鍒锋柊",
            "姝ｅ湪",
            "绠＄悊",
            "鏈繛鎺",
            "鍚姩",
            "閲嶅惎",
            "妗岄潰",
            "绯荤粺",
            "鏃ュ織",
            "鐢ㄩ噺"
        };

        var findings = FindForbiddenPhrases(appRoot, forbiddenFragments, repositoryRoot);
        Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));
    }

    private static List<string> FindForbiddenPhrases(string root, IEnumerable<string> phrases, string repositoryRoot)
    {
        var files = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var findings = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file, Encoding.UTF8);
            foreach (var phrase in phrases)
            {
                if (text.Contains(phrase, StringComparison.Ordinal))
                {
                    findings.Add($"{Path.GetRelativePath(repositoryRoot, file)} => {phrase}");
                }
            }
        }

        return findings;
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
