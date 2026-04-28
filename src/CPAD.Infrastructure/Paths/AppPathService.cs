using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Constants;
using CPAD.Core.Enums;
using CPAD.Core.Models;

namespace CPAD.Infrastructure.Paths;

public sealed class AppPathService : IPathService
{
    private readonly bool _shouldAttemptLegacyMigration;

    public AppPathService()
    {
        var dataMode = ResolveDataMode();
        var rootDirectoryOverride = ResolveRootDirectoryOverride();
        var rootDirectory = string.IsNullOrWhiteSpace(rootDirectoryOverride)
            ? ResolveDefaultRootDirectory(dataMode)
            : Path.GetFullPath(rootDirectoryOverride);
        _shouldAttemptLegacyMigration = dataMode == AppDataMode.Installed &&
            string.IsNullOrWhiteSpace(rootDirectoryOverride);

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

        if (_shouldAttemptLegacyMigration)
        {
            TryMigrateLegacyInstalledRoot();
        }

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
        var modeOverride = Environment.GetEnvironmentVariable("CODEXCLIPLUS_APP_MODE");
        if (string.IsNullOrWhiteSpace(modeOverride))
        {
            modeOverride = Environment.GetEnvironmentVariable("CPAD_APP_MODE");
        }

        return modeOverride?.Trim().ToLowerInvariant() switch
        {
            "portable" => AppDataMode.Portable,
            "development" => AppDataMode.Development,
            _ => ResolveDataModeFromPackageMarker()
        };
    }

    private static string? ResolveRootDirectoryOverride()
    {
        var rootDirectoryOverride = Environment.GetEnvironmentVariable("CODEXCLIPLUS_APP_ROOT");
        return string.IsNullOrWhiteSpace(rootDirectoryOverride)
            ? Environment.GetEnvironmentVariable("CPAD_APP_ROOT")
            : rootDirectoryOverride;
    }

    private static AppDataMode ResolveDataModeFromPackageMarker()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(baseDirectory, "portable-mode.json")))
        {
            return AppDataMode.Portable;
        }

        if (File.Exists(Path.Combine(baseDirectory, "dev-mode.json")))
        {
            return AppDataMode.Development;
        }

        return AppDataMode.Installed;
    }

    private static string ResolveDefaultRootDirectory(AppDataMode dataMode)
    {
        return dataMode switch
        {
            AppDataMode.Portable => Path.Combine(AppContext.BaseDirectory, "data"),
            AppDataMode.Development => ResolveDevelopmentRootDirectory(),
            _ => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.ProductKey)
        };
    }

    private void TryMigrateLegacyInstalledRoot()
    {
        if (Directory.Exists(Directories.RootDirectory))
        {
            return;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return;
        }

        var legacyRoot = Path.Combine(localAppData, AppConstants.LegacyProductKey);
        if (!Directory.Exists(legacyRoot))
        {
            return;
        }

        CopyDirectory(legacyRoot, Directories.RootDirectory);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(filePath, Path.Combine(targetDirectory, Path.GetFileName(filePath)), overwrite: false);
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(
                directoryPath,
                Path.Combine(targetDirectory, Path.GetFileName(directoryPath)));
        }
    }

    private static string ResolveDevelopmentRootDirectory()
    {
        var repositoryRoot = TryResolveRepositoryRoot();
        return repositoryRoot is null
            ? Path.Combine(AppContext.BaseDirectory, "artifacts", "dev-data")
            : Path.Combine(repositoryRoot, "artifacts", "dev-data");
    }

    private static string? TryResolveRepositoryRoot()
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

        return null;
    }
}
