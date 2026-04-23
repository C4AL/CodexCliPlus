using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Constants;
using CPAD.Core.Enums;
using CPAD.Core.Models;

namespace CPAD.Infrastructure.Paths;

public sealed class AppPathService : IPathService
{
    public AppPathService()
    {
        var dataMode = ResolveDataMode();
        var rootDirectoryOverride = Environment.GetEnvironmentVariable("CPAD_APP_ROOT");
        var rootDirectory = string.IsNullOrWhiteSpace(rootDirectoryOverride)
            ? ResolveDefaultRootDirectory(dataMode)
            : Path.GetFullPath(rootDirectoryOverride);

        var logsDirectory = Path.Combine(rootDirectory, "logs");
        var configDirectory = Path.Combine(rootDirectory, "config");
        var backendDirectory = Path.Combine(rootDirectory, "backend");
        var cacheDirectory = Path.Combine(rootDirectory, "cache");
        var diagnosticsDirectory = Path.Combine(rootDirectory, "diagnostics");
        var runtimeDirectory = Path.Combine(rootDirectory, "runtime");

        Directories = new AppDirectories(
            dataMode,
            rootDirectory,
            logsDirectory,
            configDirectory,
            backendDirectory,
            cacheDirectory,
            diagnosticsDirectory,
            runtimeDirectory,
            Path.Combine(configDirectory, AppConstants.AppSettingsFileName),
            Path.Combine(configDirectory, AppConstants.BackendConfigFileName));
    }

    public AppDirectories Directories { get; }

    public Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(Directories.RootDirectory);
        Directory.CreateDirectory(Directories.LogsDirectory);
        Directory.CreateDirectory(Directories.ConfigDirectory);
        Directory.CreateDirectory(Directories.BackendDirectory);
        Directory.CreateDirectory(Directories.CacheDirectory);
        Directory.CreateDirectory(Directories.DiagnosticsDirectory);
        Directory.CreateDirectory(Directories.RuntimeDirectory);

        return Task.CompletedTask;
    }

    private static AppDataMode ResolveDataMode()
    {
        var modeOverride = Environment.GetEnvironmentVariable("CPAD_APP_MODE");
        return modeOverride?.Trim().ToLowerInvariant() switch
        {
            "portable" => AppDataMode.Portable,
            "development" => AppDataMode.Development,
            _ => AppDataMode.Installed
        };
    }

    private static string ResolveDefaultRootDirectory(AppDataMode dataMode)
    {
        return dataMode switch
        {
            AppDataMode.Portable => Path.Combine(AppContext.BaseDirectory, "data"),
            AppDataMode.Development => Path.Combine(ResolveRepositoryRoot(), "artifacts", "dev-data"),
            _ => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.ProductKey)
        };
    }

    private static string ResolveRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "CliProxyApiDesktop.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
