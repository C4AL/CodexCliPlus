using System.Text;

namespace CodexCliPlus.Tests.UI;

[Trait("Category", "Fast")]
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
    public void DesktopModeRestoresSessionFromBootstrapAndBrowserModeDoesNotPersistManagementKey()
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
        Assert.Contains("desktopSessionId: state.desktopSessionId", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("isDesktopMode()", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("clearBrowserManagementSession", authStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("managementKey: state.managementKey", authStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("detectApiBaseFromLocation", authStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "!isDesktopMode() && state.rememberPassword",
            authStoreSource,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ConfigPageUsesHeaderActionsInsteadOfBottomFloatingButtons()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(
            repositoryRoot,
            "resources",
            "webui",
            "upstream",
            "source",
            "src"
        );
        var configPage = File.ReadAllText(
            Path.Combine(sourceRoot, "pages", "ConfigPage.tsx"),
            Encoding.UTF8
        );
        var configStyles = File.ReadAllText(
            Path.Combine(sourceRoot, "pages", "ConfigPage.module.scss"),
            Encoding.UTF8
        );

        Assert.Contains("className={styles.headerActions}", configPage, StringComparison.Ordinal);
        Assert.Contains("onClick={handleSave}", configPage, StringComparison.Ordinal);
        Assert.Contains("useDesktopDataChanged", configPage, StringComparison.Ordinal);
        Assert.DoesNotContain("handleReload", configPage, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "t('config_management.reload')",
            configPage,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("createPortal(floatingActions", configPage, StringComparison.Ordinal);
        Assert.DoesNotContain("floatingActionContainer", configPage, StringComparison.Ordinal);
        Assert.DoesNotContain("--config-action-bar-height", configPage, StringComparison.Ordinal);
        Assert.DoesNotContain("floatingActionContainer", configStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("--config-action-bar-height", configStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthFilesImportExportUsesSacDesktopContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(
            repositoryRoot,
            "resources",
            "webui",
            "upstream",
            "source",
            "src"
        );
        var authFilesPage = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "features",
                "accountCenter",
                "components",
                "AuthFilesSection.tsx"
            ),
            Encoding.UTF8
        );
        var authFilesStyles = File.ReadAllText(
            Path.Combine(sourceRoot, "pages", "AuthFilesPage.module.scss"),
            Encoding.UTF8
        );
        var zhCn = File.ReadAllText(
            Path.Combine(sourceRoot, "i18n", "locales", "zh-CN.json"),
            Encoding.UTF8
        );

        Assert.Contains(
            "exportAccountConfigInDesktopShell('sac')",
            authFilesPage,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "exportAccountConfigInDesktopShell('json')",
            authFilesPage,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "exportSacPackageInDesktopShell",
            authFilesPage,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "codex-auth-files-export.json",
            authFilesPage,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("downloadBlob", authFilesPage, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(authFilesPage, "t('auth_files.upload_button')"));
        Assert.True(
            authFilesPage.IndexOf("t('auth_files.upload_button')", StringComparison.Ordinal)
                > authFilesPage.IndexOf(
                    "className={styles.importModalActions}",
                    StringComparison.Ordinal
                )
        );
        Assert.Contains(
            "className={styles.hiddenFileInput}",
            authFilesPage,
            StringComparison.Ordinal
        );
        Assert.Contains(".hiddenFileInput", authFilesStyles, StringComparison.Ordinal);
        Assert.Contains("\"upload_button\": \"导入认证 JSON\"", zhCn, StringComparison.Ordinal);
        Assert.Contains("\"export_config_button\": \"导出配置\"", zhCn, StringComparison.Ordinal);
        Assert.Contains(".sac 安全配置", zhCn, StringComparison.Ordinal);
        Assert.DoesNotContain("\"upload_button\": \"上传文件\"", zhCn, StringComparison.Ordinal);
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

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
