using CodexCliPlus.Core.Models.Security;
using CodexCliPlus.Services;

namespace CodexCliPlus.Tests.Security;

public sealed class SecureAccountPackageServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-sac-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task WriteEncryptedPackageAsyncCreatesSacV2WithoutPlaintextSecrets()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "config.sac");
        var payload = CreatePayload();

        await SecureAccountPackageService.WriteEncryptedPackageAsync(
            payload,
            "passphrase",
            packagePath
        );
        var rawPackage = await File.ReadAllTextAsync(packagePath);
        var imported = await SecureAccountPackageService.ReadEncryptedPackageAsync(
            packagePath,
            "passphrase"
        );

        Assert.Contains("\"version\": 2", rawPackage, StringComparison.Ordinal);
        Assert.Contains("\"secretCount\": \"1\"", rawPackage, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-vault-secret", rawPackage, StringComparison.Ordinal);
        Assert.Equal(2, imported.Version);
        Assert.Equal("CodexCliPlus.AccountMigrationPackage", imported.Format);
        Assert.Single(imported.VaultSecrets);
        Assert.Equal("sk-vault-secret", imported.VaultSecrets[0].Value);
        Assert.Contains("ccp-secret://sec-exported", imported.ConfigYaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadEncryptedPackageAsyncRejectsWrongPasswordAndExpiredPackage()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "config.sac");
        var payload = CreatePayload();

        await SecureAccountPackageService.WriteEncryptedPackageAsync(
            payload,
            "passphrase",
            packagePath
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SecureAccountPackageService.ReadEncryptedPackageAsync(packagePath, "wrong")
        );

        var expiredPath = Path.Combine(_rootDirectory, "expired.sac");
        var expiredPayload = CreatePayload();
        expiredPayload.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        await SecureAccountPackageService.WriteEncryptedPackageAsync(
            expiredPayload,
            "passphrase",
            expiredPath
        );

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SecureAccountPackageService.ReadEncryptedPackageAsync(expiredPath, "passphrase")
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private static SecureAccountPackagePayload CreatePayload()
    {
        return new SecureAccountPackagePayload
        {
            ConfigYaml = "api-keys:\n  - \"ccp-secret://sec-exported\"\n",
            VaultSecrets =
            [
                new VaultSecretExport
                {
                    SecretId = "sec-exported",
                    Kind = SecretKind.ApiKey,
                    Source = "backend.yaml",
                    Value = "sk-vault-secret",
                    Metadata = new Dictionary<string, string> { ["path"] = "$.api-keys[0]" },
                },
            ],
        };
    }
}
