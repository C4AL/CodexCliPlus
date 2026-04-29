using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexCliPlus.Core.Models.Management;
using Konscious.Security.Cryptography;

namespace CodexCliPlus.Services;

internal static class SecureAccountPackageService
{
    private const string SecurePackageFormat = "CodexCliPlus.SecureAccountPackage";
    private const string PlainPackageFormat = "CodexCliPlus.AccountConfig";
    private const int CurrentVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Argon2MemoryKb = 64 * 1024;
    private const int Argon2Iterations = 3;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes(SecurePackageFormat);

    internal static JsonSerializerOptions JsonOptions { get; } =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static async Task WritePlainPackageAsync(
        SecureAccountPackagePayload payload,
        string packagePath,
        CancellationToken cancellationToken = default
    )
    {
        payload.Format = PlainPackageFormat;
        payload.Version = CurrentVersion;
        payload.CreatedAtUtc = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await File.WriteAllTextAsync(
            packagePath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken
        );
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

        payload.Format = PlainPackageFormat;
        payload.Version = CurrentVersion;
        payload.CreatedAtUtc = DateTimeOffset.UtcNow;

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
            CreatedAtUtc = DateTimeOffset.UtcNow,
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
                ["hasConfigYaml"] = (!string.IsNullOrWhiteSpace(payload.ConfigYaml)).ToString(),
            },
        };

        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        var json = JsonSerializer.Serialize(package, JsonOptions);
        await File.WriteAllTextAsync(
            packagePath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken
        );
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
            || package.Version != CurrentVersion
            || !string.Equals(package.Kdf.Algorithm, "argon2id", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(package.Cipher, "AES-256-GCM", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidDataException("不支持的安全包格式。");
        }

        var salt = Convert.FromBase64String(package.Kdf.Salt);
        var nonce = Convert.FromBase64String(package.Nonce);
        var tag = Convert.FromBase64String(package.Tag);
        var ciphertext = Convert.FromBase64String(package.Ciphertext);
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

    private static void ValidatePayload(SecureAccountPackagePayload payload)
    {
        if (payload.Version != CurrentVersion)
        {
            throw new InvalidDataException("不支持的账号配置版本。");
        }

        if (
            !string.IsNullOrWhiteSpace(payload.Format)
            && !string.Equals(payload.Format, PlainPackageFormat, StringComparison.Ordinal)
        )
        {
            throw new InvalidDataException("账号配置格式无效。");
        }
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
    public string Format { get; set; } = "CodexCliPlus.AccountConfig";

    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? ConfigYaml { get; set; }

    public List<SecureAccountPackageAuthFile> AuthFiles { get; set; } = [];

    public Dictionary<string, List<string>> OAuthExcludedModels { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<
        string,
        List<ManagementOAuthModelAliasEntry>
    > OAuthModelAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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

    public DateTimeOffset CreatedAtUtc { get; init; }

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
