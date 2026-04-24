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

    [Fact]
    public void DesktopWebUiChineseBrandingUsesCpadAndHidesWebLoginConnectionHints()
    {
        var repositoryRoot = FindRepositoryRoot();
        var zhCn = File.ReadAllText(Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "i18n", "locales", "zh-CN.json"), Encoding.UTF8);
        var loginPage = File.ReadAllText(Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "pages", "LoginPage.tsx"), Encoding.UTF8);
        var mainLayout = File.ReadAllText(Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "components", "layout", "MainLayout.tsx"), Encoding.UTF8);
        var systemPage = File.ReadAllText(Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "pages", "SystemPage.tsx"), Encoding.UTF8);
        var splashScreen = File.ReadAllText(Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "components", "common", "SplashScreen.tsx"), Encoding.UTF8);
        var mainEntry = File.ReadAllText(Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src", "main.tsx"), Encoding.UTF8);
        var htmlEntry = File.ReadAllText(Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "index.html"), Encoding.UTF8);
        var visibleText = string.Join(
            Environment.NewLine,
            zhCn,
            loginPage,
            mainLayout,
            systemPage,
            splashScreen,
            mainEntry,
            htmlEntry);

        Assert.Contains("\"main\": \"CPAD\"", zhCn, StringComparison.Ordinal);
        Assert.Contains("\"desktop_subtitle\"", zhCn, StringComparison.Ordinal);
        Assert.Contains("!desktopMode &&", loginPage, StringComparison.Ordinal);

        var forbiddenPhrases = new[]
        {
            "CLI Proxy API Management Center",
            "CPAMC",
            "CPA-UV",
            "CPA 版本",
            "CPA（CLI Proxy API）",
            "WebUI 仓库"
        };

        foreach (var phrase in forbiddenPhrases)
        {
            Assert.DoesNotContain(phrase, visibleText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DesktopWebUiKeepsOnlySimplifiedChineseLanguageSurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src");
        var commonTypes = File.ReadAllText(Path.Combine(sourceRoot, "types", "common.ts"), Encoding.UTF8);
        var constants = File.ReadAllText(Path.Combine(sourceRoot, "utils", "constants.ts"), Encoding.UTF8);
        var i18n = File.ReadAllText(Path.Combine(sourceRoot, "i18n", "index.ts"), Encoding.UTF8);
        var mainLayout = File.ReadAllText(Path.Combine(sourceRoot, "components", "layout", "MainLayout.tsx"), Encoding.UTF8);

        Assert.Contains("export type Language = 'zh-CN';", commonTypes, StringComparison.Ordinal);
        Assert.Contains("defineLanguageOrder(['zh-CN'] as const)", constants, StringComparison.Ordinal);
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
        var sourceRoot = Path.Combine(repositoryRoot, "resources", "webui", "upstream", "source", "src");
        var routeSource = File.ReadAllText(Path.Combine(sourceRoot, "router", "MainRoutes.tsx"), Encoding.UTF8);
        var providersPage = File.ReadAllText(Path.Combine(sourceRoot, "pages", "AiProvidersPage.tsx"), Encoding.UTF8);
        var providerNav = File.ReadAllText(Path.Combine(sourceRoot, "components", "providers", "ProviderNav", "ProviderNav.tsx"), Encoding.UTF8);
        var oauthPage = File.ReadAllText(Path.Combine(sourceRoot, "pages", "OAuthPage.tsx"), Encoding.UTF8);
        var quotaPage = File.ReadAllText(Path.Combine(sourceRoot, "pages", "QuotaPage.tsx"), Encoding.UTF8);
        var authFilesPage = File.ReadAllText(Path.Combine(sourceRoot, "pages", "AuthFilesPage.tsx"), Encoding.UTF8);

        Assert.Contains("AiProvidersCodexEditPage", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AiProvidersGeminiEditPage", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AiProvidersClaude", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AiProvidersOpenAI", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AiProvidersVertex", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AiProvidersAmpcode", routeSource, StringComparison.Ordinal);

        Assert.Contains("CodexSection", providersPage, StringComparison.Ordinal);
        Assert.DoesNotContain("GeminiSection", providersPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaudeSection", providersPage, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAISection", providersPage, StringComparison.Ordinal);
        Assert.DoesNotContain("VertexSection", providersPage, StringComparison.Ordinal);
        Assert.DoesNotContain("AmpcodeSection", providersPage, StringComparison.Ordinal);

        Assert.Contains("ProviderId = 'codex'", providerNav, StringComparison.Ordinal);
        Assert.DoesNotContain("'gemini'", providerNav, StringComparison.Ordinal);
        Assert.DoesNotContain("'claude'", providerNav, StringComparison.Ordinal);
        Assert.DoesNotContain("'openai'", providerNav, StringComparison.Ordinal);
        Assert.DoesNotContain("'vertex'", providerNav, StringComparison.Ordinal);
        Assert.DoesNotContain("'ampcode'", providerNav, StringComparison.Ordinal);

        Assert.Contains("id: 'codex'", oauthPage, StringComparison.Ordinal);
        Assert.DoesNotContain("anthropic", oauthPage, StringComparison.Ordinal);
        Assert.DoesNotContain("gemini-cli", oauthPage, StringComparison.Ordinal);
        Assert.DoesNotContain("kimi", oauthPage, StringComparison.Ordinal);
        Assert.DoesNotContain("iconVertex", oauthPage, StringComparison.Ordinal);

        Assert.Contains("CODEX_CONFIG", quotaPage, StringComparison.Ordinal);
        Assert.DoesNotContain("CLAUDE_CONFIG", quotaPage, StringComparison.Ordinal);
        Assert.DoesNotContain("GEMINI_CLI_CONFIG", quotaPage, StringComparison.Ordinal);
        Assert.Contains("CODEX_PROVIDER_FILTER = 'codex'", authFilesPage, StringComparison.Ordinal);
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
