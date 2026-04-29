namespace CodexCliPlus.Core.Models;

public sealed class PersistenceStatus
{
    public string Directory { get; init; } = string.Empty;

    public bool UsesFallbackDirectory { get; init; }

    public DateTimeOffset? LastUsageSnapshotAt { get; init; }

    public DateTimeOffset? LastLogsSnapshotAt { get; init; }
}
