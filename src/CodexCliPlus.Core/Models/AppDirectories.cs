using CodexCliPlus.Core.Enums;

namespace CodexCliPlus.Core.Models;

public sealed record AppDirectories(
    AppDataMode DataMode,
    string RootDirectory,
    string LogsDirectory,
    string ConfigDirectory,
    string BackendDirectory,
    string CacheDirectory,
    string DiagnosticsDirectory,
    string RuntimeDirectory,
    string SettingsFilePath,
    string BackendConfigFilePath,
    string PersistenceDirectory = ""
)
{
    public AppDirectories(
        string rootDirectory,
        string logsDirectory,
        string configDirectory,
        string backendDirectory,
        string cacheDirectory,
        string settingsFilePath,
        string backendConfigFilePath
    )
        : this(
            AppDataMode.Installed,
            rootDirectory,
            logsDirectory,
            configDirectory,
            backendDirectory,
            cacheDirectory,
            Path.Combine(rootDirectory, "diagnostics"),
            Path.Combine(rootDirectory, "runtime"),
            settingsFilePath,
            backendConfigFilePath,
            Path.Combine(rootDirectory, "persistence")
        ) { }

    public string DesktopConfigFilePath => SettingsFilePath;

    public bool UsesPersistenceFallback =>
        !string.IsNullOrWhiteSpace(PersistenceDirectory)
        && !Path.GetFullPath(PersistenceDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(Path.Combine(RootDirectory, "persistence"))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase
            );
}
