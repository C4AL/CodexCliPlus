namespace CPAD.Core.Models.Management;

public sealed class ManagementLogsSnapshot
{
    public IReadOnlyList<string> Lines { get; init; } = [];

    public int LineCount { get; init; }

    public long LatestTimestamp { get; init; }
}

public sealed class ManagementErrorLogFile
{
    public string Name { get; init; } = string.Empty;

    public long? Size { get; init; }

    public long? Modified { get; init; }
}
