using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Exceptions;
using CodexCliPlus.Core.Models.Security;

namespace CodexCliPlus.Infrastructure.Security;

public sealed class DpapiSecretVault : ISecretVault, IDisposable
{
    private const int ManifestVersion = 2;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(
        $"{AppConstants.AppUserModelId}:SecretVault2"
    );
    private static readonly Regex SecretIdPattern = new(
        "^[a-z0-9][a-z0-9._-]{2,127}$",
        RegexOptions.Compiled
    );
    private static readonly Regex SecretIdSanitizer = new("[^a-z0-9._-]+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IPathService _pathService;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DpapiSecretVault(IPathService pathService)
    {
        _pathService = pathService;
    }

    public async Task<SecretRecord> SaveSecretAsync(
        SecretKind kind,
        string value,
        string source,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? secretId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Secret value cannot be empty.", nameof(value));
        }

        var normalizedSecretId = NormalizeSecretId(secretId);
        var blobReference = $"{normalizedSecretId}.bin";
        var plainBytes = Encoding.UTF8.GetBytes(value);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _pathService.EnsureCreatedAsync(cancellationToken);
            Directory.CreateDirectory(GetBlobDirectory());

            var protectedBytes = ProtectedData.Protect(
                plainBytes,
                Entropy,
                DataProtectionScope.CurrentUser
            );
            await File.WriteAllBytesAsync(
                GetBlobPath(blobReference),
                protectedBytes,
                cancellationToken
            );

            var manifest = await ReadManifestAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var existing = manifest.Secrets.FirstOrDefault(record =>
                string.Equals(
                    record.SecretId,
                    normalizedSecretId,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            var record = new SecretRecord
            {
                SecretId = normalizedSecretId,
                Kind = kind,
                Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim(),
                CreatedAtUtc = existing?.CreatedAtUtc ?? now,
                LastUsedAtUtc = existing?.LastUsedAtUtc,
                Status =
                    existing?.Status is SecretStatus.Revoked
                        ? SecretStatus.Revoked
                        : SecretStatus.Active,
                BlobReference = blobReference,
                ValueSha256 = Convert.ToHexStringLower(SHA256.HashData(plainBytes)),
                Metadata =
                    metadata?.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase
                    ) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };

            manifest.Secrets.RemoveAll(item =>
                string.Equals(item.SecretId, normalizedSecretId, StringComparison.OrdinalIgnoreCase)
            );
            manifest.Secrets.Add(record);
            await WriteManifestAsync(manifest, cancellationToken);

            return record;
        }
        catch (Exception exception)
            when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new SecureCredentialStoreException(
                $"Failed to save vault secret '{normalizedSecretId}'.",
                exception
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            _gate.Release();
        }
    }

