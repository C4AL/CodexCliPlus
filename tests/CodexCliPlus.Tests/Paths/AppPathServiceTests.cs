using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Infrastructure.Paths;

namespace CodexCliPlus.Tests.Paths;

[Collection("AppPathServiceEnvironment")]
public sealed class AppPathServiceTests
{
    [Fact]
    public void DirectoriesUseExpectedLocalApplicationDataLayout()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        var service = new AppPathService();

        Assert.Equal(AppDataMode.Installed, service.Directories.DataMode);
        Assert.Contains(AppConstants.ProductKey, service.Directories.RootDirectory);
        Assert.EndsWith(AppConstants.AppSettingsFileName, service.Directories.SettingsFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(AppConstants.BackendConfigFileName, service.Directories.BackendConfigFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("logs", service.Directories.LogsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagnostics", service.Directories.DiagnosticsDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectoriesUseOverrideRootWhenEnvironmentVariableIsSetEvenWhenPackageMarkerExists()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        var overrideRoot = scope.CreateTemporaryRoot("codexcliplus-path-override");
        AppPathServiceEnvironmentScope.SetMarker(AppPathServiceEnvironmentScope.PortableMarkerFileName);
        AppPathServiceEnvironmentScope.SetRootOverride(overrideRoot);

        var service = new AppPathService();

        Assert.Equal(AppDataMode.Portable, service.Directories.DataMode);
        Assert.Equal(Path.GetFullPath(overrideRoot), service.Directories.RootDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "logs"), service.Directories.LogsDirectory);
    }

    [Fact]
    public void DirectoriesUsePortableModeWhenPortablePackageMarkerExists()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        AppPathServiceEnvironmentScope.SetMarker(AppPathServiceEnvironmentScope.PortableMarkerFileName);

        var service = new AppPathService();

        Assert.Equal(AppDataMode.Portable, service.Directories.DataMode);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "data"), service.Directories.RootDirectory);
    }

    [Fact]
    public void DirectoriesUseDevelopmentModeWhenDevelopmentPackageMarkerExists()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        AppPathServiceEnvironmentScope.SetMarker(AppPathServiceEnvironmentScope.DevelopmentMarkerFileName);

        var service = new AppPathService();

        Assert.Equal(AppDataMode.Development, service.Directories.DataMode);
        Assert.Contains(Path.Combine("artifacts", "dev-data"), service.Directories.RootDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectoriesUseDevelopmentModeWhenRequestedEvenWhenPortableMarkerExists()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        AppPathServiceEnvironmentScope.SetMarker(AppPathServiceEnvironmentScope.PortableMarkerFileName);
        AppPathServiceEnvironmentScope.SetModeOverride("development");

        var service = new AppPathService();

        Assert.Equal(AppDataMode.Development, service.Directories.DataMode);
        Assert.Contains(Path.Combine("artifacts", "dev-data"), service.Directories.RootDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectoriesUsePortableModeWhenRequestedEvenWhenDevelopmentMarkerExists()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        AppPathServiceEnvironmentScope.SetMarker(AppPathServiceEnvironmentScope.DevelopmentMarkerFileName);
        AppPathServiceEnvironmentScope.SetModeOverride("portable");

        var service = new AppPathService();

        Assert.Equal(AppDataMode.Portable, service.Directories.DataMode);
        Assert.EndsWith(Path.Combine("data"), service.Directories.RootDirectory, StringComparison.OrdinalIgnoreCase);
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
    public const string PortableMarkerFileName = "portable-mode.json";
    public const string DevelopmentMarkerFileName = "dev-mode.json";

    private static readonly string[] MarkerFileNames =
    [
        PortableMarkerFileName,
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
}
