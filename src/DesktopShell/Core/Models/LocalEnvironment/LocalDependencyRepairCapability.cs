namespace CodexCliPlus.Core.Models.LocalEnvironment;

public sealed class LocalDependencyRepairCapability
{
    public string ActionId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsAvailable { get; init; }

    public bool RequiresElevation { get; init; } = true;

    public bool IsOptional { get; init; }

    public string Detail { get; init; } = string.Empty;
}
