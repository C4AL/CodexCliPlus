namespace CodexCliPlus.Core.Models.LocalEnvironment;

public sealed class LocalDependencyRepairResult
{
    public string ActionId { get; init; } = string.Empty;

    public bool Succeeded { get; init; }

    public int? ExitCode { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string? LogPath { get; init; }
}
