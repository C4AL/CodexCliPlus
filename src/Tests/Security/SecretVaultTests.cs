using System.Text;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Exceptions;
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
            record.BlobReference
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

    [Fact]
    public async Task ListSecretsAsyncNormalizesNullManifestCollections()
    {
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        var manifestRoot = Path.Combine(
            pathService.Directories.ConfigDirectory,
            "secrets"
        );
        Directory.CreateDirectory(manifestRoot);
        var manifestPath = Path.Combine(manifestRoot, "vault-manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            """
            {
              "version": 2,
              "secrets": null
            }
            """
        );
        var vault = new DpapiSecretVault(pathService);

        Assert.Empty(await vault.ListSecretsAsync());

        await File.WriteAllTextAsync(
            manifestPath,
            """
            {
              "version": 2,
              "secrets": [
                null,
                {
                  "secretId": "sec-valid",
                  "kind": "ApiKey",
                  "source": "unit-test",
                  "createdAtUtc": "2026-01-01T00:00:00Z",
                  "status": "Active",
                  "blobReference": "sec-valid.bin",
                  "metadata": null
                }
              ]
            }
            """
        );

        var records = await vault.ListSecretsAsync();
        var record = Assert.Single(records);
        Assert.Equal("sec-valid", record.SecretId);

        await vault.SetSecretStatusAsync("sec-valid", SecretStatus.Revoked);

        Assert.Equal(SecretStatus.Revoked, (await vault.GetSecretAsync("sec-valid"))?.Status);
    }

    [Fact]
    public async Task ListSecretsAsyncIsolatesInvalidManifestAndAllowsFutureWrites()
    {
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        var manifestRoot = Path.Combine(
            pathService.Directories.ConfigDirectory,
            "secrets"
        );
        Directory.CreateDirectory(manifestRoot);
        var manifestPath = Path.Combine(manifestRoot, "vault-manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{ not-json");
        var vault = new DpapiSecretVault(pathService);

        Assert.Empty(await vault.ListSecretsAsync());
        Assert.False(File.Exists(manifestPath));
        Assert.Single(
            Directory.EnumerateFiles(manifestRoot, "vault-manifest.json.invalid.*")
        );

        var record = await vault.SaveSecretAsync(
            SecretKind.ApiKey,
            "sk-after-corrupt-manifest",
            "unit-test"
        );

        Assert.True(File.Exists(manifestPath));
        Assert.Equal(
            "sk-after-corrupt-manifest",
            await vault.RevealSecretAsync(record.SecretId)
        );
        Assert.Single(
            Directory.EnumerateFiles(manifestRoot, "vault-manifest.json.invalid.*")
        );
    }

    [Fact]
    public async Task RevealSecretAsyncReturnsValueWhenLastUsedUpdateFails()
    {
        var pathService = new TestPathService(_rootDirectory);
        var vault = new DpapiSecretVault(pathService);
        var record = await vault.SaveSecretAsync(
            SecretKind.ApiKey,
            "sk-read-secret",
            "unit-test"
        );
        var manifestPath = Path.Combine(
            pathService.Directories.ConfigDirectory,
            "secrets",
            "vault-manifest.json"
        );
        File.SetAttributes(
            manifestPath,
            File.GetAttributes(manifestPath) | FileAttributes.ReadOnly
        );

        try
        {
            Assert.Equal("sk-read-secret", await vault.RevealSecretAsync(record.SecretId));
        }
        finally
        {
            if (File.Exists(manifestPath))
            {
                File.SetAttributes(manifestPath, FileAttributes.Normal);
            }
        }

        Assert.Null((await vault.GetSecretAsync(record.SecretId))?.LastUsedAtUtc);
    }

    [Fact]
    public async Task DeleteSecretAsyncKeepsBlobWhenManifestRewriteFails()
    {
        var pathService = new TestPathService(_rootDirectory);
        var vault = new DpapiSecretVault(pathService);
        var record = await vault.SaveSecretAsync(
            SecretKind.ApiKey,
            "sk-delete-secret",
            "unit-test"
        );
        var manifestPath = Path.Combine(
            pathService.Directories.ConfigDirectory,
            "secrets",
            "vault-manifest.json"
        );
        var blobPath = Path.Combine(
            pathService.Directories.ConfigDirectory,
            "secrets",
            "vault",
            record.BlobReference
        );
        File.SetAttributes(
            manifestPath,
            File.GetAttributes(manifestPath) | FileAttributes.ReadOnly
        );

        try
        {
            await Assert.ThrowsAsync<SecureCredentialStoreException>(() =>
                vault.DeleteSecretAsync(record.SecretId)
            );
        }
        finally
        {
            if (File.Exists(manifestPath))
            {
                File.SetAttributes(manifestPath, FileAttributes.Normal);
            }
        }

        Assert.True(File.Exists(blobPath));
        Assert.Equal("sk-delete-secret", await vault.RevealSecretAsync(record.SecretId));
    }

    [Fact]
    public async Task DeleteSecretAsyncRestoresManifestWhenBlobDeleteFails()
    {
        var pathService = new TestPathService(_rootDirectory);
        var vault = new DpapiSecretVault(pathService);
        var record = await vault.SaveSecretAsync(
            SecretKind.ApiKey,
            "sk-locked-delete-secret",
            "unit-test"
        );
        var blobPath = Path.Combine(
            pathService.Directories.ConfigDirectory,
            "secrets",
            "vault",
            record.BlobReference
        );

        await using (
            new FileStream(blobPath, FileMode.Open, FileAccess.Read, FileShare.None)
        )
        {
            await Assert.ThrowsAsync<SecureCredentialStoreException>(() =>
                vault.DeleteSecretAsync(record.SecretId)
            );
        }

        Assert.True(File.Exists(blobPath));
        Assert.NotNull(await vault.GetSecretAsync(record.SecretId));
        Assert.Equal("sk-locked-delete-secret", await vault.RevealSecretAsync(record.SecretId));
    }

    [Fact]
    public async Task SaveSecretAsyncKeepsExistingSecretWhenManifestRewriteFails()
    {
        var pathService = new TestPathService(_rootDirectory);
        var vault = new DpapiSecretVault(pathService);
        var record = await vault.SaveSecretAsync(
            SecretKind.ApiKey,
            "sk-original-secret",
            "unit-test",
            secretId: "sec-save-rewrite"
        );
        var manifestPath = Path.Combine(
            pathService.Directories.ConfigDirectory,
            "secrets",
            "vault-manifest.json"
        );
        var blobDirectory = Path.Combine(
            pathService.Directories.ConfigDirectory,
            "secrets",
            "vault"
        );
        var originalBlobPath = Path.Combine(blobDirectory, record.BlobReference);
        var originalBlobBytes = await File.ReadAllBytesAsync(originalBlobPath);
        var originalManifestJson = await File.ReadAllTextAsync(manifestPath);
        File.SetAttributes(
            manifestPath,
            File.GetAttributes(manifestPath) | FileAttributes.ReadOnly
        );

        try
        {
            await Assert.ThrowsAsync<SecureCredentialStoreException>(() =>
                vault.SaveSecretAsync(
                    SecretKind.ApiKey,
                    "sk-updated-secret",
                    "unit-test",
                    secretId: record.SecretId
                )
            );
        }
        finally
        {
            if (File.Exists(manifestPath))
            {
                File.SetAttributes(manifestPath, FileAttributes.Normal);
            }
        }

        Assert.True(File.Exists(originalBlobPath));
        Assert.Equal(originalBlobBytes, await File.ReadAllBytesAsync(originalBlobPath));
        Assert.Equal(originalManifestJson, await File.ReadAllTextAsync(manifestPath));
        Assert.Equal("sk-original-secret", await vault.RevealSecretAsync(record.SecretId));
        Assert.Single(Directory.EnumerateFiles(blobDirectory, "*.bin"));
        Assert.Empty(
            Directory.EnumerateFiles(
                Path.GetDirectoryName(manifestPath)!,
                $"{Path.GetFileName(manifestPath)}.*.tmp"
            )
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
