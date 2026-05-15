using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.Security;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Tests.Security;

[Trait("Category", "LocalIntegration")]
public sealed class SensitiveConfigMigrationServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-config-migration-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task MigrateYamlAsyncExtractsSensitiveProviderFields()
    {
        var vault = new DpapiSecretVault(new TestPathService(_rootDirectory));
        var service = new SensitiveConfigMigrationService(vault);
        const string yaml = """
            api-keys:
              - "client-key-1"
            openai-compatibility:
              - name: "openrouter"
                base-url: "https://openrouter.ai/api/v1"
                headers:
                  Authorization: "Bearer upstream-token"
                api-key-entries:
                  - api-key: "sk-or-secret"
                    proxy-url: "direct"
                models:
                  - name: "moonshotai/kimi-k2"
                    alias: "kimi"
            """;

        var result = await service.MigrateYamlAsync(yaml, "backend.yaml");

        Assert.Equal(3, result.Report.TotalSecrets);
        Assert.Equal(2, result.Report.ApiKeys);
        Assert.Equal(1, result.Report.Headers);
        Assert.Contains("ccp-secret://", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("client-key-1", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("upstream-token", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-or-secret", result.Content, StringComparison.Ordinal);
        Assert.Contains("moonshotai/kimi-k2", result.Content, StringComparison.Ordinal);

        var revealed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var secret in result.Secrets)
        {
            revealed.Add(await vault.RevealSecretAsync(secret.SecretId) ?? string.Empty);
        }

        Assert.Contains("client-key-1", revealed);
        Assert.Contains("Bearer upstream-token", revealed);
        Assert.Contains("sk-or-secret", revealed);
    }

    [Fact]
    public async Task MigrateJsonAsyncExtractsOAuthTokensWithoutChangingMetadata()
    {
        var vault = new DpapiSecretVault(new TestPathService(_rootDirectory));
        var service = new SensitiveConfigMigrationService(vault);
        const string json = """
            {
              "email": "user@example.com",
              "tokens": {
                "access_token": "ya29.access",
                "refresh_token": "1//refresh"
              }
            }
            """;

        var result = await service.MigrateJsonAsync(json, "auth/codex.json");

        Assert.Equal(2, result.Report.TotalSecrets);
        Assert.Equal(2, result.Report.OAuthTokens);
        Assert.Contains("user@example.com", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("ya29.access", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("1//refresh", result.Content, StringComparison.Ordinal);
        Assert.All(result.Secrets, secret => Assert.StartsWith("sec-", secret.SecretId));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private sealed class TestPathService : IPathService
    {
        public TestPathService(string rootDirectory)
        {
            Directories = new AppDirectories(
                rootDirectory,
                Path.Combine(rootDirectory, "logs"),
                Path.Combine(rootDirectory, "config"),
                Path.Combine(rootDirectory, "backend"),
                Path.Combine(rootDirectory, "cache"),
                Path.Combine(rootDirectory, "config", "appsettings.json"),
                Path.Combine(rootDirectory, "config", "backend.yaml")
            );
        }

        public AppDirectories Directories { get; }

        public Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Directories.RootDirectory);
            Directory.CreateDirectory(Directories.LogsDirectory);
            Directory.CreateDirectory(Directories.ConfigDirectory);
            Directory.CreateDirectory(Directories.BackendDirectory);
            Directory.CreateDirectory(Directories.CacheDirectory);
            Directory.CreateDirectory(Directories.DiagnosticsDirectory);
            Directory.CreateDirectory(Directories.RuntimeDirectory);
            return Task.CompletedTask;
        }
    }
}
