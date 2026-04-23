using DesktopHost.Core.Constants;
using DesktopHost.Core.Enums;

namespace DesktopHost.Core.Models;

public sealed class DesktopSettings
{
    public bool OnboardingCompleted { get; set; }

    public int BackendPort { get; set; } = AppConstants.DefaultBackendPort;

    public string ManagementKey { get; set; } = string.Empty;

    public CodexSourceKind PreferredCodexSource { get; set; } = CodexSourceKind.Official;

    public bool StartWithWindows { get; set; }

    public bool EnableDebugTools { get; set; }

    public string? LastRepositoryPath { get; set; }
}
