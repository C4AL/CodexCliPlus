namespace CPAD.Core.Models;

public sealed class DependencyCheckResult
{
    public bool IsAvailable { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string? Detail { get; init; }
}
