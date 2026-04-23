using CPAD.Core.Models.Management;

namespace CPAD.Core.Abstractions.Management;

public interface IManagementConnectionProvider
{
    Task<ManagementConnectionInfo> GetConnectionAsync(CancellationToken cancellationToken = default);
}
