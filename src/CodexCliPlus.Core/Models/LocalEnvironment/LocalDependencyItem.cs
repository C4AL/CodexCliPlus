namespace CodexCliPlus.Core.Models.LocalEnvironment;

public sealed class LocalDependencyItem
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public LocalDependencyStatus Status { get; init; }

    public LocalDependencySeverity Severity { get; init; }

    public string? Version { get; init; }

    public string? Path { get; init; }

    public string Detail { get; init; } = string.Empty;

    public string Recommendation { get; init; } = string.Empty;

    public string? RepairActionId { get; init; }
}
