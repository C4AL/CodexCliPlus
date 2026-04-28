namespace CodexCliPlus.Core.Models;

public sealed class DependencyCheckIssue
{
    public string Code { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public bool CanRepairNow { get; init; }
}
