using CodexCliPlus.Core.Models;

namespace CodexCliPlus.Core.Abstractions.Persistence;

public interface IManagementPersistenceService
{
    Task ImportNewerUsageSnapshotAsync(CancellationToken cancellationToken = default);

    Task SyncUsageSnapshotAsync(CancellationToken cancellationToken = default);

    Task SyncLogsSnapshotAsync(CancellationToken cancellationToken = default);

    Task ClearUsageSnapshotAsync(CancellationToken cancellationToken = default);

    PersistenceStatus GetStatus();
}
