using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.Core.Abstractions.Management;

public interface IManagementConnectionProvider
{
    Task<ManagementConnectionInfo> GetConnectionAsync(
        CancellationToken cancellationToken = default
    );
}
