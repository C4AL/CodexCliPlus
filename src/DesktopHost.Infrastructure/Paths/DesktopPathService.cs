using DesktopHost.Core.Abstractions.Paths;
using DesktopHost.Core.Constants;
using DesktopHost.Core.Models;

namespace DesktopHost.Infrastructure.Paths;

public sealed class DesktopPathService : IPathService
{
    public DesktopPathService()
    {
        var rootDirectoryOverride = Environment.GetEnvironmentVariable("CPAD_APP_ROOT");
        var rootDirectory = string.IsNullOrWhiteSpace(rootDirectoryOverride)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.ProductName)
            : Path.GetFullPath(rootDirectoryOverride);

        var logsDirectory = Path.Combine(rootDirectory, "logs");
        var configDirectory = Path.Combine(rootDirectory, "config");
        var backendDirectory = Path.Combine(rootDirectory, "backend");
        var cacheDirectory = Path.Combine(rootDirectory, "cache");

        Directories = new AppDirectories(
            rootDirectory,
            logsDirectory,
            configDirectory,
            backendDirectory,
            cacheDirectory,
            Path.Combine(configDirectory, AppConstants.DesktopSettingsFileName),
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

        return Task.CompletedTask;
    }
}
