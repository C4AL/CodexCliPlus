using DesktopHost.Core.Enums;

namespace DesktopHost.Core.Models;

public sealed class BackendStatusSnapshot
{
    public BackendStateKind State { get; init; } = BackendStateKind.Stopped;

    public string Message { get; init; } = "后端尚未启动。";

    public string? LastError { get; init; }

    public BackendRuntimeInfo? Runtime { get; init; }

    public int? ProcessId { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
}
