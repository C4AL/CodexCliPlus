using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.Infrastructure.Management;

public sealed class ManagementQuotaService : IManagementQuotaService
{
    private readonly IManagementConfigurationService _configurationService;

    public ManagementQuotaService(IManagementConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public Task<ManagementApiResponse<ManagementConfigSnapshot>> GetQuotaSettingsAsync(CancellationToken cancellationToken = default)
    {
        return _configurationService.GetConfigAsync(cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> SetSwitchProjectAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return _configurationService.UpdateBooleanSettingAsync("quota-exceeded/switch-project", enabled, cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> SetSwitchPreviewModelAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return _configurationService.UpdateBooleanSettingAsync("quota-exceeded/switch-preview-model", enabled, cancellationToken);
    }
}
