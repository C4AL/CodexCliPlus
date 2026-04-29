using System.Text;

namespace CodexCliPlus.Tests.UI;

public sealed class ConfigAndLogsPageTests
{
    [Fact]
    public void DesktopModeBlocksManagementKeyPersistence()
    {
        var repositoryRoot = FindRepositoryRoot();
        var storageSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "services",
                "storage",
                "secureStorage.ts"
            ),
            Encoding.UTF8
        );

        Assert.Contains(
            "isDesktopMode() && key === 'managementKey'",
            storageSource,
            StringComparison.Ordinal
        );
        Assert.Contains("localStorage.removeItem(key);", storageSource, StringComparison.Ordinal);
        Assert.Contains("return null;", storageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopModeRestoresSessionFromBootstrapWithoutRememberPassword()
    {
        var repositoryRoot = FindRepositoryRoot();
        var authStoreSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "stores",
                "useAuthStore.ts"
            ),
            Encoding.UTF8
        );

        Assert.Contains(
            "const desktopBootstrap = consumeDesktopBootstrap();",
            authStoreSource,
            StringComparison.Ordinal
        );
        Assert.Contains("rememberPassword: false", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("isDesktopMode()", authStoreSource, StringComparison.Ordinal);
        Assert.Contains(
            "state.rememberPassword ? { managementKey: state.managementKey } : {}",
            authStoreSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "!isDesktopMode() && state.rememberPassword",
            authStoreSource,
            StringComparison.Ordinal
        );
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
}
