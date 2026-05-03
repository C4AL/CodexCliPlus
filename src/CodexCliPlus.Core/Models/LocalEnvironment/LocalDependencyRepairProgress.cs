namespace CodexCliPlus.Core.Models.LocalEnvironment;

public sealed class LocalDependencyRepairProgress
{
    public string ActionId { get; init; } = string.Empty;

    public string Phase { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? CommandLine { get; init; }

    public IReadOnlyList<string> RecentOutput { get; init; } = [];

    public string? LogPath { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public int? ExitCode { get; init; }

    public bool IsCompleted { get; init; }

    public bool Succeeded { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;
}
