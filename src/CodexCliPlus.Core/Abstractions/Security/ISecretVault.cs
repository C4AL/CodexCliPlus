using CodexCliPlus.Core.Models.Security;

namespace CodexCliPlus.Core.Abstractions.Security;

public interface ISecretVault
{
    Task<SecretRecord> SaveSecretAsync(
        SecretKind kind,
        string value,
        string source,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? secretId = null,
        CancellationToken cancellationToken = default
    );

    Task<string?> RevealSecretAsync(string secretId, CancellationToken cancellationToken = default);

    Task<SecretRecord?> GetSecretAsync(
        string secretId,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<SecretRecord>> ListSecretsAsync(
        CancellationToken cancellationToken = default
    );

    Task SetSecretStatusAsync(
        string secretId,
        SecretStatus status,
        CancellationToken cancellationToken = default
    );

    Task RevokeAllAsync(CancellationToken cancellationToken = default);

    Task DeleteSecretAsync(string secretId, CancellationToken cancellationToken = default);
}
