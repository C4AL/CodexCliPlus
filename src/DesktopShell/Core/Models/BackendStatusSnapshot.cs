using CodexCliPlus.Core.Enums;

namespace CodexCliPlus.Core.Models;

public sealed class BackendStatusSnapshot
{
    public BackendStateKind State { get; init; } = BackendStateKind.Stopped;

    public string Message { get; init; } = "Backend is stopped.";

    public string? LastError { get; init; }

    public BackendRuntimeInfo? Runtime { get; init; }

    public int? ProcessId { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
}
