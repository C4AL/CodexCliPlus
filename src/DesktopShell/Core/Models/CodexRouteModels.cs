namespace CodexCliPlus.Core.Models;

public sealed record CodexRouteState
{
    public string CurrentMode { get; init; } = "unknown";

    public string? TargetMode { get; init; }

    public string? CurrentTargetId { get; init; }

    public string CurrentLabel { get; init; } = "未知模式";

    public IReadOnlyList<CodexRouteTarget> Targets { get; init; } = Array.Empty<CodexRouteTarget>();

    public string ConfigPath { get; init; } = string.Empty;

    public string AuthPath { get; init; } = string.Empty;

    public bool CanSwitch { get; init; }

    public string StatusMessage { get; init; } = string.Empty;
}

public sealed record CodexRouteTarget
{
    public string Id { get; init; } = string.Empty;

    public string Mode { get; init; } = "unknown";

    public string Kind { get; init; } = "unknown";

    public string Label { get; init; } = string.Empty;

    public string? BaseUrl { get; init; }

    public int? Port { get; init; }

    public string? ProfileName { get; init; }

    public string? ProviderName { get; init; }

    public bool IsCurrent { get; init; }

    public bool CanSwitch { get; init; } = true;

    public string? StatusMessage { get; init; }
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
