using System.Text;

namespace CodexCliPlus.Tests.Backend;

[Trait("Category", "Fast")]
public sealed class BackendSourceRouteTests
{
    [Fact]
    public void BackendManagementRoutesExposeAllOAuthStartProviders()
    {
        var repositoryRoot = FindRepositoryRoot();
        var serverSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src", "BackendRuntime",
                "internal",
                "api",
                "server.go"
            ),
            Encoding.UTF8
        );

        Assert.Contains(
            """mgmt.GET("/anthropic-auth-url", s.mgmt.RequestAnthropicToken)""",
            serverSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            """mgmt.GET("/codex-auth-url", s.mgmt.RequestCodexToken)""",
            serverSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            """mgmt.GET("/gemini-cli-auth-url", s.mgmt.RequestGeminiCLIToken)""",
            serverSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            """mgmt.GET("/antigravity-auth-url", s.mgmt.RequestAntigravityToken)""",
            serverSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            """mgmt.GET("/kimi-auth-url", s.mgmt.RequestKimiToken)""",
            serverSource,
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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
