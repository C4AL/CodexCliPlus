using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;

namespace CodexCliPlus.Core.Models;

public sealed class AppSettings
{
    public int BackendPort { get; set; } = AppConstants.DefaultBackendPort;

    public string ManagementKey { get; set; } = string.Empty;

    public string ManagementKeyReference { get; set; } = AppConstants.DefaultManagementKeyReference;

    public bool RememberManagementKey { get; set; }

    public CodexSourceKind PreferredCodexSource { get; set; } = CodexSourceKind.Official;

    public bool StartWithWindows { get; set; }

    public bool MinimizeToTrayOnClose { get; set; } = true;

    public bool EnableTrayIcon { get; set; } = true;

    public bool CheckForUpdatesOnStartup { get; set; } = true;

    public bool UseBetaChannel { get; set; }

    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;

    public AppLogLevel MinimumLogLevel { get; set; } = AppLogLevel.Information;

    public bool EnableDebugTools { get; set; }

    public string? LastRepositoryPath { get; set; }

    public bool SecurityKeyOnboardingCompleted { get; set; }

    public string? LastSeenApplicationVersion { get; set; }
}
