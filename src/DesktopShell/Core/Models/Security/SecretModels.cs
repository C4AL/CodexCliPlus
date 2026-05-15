namespace CodexCliPlus.Core.Models.Security;

public enum SecretStatus
{
    Active,
    Disabled,
    Revoked,
    Migrated,
}

public enum SecretKind
{
    Unknown,
    ApiKey,
    OAuthAccessToken,
    OAuthRefreshToken,
    OAuthToken,
    AuthorizationHeader,
    Cookie,
    Header,
    AuthJson,
    ProviderCredential,
    ManagementKey,
}

public sealed class SecretRef
{
    public const string Scheme = "ccp-secret";
    public const string LegacyScheme = "vault";

    public SecretRef(string secretId)
    {
        if (string.IsNullOrWhiteSpace(secretId))
        {
            throw new ArgumentException("Secret id cannot be empty.", nameof(secretId));
        }

        SecretId = secretId.Trim();
    }

    public string SecretId { get; }

    public string Uri => $"{Scheme}://{SecretId}";

    public override string ToString()
    {
        return Uri;
    }

    public static bool IsSecretRef(string? value)
    {
        return TryParse(value, out _);
    }

    public static bool TryParse(string? value, out SecretRef? secretRef)
    {
        secretRef = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!System.Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (
            !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, LegacyScheme, StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        var id = uri.Host;
        if (string.IsNullOrWhiteSpace(id))
        {
            id = uri.AbsolutePath.Trim('/');
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        secretRef = new SecretRef(id);
        return true;
    }
}

public sealed class SecretRecord
{
    public string SecretId { get; init; } = string.Empty;

    public SecretKind Kind { get; init; } = SecretKind.Unknown;

    public string Source { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastUsedAtUtc { get; init; }

    public SecretStatus Status { get; init; } = SecretStatus.Active;

    public string BlobReference { get; init; } = string.Empty;

    public string? ValueSha256 { get; init; }

    public Dictionary<string, string> Metadata { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class VaultSecretExport
{
    public string SecretId { get; init; } = string.Empty;

    public SecretKind Kind { get; init; } = SecretKind.Unknown;

    public string Source { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public SecretStatus Status { get; init; } = SecretStatus.Active;

    public string Value { get; init; } = string.Empty;

    public Dictionary<string, string> Metadata { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SecretMigrationReport
{
    public int TotalSecrets { get; init; }

    public int ApiKeys { get; init; }

    public int OAuthTokens { get; init; }

    public int Headers { get; init; }

    public int AuthJsonValues { get; init; }
}

public sealed class SensitiveConfigMigrationResult
{
    public string Content { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;

    public SecretMigrationReport Report { get; init; } = new();

    public IReadOnlyList<VaultSecretExport> Secrets { get; init; } = [];
}
