using CPAD.Core.Abstractions.Management;
using CPAD.Core.Models.Management;
using CPAD.Infrastructure.Backend;

namespace CPAD.Infrastructure.Management;

public sealed class ManagementSessionService : IManagementSessionService
{
    private readonly BackendProcessManager _backendProcessManager;
    private readonly IManagementConnectionProvider _connectionProvider;

    public ManagementSessionService(
        BackendProcessManager backendProcessManager,
        IManagementConnectionProvider connectionProvider)
    {
        _backendProcessManager = backendProcessManager;
        _connectionProvider = connectionProvider;
    }

    public async Task<ManagementConnectionInfo> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        var runtime = _backendProcessManager.CurrentStatus.Runtime;
        if (runtime is not null)
        {
            return new ManagementConnectionInfo
            {
                BaseUrl = runtime.BaseUrl,
                ManagementApiBaseUrl = runtime.ManagementApiBaseUrl,
                ManagementKey = runtime.ManagementKey
            };
        }

        return await _connectionProvider.GetConnectionAsync(cancellationToken);
    }
}
