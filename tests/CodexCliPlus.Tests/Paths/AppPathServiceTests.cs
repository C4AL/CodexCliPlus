using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Infrastructure.Paths;

namespace CodexCliPlus.Tests.Paths;

[Collection("AppPathServiceEnvironment")]
public sealed class AppPathServiceTests
{
    [Fact]
    public void DirectoriesUseExpectedApplicationDirectoryLayout()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        var service = new AppPathService();

        Assert.Equal(AppDataMode.Installed, service.Directories.DataMode);
        Assert.Equal(
            Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(service.Directories.RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            ignoreCase: true);
        Assert.EndsWith(AppConstants.AppSettingsFileName, service.Directories.SettingsFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(AppConstants.BackendConfigFileName, service.Directories.BackendConfigFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("config", "appsettings.json"), service.Directories.SettingsFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("config", "backend.yaml"), service.Directories.BackendConfigFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("logs", service.Directories.LogsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagnostics", service.Directories.DiagnosticsDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectoriesUseOverrideRootWhenEnvironmentVariableIsSet()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        var overrideRoot = scope.CreateTemporaryRoot("codexcliplus-path-override");
        AppPathServiceEnvironmentScope.SetRootOverride(overrideRoot);

        var service = new AppPathService();

        Assert.Equal(AppDataMode.Installed, service.Directories.DataMode);
        Assert.Equal(Path.GetFullPath(overrideRoot), service.Directories.RootDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "logs"), service.Directories.LogsDirectory);
    }

    [Fact]
    public void DirectoriesUseApplicationDirectoryWhenPackageMarkersExist()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        AppPathServiceEnvironmentScope.SetMarker(AppPathServiceEnvironmentScope.DevelopmentMarkerFileName);

        var service = new AppPathService();

        Assert.Equal(AppDataMode.Installed, service.Directories.DataMode);
        Assert.Equal(
            Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(service.Directories.RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            ignoreCase: true);
    }

    [Fact]
    public void DirectoriesUseDevelopmentModeWhenRequested()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        AppPathServiceEnvironmentScope.SetModeOverride("development");

        var service = new AppPathService();

        Assert.Equal(AppDataMode.Development, service.Directories.DataMode);
        Assert.Contains(Path.Combine("artifacts", "dev-data"), service.Directories.RootDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownModeOverrideIsIgnoredForInstalledOnlyBuilds()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        AppPathServiceEnvironmentScope.SetModeOverride("sandbox");

        var service = new AppPathService();

        Assert.Equal(AppDataMode.Installed, service.Directories.DataMode);
    }

    [Fact]
    public void PathServiceContainsLegacyLocalAppDataMigrationForOldConfigNames()
    {
        var repositoryRoot = AppPathServiceEnvironmentScope.FindRepositoryRoot();
        var source = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.Infrastructure", "Paths", "AppPathService.cs"));

        Assert.Contains("TryMigrateLegacyLocalAppDataConfiguration", source, StringComparison.Ordinal);
        Assert.Contains("LegacyAppSettingsFileName", source, StringComparison.Ordinal);
        Assert.Contains("LegacyBackendConfigFileName", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureCreatedAsyncCreatesAllManagedDirectories()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        var overrideRoot = scope.CreateTemporaryRoot("codexcliplus-path-create");
        AppPathServiceEnvironmentScope.SetRootOverride(overrideRoot);
        var service = new AppPathService();

        await service.EnsureCreatedAsync();

        Assert.True(Directory.Exists(service.Directories.ConfigDirectory));
        Assert.True(Directory.Exists(service.Directories.CacheDirectory));
        Assert.True(Directory.Exists(service.Directories.LogsDirectory));
        Assert.True(Directory.Exists(service.Directories.DiagnosticsDirectory));
        Assert.True(Directory.Exists(service.Directories.RuntimeDirectory));
    }
}

[CollectionDefinition("AppPathServiceEnvironment", DisableParallelization = true)]
public sealed class AppPathServiceEnvironmentDefinition;

internal sealed class AppPathServiceEnvironmentScope : IDisposable
{
    public const string DevelopmentMarkerFileName = "dev-mode.json";

    private static readonly string[] MarkerFileNames =
    [
        DevelopmentMarkerFileName
    ];

    private readonly string? _originalMode;
    private readonly string? _originalRoot;
    private readonly Dictionary<string, string?> _markerSnapshots;
    private readonly List<string> _temporaryRoots = [];

    public AppPathServiceEnvironmentScope()
    {
        _originalMode = Environment.GetEnvironmentVariable("CODEXCLIPLUS_APP_MODE");
        _originalRoot = Environment.GetEnvironmentVariable("CODEXCLIPLUS_APP_ROOT");
        _markerSnapshots = MarkerFileNames.ToDictionary(
            markerFileName => markerFileName,
            CaptureMarkerContent,
            StringComparer.OrdinalIgnoreCase);

        SetModeOverride(null);
        SetRootOverride(null);
        ClearMarkers();
    }

    public string CreateTemporaryRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        _temporaryRoots.Add(root);
        return root;
    }

    public static void SetModeOverride(string? value)
    {
        Environment.SetEnvironmentVariable("CODEXCLIPLUS_APP_MODE", value);
    }

    public static void SetRootOverride(string? value)
    {
        Environment.SetEnvironmentVariable("CODEXCLIPLUS_APP_ROOT", value);
    }

    public static void SetMarker(string markerFileName)
    {
        ClearMarkers();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, markerFileName), "{}");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CODEXCLIPLUS_APP_MODE", _originalMode);
        Environment.SetEnvironmentVariable("CODEXCLIPLUS_APP_ROOT", _originalRoot);
        RestoreMarkers();

        foreach (var temporaryRoot in _temporaryRoots)
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    private static void ClearMarkers()
    {
        foreach (var markerFileName in MarkerFileNames)
        {
            var markerPath = Path.Combine(AppContext.BaseDirectory, markerFileName);
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }
        }
    }

    private void RestoreMarkers()
    {
        foreach (var markerSnapshot in _markerSnapshots)
        {
            var markerPath = Path.Combine(AppContext.BaseDirectory, markerSnapshot.Key);
            if (markerSnapshot.Value is null)
            {
                if (File.Exists(markerPath))
                {
                    File.Delete(markerPath);
                }

                continue;
            }

            File.WriteAllText(markerPath, markerSnapshot.Value);
        }
    }

    private static string? CaptureMarkerContent(string markerFileName)
    {
        var markerPath = Path.Combine(AppContext.BaseDirectory, markerFileName);
        return File.Exists(markerPath)
            ? File.ReadAllText(markerPath)
            : null;
    }

    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodexCliPlus.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
