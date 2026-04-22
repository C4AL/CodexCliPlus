using CPAD.Contracts;

namespace CPAD.Application.Abstractions;

public interface IHostSnapshotService
{
    Task<HostSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
