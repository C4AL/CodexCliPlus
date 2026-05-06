using System.Text;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.Security;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Tests.Security;

[Trait("Category", "LocalIntegration")]
public sealed class SecretVaultTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-secret-vault-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task SaveRevealAndRevokeAsyncKeepsPlaintextOutOfVaultFiles()
    {
        var pathService = new TestPathService(_rootDirectory);
        var vault = new DpapiSecretVault(pathService);

        var record = await vault.SaveSecretAsync(SecretKind.ApiKey, "sk-live-secret", "unit-test");
        var revealed = await vault.RevealSecretAsync(record.SecretId);
        var manifestPath = Path.Combine(
            pathService.Directories.ConfigDirectory,
            "secrets",
            "vault-manifest.json"
        );
        var blobPath = Path.Combine(
            pathService.Directories.ConfigDirectory,
            "secrets",
            "vault",
            $"{record.SecretId}.bin"
        );

        Assert.Equal("sk-live-secret", revealed);
        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(blobPath));
        Assert.DoesNotContain("sk-live-secret", await File.ReadAllTextAsync(manifestPath));
        Assert.False(
            ContainsBytes(await File.ReadAllBytesAsync(blobPath), "sk-live-secret"u8.ToArray())
        );

        await vault.SetSecretStatusAsync(record.SecretId, SecretStatus.Revoked);

        Assert.Null(await vault.RevealSecretAsync(record.SecretId));
        Assert.Equal(SecretStatus.Revoked, (await vault.GetSecretAsync(record.SecretId))?.Status);
    }

    [Fact]
    public async Task SaveSecretAsyncCanPreserveImportedSecretId()
    {
        var vault = new DpapiSecretVault(new TestPathService(_rootDirectory));

        var record = await vault.SaveSecretAsync(
            SecretKind.OAuthRefreshToken,
            "refresh-token",
            "sac-import",
            secretId: "sec-imported-refresh"
        );

        Assert.Equal("sec-imported-refresh", record.SecretId);
        Assert.Equal(
            "refresh-token",
            await vault.RevealSecretAsync("ccp-secret://sec-imported-refresh")
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
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
