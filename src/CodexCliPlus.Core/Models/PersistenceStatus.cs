namespace CodexCliPlus.Core.Models;

public sealed class PersistenceStatus
{
    public string Directory { get; init; } = string.Empty;

    public bool UsesFallbackDirectory { get; init; }

    public DateTimeOffset? LastUsageSnapshotAt { get; init; }

    public DateTimeOffset? LastLogsSnapshotAt { get; init; }

    public string UsageDatabasePath { get; init; } = string.Empty;

    public string UsageBackupDirectory { get; init; } = string.Empty;

    public long UsageEventCount { get; init; }

    public DateTimeOffset? LastUsageEventAt { get; init; }

    public DateTimeOffset? LastUsageSyncAt { get; init; }

    public string? LastPersistenceError { get; init; }

    public bool UsesKeeperDatabase { get; init; }

    public string KeeperSourceCommit { get; init; } = string.Empty;
}
