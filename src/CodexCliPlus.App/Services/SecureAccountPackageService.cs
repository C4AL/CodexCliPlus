using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Core.Models.Security;
using CodexCliPlus.Infrastructure.Utilities;
using Konscious.Security.Cryptography;

namespace CodexCliPlus.Services;

internal static class SecureAccountPackageService
{
    private const string SecurePackageFormat = "CodexCliPlus.SecureAccountPackage";
    private const string PlainPackageFormat = "CodexCliPlus.AccountMigrationPackage";
    private const string LegacyPlainPackageFormat = "CodexCliPlus.AccountConfig";
    private const int CurrentVersion = 2;
    private const int LegacyVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Argon2MemoryKb = 64 * 1024;
    private const int Argon2Iterations = 3;
    private const int MinArgon2MemoryKb = 8 * 1024;
    private const int MaxArgon2MemoryKb = 256 * 1024;
    private const int MinArgon2Iterations = 1;
    private const int MaxArgon2Iterations = 10;
    private const int MaxArgon2Parallelism = 16;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes(SecurePackageFormat);
    private static readonly Regex SecretReferencePattern =
        new(
            @"\b(?:ccp-secret|vault)://[A-Za-z0-9][A-Za-z0-9._-]*\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

    internal static JsonSerializerOptions JsonOptions { get; } =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

    public static async Task WritePlainPackageAsync(
        SecureAccountPackagePayload payload,
        string packagePath,
        CancellationToken cancellationToken = default
    )
    {
        NormalizePayloadCollections(payload);
        if (payload.VaultSecrets.Count > 0)
        {
            throw new InvalidOperationException(
                "明文导出已禁用，包含凭据的配置只能导出为 .sac 安全包。"
            );
        }

        ValidatePlainPackageHasNoSecretReferences(payload, invalidOperation: true);

        PreparePayloadForExport(payload);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await AtomicFileWriter.WriteUtf8NoBomTextAsync(packagePath, json, cancellationToken);
    }

    public static async Task<SecureAccountPackagePayload> ReadPlainPackageAsync(
        string packagePath,
        CancellationToken cancellationToken = default
    )
    {
        var json = await File.ReadAllTextAsync(packagePath, cancellationToken);
        var payload =
            JsonSerializer.Deserialize<SecureAccountPackagePayload>(json, JsonOptions)
            ?? throw new InvalidDataException("配置文件内容无效。");
        ValidatePayload(payload);
        if ((payload.VaultSecrets?.Count ?? 0) > 0)
        {
            throw new InvalidDataException("明文账号配置包含凭据，请使用 .sac 安全包导入。");
        }

        ValidatePlainPackageHasNoSecretReferences(payload, invalidOperation: false);

        return payload;
    }

    public static async Task WriteEncryptedPackageAsync(
        SecureAccountPackagePayload payload,
        string password,
        string packagePath,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("安全包密码不能为空。");
        }

        PreparePayloadForExport(payload);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = await DeriveKeyAsync(password, salt);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
        }

        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plaintext);

        var package = new SecureAccountPackageFile
        {
            Format = SecurePackageFormat,
            Version = CurrentVersion,
            PackageId = payload.PackageId,
            CreatedDeviceId = payload.CreatedDeviceId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = payload.ExpiresAtUtc,
            Kdf = new SecureAccountPackageKdf
            {
                Algorithm = "argon2id",
                MemoryKb = Argon2MemoryKb,
                Iterations = Argon2Iterations,
                Parallelism =
                    Environment.ProcessorCount <= 1 ? 1 : Math.Min(4, Environment.ProcessorCount),
                Salt = Convert.ToBase64String(salt),
            },
            Cipher = "AES-256-GCM",
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext),
            Metadata = new Dictionary<string, string>
            {
                ["authFileCount"] = payload.AuthFiles.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                ["secretCount"] = payload.VaultSecrets.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                ["schemaVersion"] = payload.Version.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                ["hasConfigYaml"] = (!string.IsNullOrWhiteSpace(payload.ConfigYaml)).ToString(),
            },
        };

        var json = JsonSerializer.Serialize(package, JsonOptions);
        await AtomicFileWriter.WriteUtf8NoBomTextAsync(packagePath, json, cancellationToken);
    }

    public static async Task<SecureAccountPackagePayload> ReadEncryptedPackageAsync(
        string packagePath,
        string password,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("安全包密码不能为空。");
        }

        var json = await File.ReadAllTextAsync(packagePath, cancellationToken);
        var package =
            JsonSerializer.Deserialize<SecureAccountPackageFile>(json, JsonOptions)
            ?? throw new InvalidDataException("安全包内容无效。");

        if (
            !string.Equals(package.Format, SecurePackageFormat, StringComparison.Ordinal)
            || package.Version is not (LegacyVersion or CurrentVersion)
            || package.Kdf is null
            || !string.Equals(package.Kdf.Algorithm, "argon2id", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(package.Cipher, "AES-256-GCM", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidDataException("不支持的安全包格式。");
        }

        if (package.ExpiresAtUtc is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidDataException("安全包已过期。");
        }

        ValidateEncryptedPackageParameters(package);

        var salt = ReadBase64Field(package.Kdf.Salt, SaltSize);
        var nonce = ReadBase64Field(package.Nonce, NonceSize);
        var tag = ReadBase64Field(package.Tag, TagSize);
        var ciphertext = ReadBase64Field(package.Ciphertext);
        var plaintext = new byte[ciphertext.Length];
        var key = await DeriveKeyAsync(
            password,
            salt,
            package.Kdf.MemoryKb,
            package.Kdf.Iterations,
            package.Kdf.Parallelism
        );

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
            var payload =
                JsonSerializer.Deserialize<SecureAccountPackagePayload>(plaintext, JsonOptions)
                ?? throw new InvalidDataException("安全包负载无效。");
            ValidatePayload(payload);
            if (
                package.Version == CurrentVersion
                && !string.IsNullOrWhiteSpace(package.PackageId)
                && string.IsNullOrWhiteSpace(payload.PackageId)
            )
            {
                payload.PackageId = package.PackageId;
            }

            return payload;
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("安全包密码错误或文件已损坏。", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static void ValidateEncryptedPackageParameters(SecureAccountPackageFile package)
    {
        var invalidParallelism =
            package.Version >= CurrentVersion
                ? package.Kdf.Parallelism is <= 0 or > MaxArgon2Parallelism
                : package.Kdf.Parallelism is < 0 or > MaxArgon2Parallelism;

        if (
            package.Kdf.MemoryKb is < MinArgon2MemoryKb or > MaxArgon2MemoryKb
            || package.Kdf.Iterations is < MinArgon2Iterations or > MaxArgon2Iterations
            || invalidParallelism
        )
        {
            throw new InvalidDataException("安全包 KDF 参数无效。");
        }
    }

    private static byte[] ReadBase64Field(string? value, int? expectedLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("安全包加密参数无效。");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("安全包加密参数无效。", exception);
        }

        if (
            bytes.Length == 0
            || (expectedLength is { } length && bytes.Length != length)
        )
        {
            throw new InvalidDataException("安全包加密参数无效。");
        }

        return bytes;
    }

    private static void ValidatePayload(SecureAccountPackagePayload payload)
    {
        NormalizePayloadCollections(payload);

        if (payload.Version is not (LegacyVersion or CurrentVersion))
        {
            throw new InvalidDataException("不支持的账号配置版本。");
        }

        if (
            !string.IsNullOrWhiteSpace(payload.Format)
            && !string.Equals(payload.Format, PlainPackageFormat, StringComparison.Ordinal)
            && !string.Equals(payload.Format, LegacyPlainPackageFormat, StringComparison.Ordinal)
        )
        {
            throw new InvalidDataException("账号配置格式无效。");
        }

        if (payload.ExpiresAtUtc is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidDataException("账号配置已过期。");
        }
    }

    private static void ValidatePlainPackageHasNoSecretReferences(
        SecureAccountPackagePayload payload,
        bool invalidOperation
    )
    {
        if (!PayloadContainsSecretReference(payload))
        {
            return;
        }

        if (invalidOperation)
        {
            throw new InvalidOperationException(
                "明文导出已禁用，包含凭据引用的配置只能导出为 .sac 安全包。"
            );
        }

        throw new InvalidDataException("明文账号配置包含凭据引用，请使用 .sac 安全包导入。");
    }

    private static bool PayloadContainsSecretReference(SecureAccountPackagePayload payload)
    {
        if (ContainsSecretReference(payload.ConfigYaml))
        {
            return true;
        }

        return payload.AuthFiles.Any(file => ContainsSecretReference(file.Content));
    }

    private static bool ContainsSecretReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (Match match in SecretReferencePattern.Matches(value))
        {
            if (SecretRef.TryParse(match.Value, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static void PreparePayloadForExport(SecureAccountPackagePayload payload)
    {
        NormalizePayloadCollections(payload);

        payload.Format = PlainPackageFormat;
        payload.Version = CurrentVersion;
        payload.PackageId = string.IsNullOrWhiteSpace(payload.PackageId)
            ? $"sac-{Guid.NewGuid():N}"
            : payload.PackageId.Trim();
        payload.CreatedDeviceId = string.IsNullOrWhiteSpace(payload.CreatedDeviceId)
            ? CreateDeviceId()
            : payload.CreatedDeviceId.Trim();
        payload.CreatedAtUtc = DateTimeOffset.UtcNow;
        payload.ExportPolicy ??= new SecureAccountPackageExportPolicy();
    }

    private static void NormalizePayloadCollections(SecureAccountPackagePayload payload)
    {
        payload.ExportPolicy ??= new SecureAccountPackageExportPolicy();
        payload.AuthFiles ??= [];
        payload.VaultSecrets ??= [];
        payload.Revocations ??= [];
        payload.OAuthExcludedModels = NormalizeStringListMap(payload.OAuthExcludedModels);
        payload.OAuthModelAliases = NormalizeAliasMap(payload.OAuthModelAliases);
    }

    private static Dictionary<string, List<string>> NormalizeStringListMap(
        Dictionary<string, List<string>>? source
    )
    {
        var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return normalized;
        }

        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            if (!normalized.TryGetValue(pair.Key, out var values))
            {
                values = [];
                normalized[pair.Key] = values;
            }

            foreach (var value in pair.Value ?? [])
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }

            normalized[pair.Key] = values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        return normalized;
    }

    private static Dictionary<string, List<ManagementOAuthModelAliasEntry>> NormalizeAliasMap(
        Dictionary<string, List<ManagementOAuthModelAliasEntry>>? source
    )
    {
        var normalized = new Dictionary<string, List<ManagementOAuthModelAliasEntry>>(
            StringComparer.OrdinalIgnoreCase
        );
        if (source is null)
        {
            return normalized;
        }

        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            if (!normalized.TryGetValue(pair.Key, out var values))
            {
                values = [];
                normalized[pair.Key] = values;
            }

            values.AddRange((pair.Value ?? []).Where(entry => entry is not null));
        }

        return normalized;
    }

    private static string CreateDeviceId()
    {
        var source =
            $"{Environment.UserDomainName}\\{Environment.UserName}@{Environment.MachineName}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    private static async Task<byte[]> DeriveKeyAsync(
        string password,
        byte[] salt,
        int memoryKb = Argon2MemoryKb,
        int iterations = Argon2Iterations,
        int parallelism = 0
    )
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism =
                    parallelism > 0 ? parallelism
                    : Environment.ProcessorCount <= 1 ? 1
                    : Math.Min(4, Environment.ProcessorCount),
                Iterations = iterations,
                MemorySize = memoryKb,
            };
            return await argon2.GetBytesAsync(KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}

internal sealed class SecureAccountPackagePayload
{
    public string Format { get; set; } = "CodexCliPlus.AccountMigrationPackage";

    public int Version { get; set; } = 2;

    public string PackageId { get; set; } = string.Empty;

    public string CreatedDeviceId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public SecureAccountPackageExportPolicy? ExportPolicy { get; set; } = new();

    public string? ConfigYaml { get; set; }

    public List<SecureAccountPackageAuthFile> AuthFiles { get; set; } = [];

    public List<VaultSecretExport> VaultSecrets { get; set; } = [];

    public List<SecureAccountPackageRevocation> Revocations { get; set; } = [];

    public Dictionary<string, List<string>> OAuthExcludedModels { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<
        string,
        List<ManagementOAuthModelAliasEntry>
    > OAuthModelAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class SecureAccountPackageExportPolicy
{
    public bool AllowPlaintextExport { get; set; }

    public bool DeviceBound { get; set; }

    public string? TargetDeviceId { get; set; }
}

internal sealed class SecureAccountPackageRevocation
{
    public string Type { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public DateTimeOffset RevokedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class SecureAccountPackageAuthFile
{
    public string Name { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}

internal sealed class SecureAccountPackageFile
{
    public string Format { get; init; } = string.Empty;

    public int Version { get; init; }

    public string PackageId { get; init; } = string.Empty;

    public string CreatedDeviceId { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public SecureAccountPackageKdf Kdf { get; init; } = new();

    public string Cipher { get; init; } = string.Empty;

    public string Nonce { get; init; } = string.Empty;

    public string Tag { get; init; } = string.Empty;

    public string Ciphertext { get; init; } = string.Empty;

    public Dictionary<string, string> Metadata { get; init; } = [];
}

internal sealed class SecureAccountPackageKdf
{
    public string Algorithm { get; init; } = string.Empty;

    public int MemoryKb { get; init; }

    public int Iterations { get; init; }

    public int Parallelism { get; init; }

    public string Salt { get; init; } = string.Empty;
}
