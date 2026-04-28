namespace CodexCliPlus.Core.Models;

public sealed class CodexLaunchResult
{
    public bool IsSuccess { get; init; }

    public string Command { get; init; } = string.Empty;

    public string? ErrorMessage { get; init; }
}
