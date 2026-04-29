using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.Infrastructure.Management;

public sealed class ManagementLogsService : IManagementLogsService
{
    private static readonly TimeSpan LogsTimeout = TimeSpan.FromSeconds(60);

    private readonly IManagementApiClient _apiClient;

    public ManagementLogsService(IManagementApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<ManagementApiResponse<ManagementLogsSnapshot>> GetLogsAsync(
        long after = 0,
        int limit = 0,
        CancellationToken cancellationToken = default
    )
    {
        var query = new List<string>();
        if (after > 0)
        {
            query.Add($"after={after}");
        }

        if (limit > 0)
        {
            query.Add($"limit={limit}");
        }

        var path = query.Count == 0 ? "logs" : $"logs?{string.Join("&", query)}";
        var response = await _apiClient.SendManagementAsync(
            HttpMethod.Get,
            path,
            timeout: LogsTimeout,
            cancellationToken: cancellationToken
        );
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(
            response,
            ManagementMappers.MapLogs(document.RootElement)
        );
    }

    public async Task<ManagementApiResponse<ManagementOperationResult>> ClearLogsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var response = await _apiClient.SendManagementAsync(
            HttpMethod.Delete,
            "logs",
            timeout: LogsTimeout,
            cancellationToken: cancellationToken
        );
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(
            response,
            ManagementMappers.MapOperation(document.RootElement)
        );
    }

    public async Task<
        ManagementApiResponse<IReadOnlyList<ManagementErrorLogFile>>
    > GetRequestErrorLogsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendManagementAsync(
            HttpMethod.Get,
            "request-error-logs",
            timeout: LogsTimeout,
            cancellationToken: cancellationToken
        );
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(
            response,
            ManagementMappers.MapErrorLogs(document.RootElement)
        );
    }

    public Task<ManagementApiResponse<string>> GetRequestLogByIdAsync(
        string id,
        CancellationToken cancellationToken = default
    )
    {
        return _apiClient.SendManagementAsync(
            HttpMethod.Get,
            $"request-log-by-id/{Uri.EscapeDataString(id)}",
            accept: "*/*",
            timeout: LogsTimeout,
            cancellationToken: cancellationToken
        );
    }
}
