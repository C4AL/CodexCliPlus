using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;

namespace CodexCliPlus.Infrastructure.Paths;

public sealed class AppPathService : IPathService
{
    private readonly bool _usesRootOverride;
    private bool _legacyMigrationAttempted;

    public AppPathService()
    {
        var dataMode = ResolveDataMode();
        var rootDirectoryOverride = Environment.GetEnvironmentVariable("CODEXCLIPLUS_APP_ROOT");
        _usesRootOverride = !string.IsNullOrWhiteSpace(rootDirectoryOverride);
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
        TryMigrateLegacyLocalAppDataConfiguration();

        return Task.CompletedTask;
    }

    private static AppDataMode ResolveDataMode()
    {
        var modeOverride = Environment.GetEnvironmentVariable("CODEXCLIPLUS_APP_MODE");
        return modeOverride?.Trim().ToLowerInvariant() switch
        {
            "development" => AppDataMode.Development,
            _ => AppDataMode.Installed
        };
    }

    private static string ResolveDefaultRootDirectory(AppDataMode dataMode)
    {
        return dataMode switch
        {
            AppDataMode.Development => ResolveDevelopmentRootDirectory(),
            _ => AppContext.BaseDirectory
        };
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
            if (File.Exists(Path.Combine(currentDirectory.FullName, "CodexCliPlus.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }

    private void TryMigrateLegacyLocalAppDataConfiguration()
    {
        if (_legacyMigrationAttempted ||
            _usesRootOverride ||
            Directories.DataMode != AppDataMode.Installed ||
            HasNewConfiguration())
        {
            _legacyMigrationAttempted = true;
            return;
        }

        _legacyMigrationAttempted = true;
        var legacyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppConstants.ProductKey);
        if (string.IsNullOrWhiteSpace(legacyRoot) ||
            !Directory.Exists(legacyRoot) ||
            IsSameDirectory(legacyRoot, Directories.RootDirectory))
        {
            return;
        }

        CopyLegacyFile(
            Path.Combine(legacyRoot, "config", AppConstants.LegacyAppSettingsFileName),
            Directories.SettingsFilePath);
        CopyLegacyFile(
            Path.Combine(legacyRoot, "config", AppConstants.LegacyBackendConfigFileName),
            Directories.BackendConfigFilePath);
        CopyLegacyDirectory(
            Path.Combine(legacyRoot, "config", AppConstants.SecretsDirectoryName),
            Path.Combine(Directories.ConfigDirectory, AppConstants.SecretsDirectoryName));
        CopyLegacyDirectory(Path.Combine(legacyRoot, "backend", "auth"), Path.Combine(Directories.BackendDirectory, "auth"));
    }

    private bool HasNewConfiguration()
    {
        return File.Exists(Directories.SettingsFilePath) ||
            File.Exists(Directories.BackendConfigFilePath) ||
            Directory.Exists(Path.Combine(Directories.ConfigDirectory, AppConstants.SecretsDirectoryName)) ||
            Directory.Exists(Path.Combine(Directories.BackendDirectory, "auth"));
    }

    private static void CopyLegacyFile(string source, string target)
    {
        if (!File.Exists(source) || File.Exists(target))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: false);
    }

    private static void CopyLegacyDirectory(string source, string target)
    {
        if (!Directory.Exists(source) || Directory.Exists(target))
        {
            return;
        }

        Directory.CreateDirectory(target);
        foreach (var sourcePath in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, sourcePath);
            var targetPath = Path.Combine(target, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath, overwrite: false);
        }
    }

    private static bool IsSameDirectory(string first, string second)
    {
        return string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
