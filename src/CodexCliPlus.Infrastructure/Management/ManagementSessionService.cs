using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Infrastructure.Backend;

namespace CodexCliPlus.Infrastructure.Management;

public sealed class ManagementSessionService : IManagementSessionService
{
    private readonly BackendProcessManager _backendProcessManager;
    private readonly IManagementConnectionProvider _connectionProvider;

    public ManagementSessionService(
        BackendProcessManager backendProcessManager,
        IManagementConnectionProvider connectionProvider
    )
    {
        _backendProcessManager = backendProcessManager;
        _connectionProvider = connectionProvider;
    }

    public async Task<ManagementConnectionInfo> GetConnectionAsync(
        CancellationToken cancellationToken = default
    )
    {
        var runtime = _backendProcessManager.CurrentStatus.Runtime;
        if (runtime is not null)
        {
            return new ManagementConnectionInfo
            {
                BaseUrl = runtime.BaseUrl,
                ManagementApiBaseUrl = runtime.ManagementApiBaseUrl,
                ManagementKey = runtime.ManagementKey,
            };
        }

        return await _connectionProvider.GetConnectionAsync(cancellationToken);
    }
}