    public async Task<string?> RevealSecretAsync(
        string secretId,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedSecretId = NormalizeExistingSecretId(secretId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var manifest = await ReadManifestAsync(cancellationToken);
            var record = manifest.Secrets.FirstOrDefault(item =>
                string.Equals(item.SecretId, normalizedSecretId, StringComparison.OrdinalIgnoreCase)
            );
            if (record is null || record.Status != SecretStatus.Active)
            {
                return null;
            }

            var blobPath = GetBlobPath(record.BlobReference);
            if (!File.Exists(blobPath))
            {
                return null;
            }

            var protectedBytes = await File.ReadAllBytesAsync(blobPath, cancellationToken);
            var plainBytes = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser
            );
            try
            {
                var value = Encoding.UTF8.GetString(plainBytes);
                UpdateRecord(
                    manifest,
                    normalizedSecretId,
                    record.WithLastUsedAtUtc(DateTimeOffset.UtcNow)
                );
                await WriteManifestAsync(manifest, cancellationToken);
                return value;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
        catch (Exception exception)
            when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new SecureCredentialStoreException(
                $"Failed to reveal vault secret '{normalizedSecretId}'.",
                exception
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SecretRecord?> GetSecretAsync(
        string secretId,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedSecretId = NormalizeExistingSecretId(secretId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var manifest = await ReadManifestAsync(cancellationToken);
            return manifest.Secrets.FirstOrDefault(item =>
                string.Equals(item.SecretId, normalizedSecretId, StringComparison.OrdinalIgnoreCase)
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SecretRecord>> ListSecretsAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var manifest = await ReadManifestAsync(cancellationToken);
            return manifest.Secrets.OrderBy(item => item.CreatedAtUtc).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetSecretStatusAsync(
        string secretId,
        SecretStatus status,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedSecretId = NormalizeExistingSecretId(secretId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var manifest = await ReadManifestAsync(cancellationToken);
            var record = manifest.Secrets.FirstOrDefault(item =>
                string.Equals(item.SecretId, normalizedSecretId, StringComparison.OrdinalIgnoreCase)
            );
            if (record is null)
            {
                return;
            }

            UpdateRecord(manifest, normalizedSecretId, record.WithStatus(status));
            await WriteManifestAsync(manifest, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RevokeAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var manifest = await ReadManifestAsync(cancellationToken);
            manifest.Secrets = manifest
                .Secrets.Select(record => record.WithStatus(SecretStatus.Revoked))
                .ToList();
            await WriteManifestAsync(manifest, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteSecretAsync(
        string secretId,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedSecretId = NormalizeExistingSecretId(secretId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var manifest = await ReadManifestAsync(cancellationToken);
            var record = manifest.Secrets.FirstOrDefault(item =>
                string.Equals(item.SecretId, normalizedSecretId, StringComparison.OrdinalIgnoreCase)
            );
            if (record is not null)
            {
                var blobPath = GetBlobPath(record.BlobReference);
                if (File.Exists(blobPath))
                {
                    File.Delete(blobPath);
                }
            }

            manifest.Secrets.RemoveAll(item =>
                string.Equals(item.SecretId, normalizedSecretId, StringComparison.OrdinalIgnoreCase)
            );
            await WriteManifestAsync(manifest, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SecureCredentialStoreException(
                $"Failed to delete vault secret '{normalizedSecretId}'.",
                exception
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SecretVaultManifest> ReadManifestAsync(CancellationToken cancellationToken)
    {
        var manifestPath = GetManifestPath();
        if (!File.Exists(manifestPath))
        {
            return new SecretVaultManifest();
        }

        await using var stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<SecretVaultManifest>(
                stream,
                JsonOptions,
                cancellationToken
            ) ?? new SecretVaultManifest();
    }

    private async Task WriteManifestAsync(
        SecretVaultManifest manifest,
        CancellationToken cancellationToken
    )
    {
        manifest.Version = ManifestVersion;
        manifest.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(GetSecretRootDirectory());
        await using var stream = File.Create(GetManifestPath());
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
    }

    private string GetSecretRootDirectory()
    {
        return Path.Combine(
            _pathService.Directories.ConfigDirectory,
            AppConstants.SecretsDirectoryName
        );
    }

    private string GetBlobDirectory()
    {
        return Path.Combine(GetSecretRootDirectory(), "vault");
    }

    private string GetManifestPath()
    {
        return Path.Combine(GetSecretRootDirectory(), "vault-manifest.json");
    }

    private string GetBlobPath(string blobReference)
    {
        return Path.Combine(GetBlobDirectory(), Path.GetFileName(blobReference));
    }

    private static string NormalizeSecretId(string? requestedSecretId)
    {
        if (string.IsNullOrWhiteSpace(requestedSecretId))
        {
            return $"sec-{Guid.NewGuid():N}";
        }

        var trimmed = requestedSecretId.Trim().ToLowerInvariant();
        if (SecretIdPattern.IsMatch(trimmed))
        {
            return trimmed;
        }

        var sanitized = SecretIdSanitizer.Replace(trimmed, "-").Trim('-', '.', '_');
        if (!SecretIdPattern.IsMatch(sanitized))
        {
            throw new ArgumentException(
                "Secret id contains no usable identifier characters.",
                nameof(requestedSecretId)
            );
        }

        return sanitized;
    }

    private static string NormalizeExistingSecretId(string secretId)
    {
        if (SecretRef.TryParse(secretId, out var secretRef))
        {
            secretId = secretRef!.SecretId;
        }

        return NormalizeSecretId(secretId);
    }

    private static void UpdateRecord(
        SecretVaultManifest manifest,
        string secretId,
        SecretRecord replacement
    )
    {
        manifest.Secrets.RemoveAll(item =>
            string.Equals(item.SecretId, secretId, StringComparison.OrdinalIgnoreCase)
        );
        manifest.Secrets.Add(replacement);
    }

    private sealed class SecretVaultManifest
    {
        public int Version { get; set; } = ManifestVersion;

        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public List<SecretRecord> Secrets { get; set; } = [];
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}

internal static class SecretRecordExtensions
{
    public static SecretRecord WithStatus(this SecretRecord record, SecretStatus status)
    {
        return new SecretRecord
        {
            SecretId = record.SecretId,
            Kind = record.Kind,
            Source = record.Source,
            CreatedAtUtc = record.CreatedAtUtc,
            LastUsedAtUtc = record.LastUsedAtUtc,
            Status = status,
            BlobReference = record.BlobReference,
            ValueSha256 = record.ValueSha256,
            Metadata = new Dictionary<string, string>(
                record.Metadata,
                StringComparer.OrdinalIgnoreCase
            ),
        };
    }

    public static SecretRecord WithLastUsedAtUtc(
        this SecretRecord record,
        DateTimeOffset lastUsedAtUtc
    )
    {
        return new SecretRecord
        {
            SecretId = record.SecretId,
            Kind = record.Kind,
            Source = record.Source,
            CreatedAtUtc = record.CreatedAtUtc,
            LastUsedAtUtc = lastUsedAtUtc,
            Status = record.Status,
            BlobReference = record.BlobReference,
            ValueSha256 = record.ValueSha256,
            Metadata = new Dictionary<string, string>(
                record.Metadata,
                StringComparer.OrdinalIgnoreCase
            ),
        };
    }
}
