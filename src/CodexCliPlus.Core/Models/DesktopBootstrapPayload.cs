namespace CodexCliPlus.Core.Models;

public sealed class DesktopBootstrapPayload
{
    public required bool DesktopMode { get; init; }

    public required string ApiBase { get; init; }

    public required string DesktopSessionId { get; init; }

    public string Theme { get; init; } = "auto";

    public string ResolvedTheme { get; init; } = "light";

    public bool SidebarCollapsed { get; init; }
}
