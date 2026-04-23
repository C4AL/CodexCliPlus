namespace DesktopHost.Core.Models;

public sealed record AppDirectories(
    string RootDirectory,
    string LogsDirectory,
    string ConfigDirectory,
    string BackendDirectory,
    string CacheDirectory,
    string DesktopConfigFilePath,
    string BackendConfigFilePath);
