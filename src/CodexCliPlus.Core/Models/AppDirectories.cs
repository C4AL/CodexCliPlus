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
    string BackendConfigFilePath)
{
    public AppDirectories(
        string rootDirectory,
        string logsDirectory,
        string configDirectory,
        string backendDirectory,
        string cacheDirectory,
        string settingsFilePath,
        string backendConfigFilePath)
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
            backendConfigFilePath)
    {
    }

    public string DesktopConfigFilePath => SettingsFilePath;
}
