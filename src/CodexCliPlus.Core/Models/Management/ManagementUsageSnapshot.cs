namespace CodexCliPlus.Core.Models.Management;

public sealed class ManagementUsageSnapshot
{
    public long TotalRequests { get; init; }

    public long SuccessCount { get; init; }

    public long FailureCount { get; init; }

    public long TotalTokens { get; init; }

    public IReadOnlyDictionary<string, ManagementUsageApiSnapshot> Apis { get; init; } =
        new Dictionary<string, ManagementUsageApiSnapshot>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, long> RequestsByDay { get; init; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, long> RequestsByHour { get; init; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, long> TokensByDay { get; init; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, long> TokensByHour { get; init; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
}

public sealed class ManagementUsageApiSnapshot
{
    public long TotalRequests { get; init; }

    public long TotalTokens { get; init; }

    public IReadOnlyDictionary<string, ManagementUsageModelSnapshot> Models { get; init; } =
        new Dictionary<string, ManagementUsageModelSnapshot>(StringComparer.OrdinalIgnoreCase);
}

public sealed class ManagementUsageModelSnapshot
{
    public long TotalRequests { get; init; }

    public long TotalTokens { get; init; }

    public IReadOnlyList<ManagementUsageRequestDetail> Details { get; init; } = [];
}

public sealed class ManagementUsageRequestDetail
{
    public DateTimeOffset? Timestamp { get; init; }

    public string? Source { get; init; }

    public string? AuthIndex { get; init; }

    public long? LatencyMs { get; init; }

    public bool Failed { get; init; }

    public ManagementUsageTokenStats Tokens { get; init; } = new();
}

public sealed class ManagementUsageTokenStats
{
    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long ReasoningTokens { get; init; }

    public long CachedTokens { get; init; }

    public long TotalTokens { get; init; }
}

public sealed class ManagementUsageExportPayload
{
    public int Version { get; init; }

    public DateTimeOffset? ExportedAt { get; init; }

    public ManagementUsageSnapshot Usage { get; init; } = new();
}

public sealed class ManagementUsageImportResult
{
    public int? Added { get; init; }

    public int? Skipped { get; init; }

    public long? TotalRequests { get; init; }

    public long? FailedRequests { get; init; }
}
