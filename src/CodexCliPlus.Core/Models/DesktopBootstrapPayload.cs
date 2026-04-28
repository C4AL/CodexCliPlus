namespace CodexCliPlus.Core.Models;

public sealed class DesktopBootstrapPayload
{
    public required bool DesktopMode { get; init; }

    public required string ApiBase { get; init; }

    public required string ManagementKey { get; init; }
}
