namespace CodexCliPlus.Core.Models;

public sealed class DependencyCheckResult
{
    public bool IsAvailable { get; init; }

    public bool RequiresRepairMode { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string? Detail { get; init; }

    public IReadOnlyList<DependencyCheckIssue> Issues { get; init; } = [];
}
