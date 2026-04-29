using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Exceptions;

namespace CodexCliPlus.Infrastructure.Security;

public sealed class DpapiCredentialStore : ISecureCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(AppConstants.AppUserModelId);
    private static readonly Regex ReferenceSanitizer = new("[^a-z0-9\\-]+", RegexOptions.Compiled);

    private readonly IPathService _pathService;

    public DpapiCredentialStore(IPathService pathService)
    {
        _pathService = pathService;
    }

    public async Task SaveSecretAsync(
        string reference,
        string value,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var secretPath = GetSecretPath(reference);
            Directory.CreateDirectory(Path.GetDirectoryName(secretPath)!);

            var payload = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value),
                Entropy,
                DataProtectionScope.CurrentUser
            );

            await File.WriteAllBytesAsync(secretPath, payload, cancellationToken);
        }
        catch (Exception exception)
            when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new SecureCredentialStoreException(
                $"Failed to save secure credential reference '{reference}'.",
                exception
            );
        }
    }

    public async Task<string?> LoadSecretAsync(
        string reference,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var secretPath = GetSecretPath(reference);
            if (!File.Exists(secretPath))
            {
                return null;
            }

            var payload = await File.ReadAllBytesAsync(secretPath, cancellationToken);
            var plainBytes = ProtectedData.Unprotect(
                payload,
                Entropy,
                DataProtectionScope.CurrentUser
            );
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception exception)
            when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new SecureCredentialStoreException(
                $"Failed to load secure credential reference '{reference}'.",
                exception
            );
        }
    }

    public Task DeleteSecretAsync(string reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var secretPath = GetSecretPath(reference);
            if (File.Exists(secretPath))
            {
                File.Delete(secretPath);
            }

            return Task.CompletedTask;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SecureCredentialStoreException(
                $"Failed to delete secure credential reference '{reference}'.",
                exception
            );
        }
    }

    private string GetSecretPath(string reference)
    {
        var normalizedReference = NormalizeReference(reference);
        return Path.Combine(
            _pathService.Directories.ConfigDirectory,
            AppConstants.SecretsDirectoryName,
            $"{normalizedReference}.bin"
        );
    }

    private static string NormalizeReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("Credential reference cannot be empty.", nameof(reference));
        }

        var sanitized = ReferenceSanitizer
            .Replace(reference.Trim().ToLowerInvariant(), "-")
            .Trim('-');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new ArgumentException(
                "Credential reference must contain letters or digits.",
                nameof(reference)
            );
        }

        return sanitized;
    }
}
