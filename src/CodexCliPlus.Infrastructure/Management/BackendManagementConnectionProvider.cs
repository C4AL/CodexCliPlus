using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Infrastructure.Backend;

namespace CodexCliPlus.Infrastructure.Management;

public sealed class BackendManagementConnectionProvider : IManagementConnectionProvider
{
    private readonly BackendProcessManager _backendProcessManager;

    public BackendManagementConnectionProvider(BackendProcessManager backendProcessManager)
    {
        _backendProcessManager = backendProcessManager;
    }

    public async Task<ManagementConnectionInfo> GetConnectionAsync(
        CancellationToken cancellationToken = default
    )
    {
        var status = _backendProcessManager.CurrentStatus;
        if (status.Runtime is null || status.State != Core.Enums.BackendStateKind.Running)
        {
            status = await _backendProcessManager.StartAsync(cancellationToken);
        }

        if (status.Runtime is null || status.State != Core.Enums.BackendStateKind.Running)
        {
            throw new InvalidOperationException(
                status.LastError ?? "CLIProxyAPI backend is not available."
            );
        }

        return new ManagementConnectionInfo
        {
            BaseUrl = status.Runtime.BaseUrl,
            ManagementApiBaseUrl = status.Runtime.ManagementApiBaseUrl,
            ManagementKey = status.Runtime.ManagementKey,
        };
    }
}
