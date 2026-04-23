using CPAD.Core.Abstractions.Management;
using CPAD.Core.Models.Management;

namespace CPAD.Infrastructure.Management;

public sealed class ManagementSystemService : IManagementSystemService
{
    private readonly IManagementApiClient _apiClient;

    public ManagementSystemService(IManagementApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<ManagementApiResponse<ManagementLatestVersionInfo>> GetLatestVersionAsync(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, "latest-version", cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapLatestVersion(document.RootElement));
    }

    public async Task<ManagementApiResponse<IReadOnlyList<ManagementModelDescriptor>>> GetAvailableModelsAsync(
        string? apiKey = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        var requestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers is not null)
        {
            foreach (var pair in headers)
            {
                requestHeaders[pair.Key] = pair.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(apiKey) && !requestHeaders.ContainsKey("Authorization"))
        {
            requestHeaders["Authorization"] = $"Bearer {apiKey}";
        }

        var response = await _apiClient.GetBackendAsync("v1/models", requestHeaders, cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapModelDescriptors(document.RootElement));
    }

    public async Task<ManagementApiResponse<ManagementApiCallResult>> ExecuteApiCallAsync(ManagementApiCallRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["method"] = request.Method,
            ["url"] = request.Url
        };

        if (!string.IsNullOrWhiteSpace(request.AuthIndex))
        {
            payload["authIndex"] = request.AuthIndex;
        }

        if (request.Headers.Count > 0)
        {
            payload["header"] = request.Headers;
        }

        if (!string.IsNullOrWhiteSpace(request.Data))
        {
            payload["data"] = request.Data;
        }

        var response = await _apiClient.SendManagementAsync(
            HttpMethod.Post,
            "api-call",
            ManagementJson.Serialize(payload),
            cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapApiCallResult(document.RootElement));
    }
}
