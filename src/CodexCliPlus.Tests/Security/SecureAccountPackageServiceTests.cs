using System.Text.Json;
using CodexCliPlus.Core.Models.Security;
using CodexCliPlus.Services;

namespace CodexCliPlus.Tests.Security;

[Trait("Category", "LocalIntegration")]
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
    public async Task WritePackageAsyncAllowsFileNameOnlyPackagePath()
    {
        var plainPath = $"codexcliplus-plain-{Guid.NewGuid():N}.json";
        var securePath = $"codexcliplus-secure-{Guid.NewGuid():N}.sac";

        try
        {
            await SecureAccountPackageService.WritePlainPackageAsync(
                new SecureAccountPackagePayload { ConfigYaml = "model = \"gpt-5\"\n" },
                plainPath
            );
            await SecureAccountPackageService.WriteEncryptedPackageAsync(
                CreatePayload(),
                "passphrase",
                securePath
            );

            var plain = await SecureAccountPackageService.ReadPlainPackageAsync(plainPath);
            var secure = await SecureAccountPackageService.ReadEncryptedPackageAsync(
                securePath,
                "passphrase"
            );

            Assert.Equal("model = \"gpt-5\"\n", plain.ConfigYaml);
            Assert.Single(secure.VaultSecrets);
        }
        finally
        {
            if (File.Exists(plainPath))
            {
                File.Delete(plainPath);
            }

            if (File.Exists(securePath))
            {
                File.Delete(securePath);
            }
        }
    }

    [Fact]
    public async Task WritePlainPackageAsyncKeepsExistingPackageWhenReplacementFails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "plain-locked.json");
        var originalJson =
            """
            {
              "format": "CodexCliPlus.AccountMigrationPackage",
              "version": 2,
              "configYaml": "model = \"old\"\n"
            }
            """;
        await File.WriteAllTextAsync(packagePath, originalJson);
        await using var lockedPackage = new FileStream(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );

        var exception = await Record.ExceptionAsync(() =>
            SecureAccountPackageService.WritePlainPackageAsync(
                new SecureAccountPackagePayload { ConfigYaml = "model = \"new\"\n" },
                packagePath
            )
        );

        Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
        Assert.Equal(originalJson, await File.ReadAllTextAsync(packagePath));
        Assert.Empty(Directory.EnumerateFiles(_rootDirectory, "plain-locked.json.*.tmp"));
    }

    [Fact]
    public async Task WritePlainPackageAsyncPropagatesCancellationBeforePreparingPayload()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "cancelled-plain.json");
        var payload = new SecureAccountPackagePayload { ConfigYaml = "model = \"gpt-5\"\n" };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            SecureAccountPackageService.WritePlainPackageAsync(
                payload,
                packagePath,
                cancellation.Token
            )
        );

        Assert.Empty(payload.PackageId);
        Assert.Empty(payload.CreatedDeviceId);
        Assert.False(File.Exists(packagePath));
    }

    [Fact]
    public async Task WriteEncryptedPackageAsyncPropagatesCancellationBeforePreparingPayload()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "cancelled-secure.sac");
        var payload = CreatePayload();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            SecureAccountPackageService.WriteEncryptedPackageAsync(
                payload,
                "passphrase",
                packagePath,
                cancellation.Token
            )
        );

        Assert.Empty(payload.PackageId);
        Assert.Empty(payload.CreatedDeviceId);
        Assert.False(File.Exists(packagePath));
    }

    [Fact]
    public async Task WritePlainPackageAsyncRejectsSecretRefsWithoutEncryptedVaultPayload()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "plain-ref.json");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SecureAccountPackageService.WritePlainPackageAsync(
                new SecureAccountPackagePayload
                {
                    ConfigYaml = "api-keys:\n  - \"vault://sec-missing\"\n",
                },
                packagePath
            )
        );

        Assert.Contains(".sac", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(packagePath));
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

    [Fact]
    public async Task ReadEncryptedPackageAsyncRejectsUnsafeKdfParametersBeforeDerivingKey()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "unsafe-kdf.sac");
        await File.WriteAllTextAsync(
            packagePath,
            CreateEncryptedPackageJson(memoryKb: 1024 * 1024)
        );

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SecureAccountPackageService.ReadEncryptedPackageAsync(packagePath, "passphrase")
        );

        Assert.Contains("KDF", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task ReadEncryptedPackageAsyncRejectsInvalidV2KdfParallelismBeforeDerivingKey(
        int parallelism
    )
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "unsafe-kdf-parallelism.sac");
        await File.WriteAllTextAsync(
            packagePath,
            CreateEncryptedPackageJson(parallelism: parallelism)
        );

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SecureAccountPackageService.ReadEncryptedPackageAsync(packagePath, "passphrase")
        );

        Assert.Contains("KDF", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadEncryptedPackageAsyncRejectsInvalidCryptoParameterSizes()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "invalid-crypto.sac");
        await File.WriteAllTextAsync(
            packagePath,
            CreateEncryptedPackageJson(salt: Convert.ToBase64String([1, 2, 3]))
        );

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SecureAccountPackageService.ReadEncryptedPackageAsync(packagePath, "passphrase")
        );

        Assert.Contains("加密参数", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadEncryptedPackageAsyncRejectsMissingKdfWithoutNullReference()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "missing-kdf.sac");
        await File.WriteAllTextAsync(packagePath, CreateEncryptedPackageJson(kdfJson: "null"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SecureAccountPackageService.ReadEncryptedPackageAsync(packagePath, "passphrase")
        );

        Assert.Contains("安全包格式", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadPlainPackageAsyncRejectsPlaintextVaultSecrets()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "plain-secrets.json");
        await File.WriteAllTextAsync(
            packagePath,
            """
            {
              "format": "CodexCliPlus.AccountMigrationPackage",
              "version": 2,
              "vaultSecrets": [
                {
                  "secretId": "sec-plain",
                  "kind": "ApiKey",
                  "source": "plain-json",
                  "status": "Active",
                  "value": "sk-plain-secret"
                }
              ]
            }
            """
        );

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SecureAccountPackageService.ReadPlainPackageAsync(packagePath)
        );

        Assert.Contains(".sac", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadPlainPackageAsyncRejectsMalformedJsonAsInvalidData()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "malformed-plain.json");
        await File.WriteAllTextAsync(packagePath, "{ not-json");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SecureAccountPackageService.ReadPlainPackageAsync(packagePath)
        );

        Assert.Contains("配置文件内容无效", exception.Message, StringComparison.Ordinal);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task ReadEncryptedPackageAsyncRejectsMalformedJsonAsInvalidData()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "malformed-secure.sac");
        await File.WriteAllTextAsync(packagePath, "{ not-json");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SecureAccountPackageService.ReadEncryptedPackageAsync(packagePath, "passphrase")
        );

        Assert.Contains("安全包内容无效", exception.Message, StringComparison.Ordinal);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task ReadPlainPackageAsyncRejectsSecretRefsWithoutEncryptedVaultPayload()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "plain-ref.json");
        await File.WriteAllTextAsync(
            packagePath,
            """
            {
              "format": "CodexCliPlus.AccountMigrationPackage",
              "version": 2,
              "configYaml": "api-keys:\n  - \"ccp-secret://sec-missing\"\n"
            }
            """
        );

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SecureAccountPackageService.ReadPlainPackageAsync(packagePath)
        );

        Assert.Contains(".sac", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadPlainPackageAsyncNormalizesNullCollections()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "null-collections.json");
        await File.WriteAllTextAsync(
            packagePath,
            """
            {
              "format": "CodexCliPlus.AccountMigrationPackage",
              "version": 2,
              "exportPolicy": null,
              "authFiles": null,
              "vaultSecrets": null,
              "revocations": null,
              "oauthExcludedModels": null,
              "oauthModelAliases": null
            }
            """
        );

        var payload = await SecureAccountPackageService.ReadPlainPackageAsync(packagePath);

        Assert.NotNull(payload.ExportPolicy);
        Assert.Empty(payload.AuthFiles);
        Assert.Empty(payload.VaultSecrets);
        Assert.Empty(payload.Revocations);
        Assert.Empty(payload.OAuthExcludedModels);
        Assert.Empty(payload.OAuthModelAliases);
    }

    [Fact]
    public async Task ReadPlainPackageAsyncMergesCaseVariantOAuthMaps()
    {
        Directory.CreateDirectory(_rootDirectory);
        var packagePath = Path.Combine(_rootDirectory, "oauth-case-variants.json");
        await File.WriteAllTextAsync(
            packagePath,
            """
            {
              "format": "CodexCliPlus.AccountMigrationPackage",
              "version": 2,
              "oauthExcludedModels": {
                "gemini": [ "gemini-1.5-pro", "gemini-1.5-flash" ],
                "GEMINI": [ "GEMINI-1.5-PRO", "gemini-2.0-flash" ]
              },
              "oauthModelAliases": {
                "openai": [
                  { "name": "gpt-5", "alias": "gpt-5-chat", "fork": false }
                ],
                "OpenAI": [
                  { "name": "gpt-5-mini", "alias": "gpt-5-mini-chat", "fork": true }
                ]
              }
            }
            """
        );

        var payload = await SecureAccountPackageService.ReadPlainPackageAsync(packagePath);

        var excluded = Assert.Single(payload.OAuthExcludedModels);
        Assert.Equal("gemini", excluded.Key);
        Assert.Equal(
            ["gemini-1.5-pro", "gemini-1.5-flash", "gemini-2.0-flash"],
            excluded.Value
        );
        var aliases = Assert.Single(payload.OAuthModelAliases);
        Assert.Equal("openai", aliases.Key);
        Assert.Equal(2, aliases.Value.Count);
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

    private static string CreateEncryptedPackageJson(
        int memoryKb = 64 * 1024,
        int parallelism = 1,
        string? salt = null,
        string? kdfJson = null
    )
    {
        salt ??= Convert.ToBase64String(new byte[16]);
        var nonce = Convert.ToBase64String(new byte[12]);
        var tag = Convert.ToBase64String(new byte[16]);
        var ciphertext = Convert.ToBase64String([1]);
        kdfJson ??= $$"""
        {
            "algorithm": "argon2id",
            "memoryKb": {{memoryKb}},
            "iterations": 3,
            "parallelism": {{parallelism}},
            "salt": "{{salt}}"
          }
        """;
        return $$"""
        {
          "format": "CodexCliPlus.SecureAccountPackage",
          "version": 2,
          "packageId": "sac-test",
          "createdDeviceId": "device-test",
          "createdAtUtc": "2026-01-01T00:00:00Z",
          "kdf": {{kdfJson}},
          "cipher": "AES-256-GCM",
          "nonce": "{{nonce}}",
          "tag": "{{tag}}",
          "ciphertext": "{{ciphertext}}",
          "metadata": {}
        }
        """;
    }
}
