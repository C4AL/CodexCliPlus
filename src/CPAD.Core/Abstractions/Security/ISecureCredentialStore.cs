namespace CPAD.Core.Abstractions.Security;

public interface ISecureCredentialStore
{
    Task SaveSecretAsync(string reference, string value, CancellationToken cancellationToken = default);

    Task<string?> LoadSecretAsync(string reference, CancellationToken cancellationToken = default);

    Task DeleteSecretAsync(string reference, CancellationToken cancellationToken = default);
}
