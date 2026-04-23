namespace DesktopHost.Core.Models;

public sealed record BackendAssetLayout(
    string WorkingDirectory,
    string ExecutablePath,
    string StaticDirectory,
    string ManagementHtmlPath);
