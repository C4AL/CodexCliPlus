using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.Infrastructure.Management;

public sealed class ManagementUsageService : IManagementUsageService
{
    private static readonly TimeSpan UsageTimeout = TimeSpan.FromSeconds(60);

    private readonly IManagementApiClient _apiClient;

    public ManagementUsageService(IManagementApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<ManagementApiResponse<ManagementUsageSnapshot>> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, "usage", timeout: UsageTimeout, cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapUsage(document.RootElement));
    }

    public async Task<ManagementApiResponse<ManagementUsageExportPayload>> ExportUsageAsync(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, "usage/export", timeout: UsageTimeout, cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapUsageExport(document.RootElement));
    }

    public async Task<ManagementApiResponse<ManagementUsageImportResult>> ImportUsageAsync(ManagementUsageExportPayload payload, CancellationToken cancellationToken = default)
    {
        var body = ManagementJson.Serialize(ManagementMappers.ToUsageImportPayload(payload));
        var response = await _apiClient.SendManagementAsync(HttpMethod.Post, "usage/import", body, timeout: UsageTimeout, cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapUsageImportResult(document.RootElement));
    }
}
