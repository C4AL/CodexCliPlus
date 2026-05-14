using System.Text;

namespace CodexCliPlus.Tests;

[Trait("Category", "Fast")]
public sealed class AppInstallerCleanupModeTests
{
    [Fact]
    public void ParseRequestDecodesInstallerTargetAndParentProcess()
    {
        var targetPath = Path.Combine(
            Path.GetTempPath(),
            "CodexCliPlus.Setup.Offline.9.9.9.exe"
        );

        var request = InstallerCleanupMode.ParseRequest(
            [
                "--" + InstallerCleanupMode.ModeArgument,
                "--"
                    + InstallerCleanupMode.TargetArgument
                    + "="
                    + InstallerCleanupMode.EncodeArgument(targetPath),
                "--" + InstallerCleanupMode.ParentProcessArgument + "=12345",
            ]
        );

        Assert.Equal(Path.GetFullPath(targetPath), request.TargetPath);
        Assert.Equal(12345, request.ParentProcessId);
    }

    [Fact]
    public void AppRunsInstallerCleanupModeBeforeDesktopStartup()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "App.xaml.cs"),
            Encoding.UTF8
        );

        Assert.Contains("InstallerCleanupMode.TryRun(e.Args", appSource, StringComparison.Ordinal);
        AssertSourceOrder(
            appSource,
            "InstallerCleanupMode.TryRun(e.Args",
            "var services = new ServiceCollection();"
        );
        AssertSourceOrder(
            appSource,
            "InstallerCleanupMode.TryRun(e.Args",
            "TryParseRepairMode(e.Args"
        );
        AssertSourceOrder(
            appSource,
            "InstallerCleanupMode.TryRun(e.Args",
            "TryAcquireSingleInstance()"
        );
    }

    [Fact]
    public void InstallerCleanupModeValidatesTargetAndUsesNativeDeleteFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var cleanupSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "InstallerCleanupMode.cs"
            ),
            Encoding.UTF8
        );

        Assert.Contains("FileVersionInfo.GetVersionInfo(targetPath)", cleanupSource, StringComparison.Ordinal);
        Assert.Contains("ProductName, \"CodexCliPlus\"", cleanupSource, StringComparison.Ordinal);
        Assert.Contains("ContainsInstallerMarker(versionInfo.FileDescription)", cleanupSource, StringComparison.Ordinal);
        Assert.Contains("File.Delete(targetPath);", cleanupSource, StringComparison.Ordinal);
        Assert.Contains("MOVEFILE_DELAY_UNTIL_REBOOT", cleanupSource, StringComparison.Ordinal);
        Assert.DoesNotContain("cmd.exe", cleanupSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell.exe", cleanupSource, StringComparison.OrdinalIgnoreCase);
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

    private static void AssertSourceOrder(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);

        Assert.True(firstIndex >= 0, $"Expected to find '{first}'.");
        Assert.True(secondIndex >= 0, $"Expected to find '{second}'.");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' to appear before '{second}'.");
    }
}
