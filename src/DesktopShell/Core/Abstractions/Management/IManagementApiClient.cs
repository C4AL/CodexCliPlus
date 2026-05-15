using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.Core.Abstractions.Management;

public interface IManagementApiClient
{
    Task<ManagementApiResponse<string>> SendManagementAsync(
        HttpMethod method,
        string path,
        string? body = null,
        string contentType = "application/json",
        string? accept = "application/json",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    );

    Task<ManagementApiResponse<string>> SendManagementMultipartAsync(
        HttpMethod method,
        string path,
        IReadOnlyList<ManagementMultipartFile> files,
        IReadOnlyDictionary<string, string>? fields = null,
        string? accept = "application/json",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    );

    Task<ManagementApiResponse<string>> GetBackendAsync(
        string path,
        IReadOnlyDictionary<string, string>? headers = null,
        string? accept = "application/json",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    );
}
