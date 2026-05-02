namespace CodexCliPlus.Core.Models;

public sealed record CodexRouteState
{
    public string CurrentMode { get; init; } = "unknown";

    public string? TargetMode { get; init; }

    public string ConfigPath { get; init; } = string.Empty;

    public string AuthPath { get; init; } = string.Empty;

    public bool CanSwitch { get; init; }

    public string StatusMessage { get; init; } = string.Empty;
}

public sealed record CodexRouteSwitchResult
{
    public bool Succeeded { get; init; }

    public CodexRouteState State { get; init; } = new();

    public string? ConfigBackupPath { get; init; }

    public string? AuthBackupPath { get; init; }

    public string? OfficialAuthBackupPath { get; init; }

    public string? ErrorMessage { get; init; }
}
