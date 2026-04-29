using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.Infrastructure.Management;

public sealed class ManagementConfigurationService : IManagementConfigurationService
{
    private readonly IManagementApiClient _apiClient;

    public ManagementConfigurationService(IManagementApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<ManagementApiResponse<ManagementConfigSnapshot>> GetConfigAsync(
        CancellationToken cancellationToken = default
    )
    {
        var response = await _apiClient.SendManagementAsync(
            HttpMethod.Get,
            "config",
            cancellationToken: cancellationToken
        );
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(
            response,
            ManagementMappers.MapConfig(document.RootElement)
        );
    }

    public Task<ManagementApiResponse<string>> GetConfigYamlAsync(
        CancellationToken cancellationToken = default
    )
    {
        return _apiClient.SendManagementAsync(
            HttpMethod.Get,
            "config.yaml",
            accept: "application/yaml",
            cancellationToken: cancellationToken
        );
    }

    public async Task<ManagementApiResponse<ManagementOperationResult>> PutConfigYamlAsync(
        string yamlContent,
        CancellationToken cancellationToken = default
    )
    {
        var response = await _apiClient.SendManagementAsync(
            HttpMethod.Put,
            "config.yaml",
            yamlContent,
            contentType: "application/yaml",
            cancellationToken: cancellationToken
        );
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(
            response,
            ManagementMappers.MapOperation(document.RootElement)
        );
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> UpdateBooleanSettingAsync(
        string path,
        bool value,
        CancellationToken cancellationToken = default
    )
    {
        return SendOperationAsync(
            HttpMethod.Put,
            path,
            new Dictionary<string, object?> { ["value"] = value },
            cancellationToken
        );
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> UpdateIntegerSettingAsync(
        string path,
        int value,
        CancellationToken cancellationToken = default
    )
    {
        return SendOperationAsync(
            HttpMethod.Put,
            path,
            new Dictionary<string, object?> { ["value"] = value },
            cancellationToken
        );
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> UpdateStringSettingAsync(
        string path,
        string value,
        CancellationToken cancellationToken = default
    )
    {
        return SendOperationAsync(
            HttpMethod.Put,
            path,
            new Dictionary<string, object?> { ["value"] = value },
            cancellationToken
        );
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> DeleteSettingAsync(
        string path,
        CancellationToken cancellationToken = default
    )
    {
        return SendOperationAsync(HttpMethod.Delete, path, payload: null, cancellationToken);
    }

    private async Task<ManagementApiResponse<ManagementOperationResult>> SendOperationAsync(
        HttpMethod method,
        string path,
        object? payload,
        CancellationToken cancellationToken
    )
    {
        var body = payload is null ? null : ManagementJson.Serialize(payload);
        var response = await _apiClient.SendManagementAsync(
            method,
            path.TrimStart('/'),
            body,
            cancellationToken: cancellationToken
        );
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(
            response,
            ManagementMappers.MapOperation(document.RootElement)
        );
    }
}
