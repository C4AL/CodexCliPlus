using System.Text;
using System.Text.Json;

namespace CodexCliPlus.Tests.UI;

public sealed class UiTextLocalizationTests
{
    [Fact]
    public void DesktopShellDoesNotReintroduceLegacyEnglishTrayLabels()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainWindowXaml = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml"),
            Encoding.UTF8
        );
        var forbiddenPhrases = new[]
        {
            "Open Main Interface",
            "Restart Backend",
            "Check Updates",
            "Exit and Stop Backend",
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
        var viewModelSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "ViewModels",
                "MainWindowViewModel.cs"
            ),
            Encoding.UTF8
        );

        Assert.Contains("AppConstants.DisplayName", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("\u684c\u9762\u7248", viewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopWebUiChineseBrandingUsesCodexCliPlusAndHidesWebLoginConnectionHints()
    {
        var repositoryRoot = FindRepositoryRoot();
        var zhCn = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "i18n",
                "locales",
                "zh-CN.json"
            ),
            Encoding.UTF8
        );
        var loginPage = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "pages",
                "LoginPage.tsx"
            ),
            Encoding.UTF8
        );
        var mainLayout = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "components",
                "layout",
                "MainLayout.tsx"
            ),
            Encoding.UTF8
        );
        var systemPage = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "pages",
                "SystemPage.tsx"
            ),
            Encoding.UTF8
        );
        var mainEntry = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "main.tsx"
            ),
            Encoding.UTF8
        );
        var htmlEntry = File.ReadAllText(
            Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "index.html"),
            Encoding.UTF8
        );
        var visibleText = string.Join(
            Environment.NewLine,
            zhCn,
            loginPage,
            mainLayout,
            systemPage,
            mainEntry,
            htmlEntry
        );

        Assert.Contains("\"main\": \"CodexCliPlus\"", zhCn, StringComparison.Ordinal);
        Assert.Contains("CodexCliPlus", visibleText, StringComparison.Ordinal);
        Assert.Contains("账号中心", visibleText, StringComparison.Ordinal);
        Assert.Contains("\"desktop_subtitle\"", zhCn, StringComparison.Ordinal);
        Assert.Contains("!desktopMode &&", loginPage, StringComparison.Ordinal);

        var forbiddenPhrases = new[]
        {
            "CLI Proxy API Management Center",
            "CPAMC",
            "CPA-UV",
            "CPA 版本",
            "CPA（CLI Proxy API）",
            "WebUI 仓库",
        };

        foreach (var phrase in forbiddenPhrases)
        {
            Assert.DoesNotContain(phrase, visibleText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DesktopWebUiDashboardOverviewTranslationsUseChineseLabels()
    {
        var repositoryRoot = FindRepositoryRoot();
        var zhCn = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "i18n",
                "locales",
                "zh-CN.json"
            ),
            Encoding.UTF8
        );

        using var document = JsonDocument.Parse(zhCn);
        var root = document.RootElement;
        var nav = root.GetProperty("nav");
        var dashboard = root.GetProperty("dashboard");

        Assert.Equal("操作台", nav.GetProperty("dashboard_overview").GetString());
        Assert.Equal("操作台", nav.GetProperty("console").GetString());
        Assert.Equal("账号统计", dashboard.GetProperty("account_stats").GetString());
        Assert.Equal("暂无可用账号", dashboard.GetProperty("no_codex_accounts").GetString());
        Assert.DoesNotContain("运行概览", zhCn, StringComparison.Ordinal);
        Assert.DoesNotContain("dashboard.account_stats", zhCn, StringComparison.Ordinal);
        Assert.DoesNotContain("dashboard.no_codex_accounts", zhCn, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopWebUiKeepsOnlySimplifiedChineseLanguageSurface()
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
        var commonTypes = File.ReadAllText(
            Path.Combine(sourceRoot, "types", "common.ts"),
            Encoding.UTF8
        );
        var constants = File.ReadAllText(
            Path.Combine(sourceRoot, "utils", "constants.ts"),
            Encoding.UTF8
        );
        var i18n = File.ReadAllText(Path.Combine(sourceRoot, "i18n", "index.ts"), Encoding.UTF8);
        var mainLayout = File.ReadAllText(
            Path.Combine(sourceRoot, "components", "layout", "MainLayout.tsx"),
            Encoding.UTF8
        );

        Assert.Contains("export type Language = 'zh-CN';", commonTypes, StringComparison.Ordinal);
        Assert.Contains(
            "defineLanguageOrder(['zh-CN'] as const)",
            constants,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("zh-TW", i18n, StringComparison.Ordinal);
        Assert.DoesNotContain("from './locales/en.json'", i18n, StringComparison.Ordinal);
        Assert.DoesNotContain("language-menu", mainLayout, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(sourceRoot, "i18n", "locales", "en.json")));
        Assert.False(File.Exists(Path.Combine(sourceRoot, "i18n", "locales", "ru.json")));
        Assert.False(File.Exists(Path.Combine(sourceRoot, "i18n", "locales", "zh-TW.json")));
    }

    [Fact]
    public void DesktopWebUiVisibleProviderSurfaceIsCodexOnly()
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
        var routeSource = File.ReadAllText(
            Path.Combine(sourceRoot, "router", "MainRoutes.tsx"),
            Encoding.UTF8
        );
        var accountCenterPage = File.ReadAllText(
            Path.Combine(sourceRoot, "pages", "AccountCenterPage.tsx"),
            Encoding.UTF8
        );
        var providersSection = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "features",
                "accountCenter",
                "components",
                "CodexConfigurationsSection.tsx"
            ),
            Encoding.UTF8
        );
        var oauthSection = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "features",
                "accountCenter",
                "components",
                "OAuthLoginSection.tsx"
            ),
            Encoding.UTF8
        );
        var quotaSection = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "features",
                "accountCenter",
                "components",
                "QuotaManagementSection.tsx"
            ),
            Encoding.UTF8
        );
        var authFilesSection = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "features",
                "accountCenter",
                "components",
                "AuthFilesSection.tsx"
            ),
            Encoding.UTF8
        );

        Assert.Contains("AccountCenterPage", routeSource, StringComparison.Ordinal);
        Assert.Contains("path: '/accounts'", routeSource, StringComparison.Ordinal);
        Assert.Contains(
            "Navigate to=\"/accounts#codex-config\"",
            routeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Navigate to=\"/accounts#auth-files\"",
            routeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Navigate to=\"/accounts#quota-management\"",
            routeSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("AiProvidersCodexEditPage", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthFilesOAuthExcludedEditPage", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthFilesOAuthModelAliasEditPage", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("element: <OAuthPage", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AiProvidersGeminiEditPage", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AiProvidersClaude", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AiProvidersOpenAI", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AiProvidersVertex", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AiProvidersAmpcode", routeSource, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(sourceRoot, "pages", "AiProvidersGeminiEditPage.tsx"))
        );
        Assert.False(
            File.Exists(Path.Combine(sourceRoot, "pages", "AiProvidersClaudeEditPage.tsx"))
        );
        Assert.False(
            File.Exists(Path.Combine(sourceRoot, "pages", "AiProvidersOpenAIEditPage.tsx"))
        );
        Assert.False(
            File.Exists(Path.Combine(sourceRoot, "pages", "AiProvidersVertexEditPage.tsx"))
        );
        Assert.False(
            File.Exists(Path.Combine(sourceRoot, "pages", "AiProvidersAmpcodeEditPage.tsx"))
        );
        Assert.False(File.Exists(Path.Combine(sourceRoot, "pages", "AiProvidersPage.tsx")));
        Assert.False(File.Exists(Path.Combine(sourceRoot, "pages", "AiProvidersCodexEditPage.tsx")));
        Assert.False(File.Exists(Path.Combine(sourceRoot, "pages", "AuthFilesPage.tsx")));
        Assert.False(File.Exists(Path.Combine(sourceRoot, "pages", "QuotaPage.tsx")));
        Assert.False(File.Exists(Path.Combine(sourceRoot, "pages", "OAuthPage.tsx")));
        Assert.False(
            File.Exists(Path.Combine(sourceRoot, "pages", "AuthFilesOAuthExcludedEditPage.tsx"))
        );
        Assert.False(
            File.Exists(Path.Combine(sourceRoot, "pages", "AuthFilesOAuthModelAliasEditPage.tsx"))
        );

        Assert.Contains("OAuthLoginSection", accountCenterPage, StringComparison.Ordinal);
        Assert.Contains("CodexConfigurationsSection", accountCenterPage, StringComparison.Ordinal);
        Assert.Contains("AuthFilesSection", accountCenterPage, StringComparison.Ordinal);
        Assert.Contains("QuotaManagementSection", accountCenterPage, StringComparison.Ordinal);
        Assert.Contains("SECTION_IDS.oauth", accountCenterPage, StringComparison.Ordinal);
        Assert.Contains("SECTION_IDS.codex", accountCenterPage, StringComparison.Ordinal);
        Assert.Contains("SECTION_IDS.authFiles", accountCenterPage, StringComparison.Ordinal);
        Assert.Contains("SECTION_IDS.quota", accountCenterPage, StringComparison.Ordinal);

        Assert.Contains("CodexSection", providersSection, StringComparison.Ordinal);
        Assert.DoesNotContain("GeminiSection", providersSection, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaudeSection", providersSection, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAISection", providersSection, StringComparison.Ordinal);
        Assert.DoesNotContain("VertexSection", providersSection, StringComparison.Ordinal);
        Assert.DoesNotContain("AmpcodeSection", providersSection, StringComparison.Ordinal);

        Assert.Contains("id: 'codex'", oauthSection, StringComparison.Ordinal);
        Assert.DoesNotContain("anthropic", oauthSection, StringComparison.Ordinal);
        Assert.DoesNotContain("gemini-cli", oauthSection, StringComparison.Ordinal);
        Assert.DoesNotContain("kimi", oauthSection, StringComparison.Ordinal);
        Assert.DoesNotContain("iconVertex", oauthSection, StringComparison.Ordinal);

        Assert.Contains("CODEX_CONFIG", quotaSection, StringComparison.Ordinal);
        Assert.DoesNotContain("CLAUDE_CONFIG", quotaSection, StringComparison.Ordinal);
        Assert.DoesNotContain("GEMINI_CLI_CONFIG", quotaSection, StringComparison.Ordinal);
        Assert.Contains("CODEX_PROVIDER_FILTER = 'codex'", authFilesSection, StringComparison.Ordinal);
        Assert.DoesNotContain("import_cpa", authFilesSection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallerFailureHandlingUsesChinesePromptAndSkipsRawAclStackTraceDialog()
    {
        var repositoryRoot = FindRepositoryRoot();
        var securityHelper = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "build",
                "micasetup",
                "source-template",
                "Helper",
                "System",
                "SecurityControlHelper.cs"
            ),
            Encoding.UTF8
        );
        var installViewModelTemplate = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "build",
                "micasetup",
                "overrides",
                "MicaSetup",
                "ViewModels",
                "Inst",
                "InstallViewModel.cs.template"
            ),
            Encoding.UTF8
        );

        Assert.Contains("安装失败", installViewModelTemplate, StringComparison.Ordinal);
        Assert.Contains("ShowInstallFailure", installViewModelTemplate, StringComparison.Ordinal);
        Assert.Contains("fileInfo.Exists", securityHelper, StringComparison.Ordinal);
        Assert.Contains("dir.Exists", securityHelper, StringComparison.Ordinal);
        Assert.Contains("WellKnownSidType.WorldSid", securityHelper, StringComparison.Ordinal);
        Assert.Contains("WellKnownSidType.BuiltinUsersSid", securityHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("Allow Full File Security Error", securityHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("Allow Full Folder Security Error", securityHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox.Show", securityHelper, StringComparison.Ordinal);
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
