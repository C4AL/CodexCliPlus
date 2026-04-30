namespace CodexCliPlus.Core.Models.LocalEnvironment;

public sealed class LocalDependencySnapshot
{
    public DateTimeOffset CheckedAt { get; init; }

    public int ReadinessScore { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<LocalDependencyItem> Items { get; init; } = [];

    public IReadOnlyList<LocalDependencyRepairCapability> RepairCapabilities { get; init; } = [];
}
