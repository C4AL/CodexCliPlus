using System.Text.RegularExpressions;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Tests.Security;

[Trait("Category", "LocalIntegration")]
public sealed class AccountConfigImportGuardTests
{
    [Fact]
    public void PreserveLocalRuntimeSettingsKeepsCurrentManagementSecret()
    {
        const string localManagementKey = "local-management-key";
        var localHash = BCrypt.Net.BCrypt.HashPassword(localManagementKey);
        var packageHash = BCrypt.Net.BCrypt.HashPassword("package-management-key");
        var currentYaml =
            $$"""
            host: "127.0.0.1"
            port: 8317
            remote-management:
              allow-remote: false
              secret-key: "{{localHash}}"
              disable-control-panel: true
            auth-dir: "C:\\Users\\Reol\\AppData\\Local\\CodexCliPlus\\backend\\auth"
            api-keys:
              - "sk-local"
            """;
        var importedYaml =
            $$"""
            host: "0.0.0.0"
            port: 9327
            remote-management:
              allow-remote: true
              secret-key: "{{packageHash}}"
              disable-control-panel: false
            auth-dir: "D:\\imported\\auth"
            api-keys:
              - "ccp-secret://imported-api-key"
            oauth-excluded-models:
              codex:
                - "o1-*"
            """;

        var guardedYaml = AccountConfigImportGuard.PreserveLocalRuntimeSettings(
            importedYaml,
            currentYaml
        );

        Assert.Contains("host: \"127.0.0.1\"", guardedYaml, StringComparison.Ordinal);
        Assert.Contains("port: 8317", guardedYaml, StringComparison.Ordinal);
        Assert.Contains("allow-remote: false", guardedYaml, StringComparison.Ordinal);
        var secretKeyMatch = Regex.Match(
            guardedYaml,
            "(?m)^\\s*secret-key:\\s*[\"']?(?<hash>\\$2[aby]\\$.+?)[\"']?\\s*$"
        );

        Assert.True(secretKeyMatch.Success);
        Assert.True(BCrypt.Net.BCrypt.Verify(localManagementKey, secretKeyMatch.Groups["hash"].Value));
        Assert.Contains("disable-control-panel: true", guardedYaml, StringComparison.Ordinal);
        Assert.Contains("CodexCliPlus\\\\backend\\\\auth", guardedYaml, StringComparison.Ordinal);
        Assert.Contains("ccp-secret://imported-api-key", guardedYaml, StringComparison.Ordinal);
        Assert.Contains("oauth-excluded-models:", guardedYaml, StringComparison.Ordinal);
        Assert.DoesNotContain(packageHash, guardedYaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PreserveLocalRuntimeSettingsRejectsUnparseableCurrentConfig()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AccountConfigImportGuard.PreserveLocalRuntimeSettings(
                "api-keys:\n  - \"ccp-secret://imported\"",
                "remote-management: [broken"
            )
        );

        Assert.Contains("无法解析", exception.Message, StringComparison.Ordinal);
    }
}
