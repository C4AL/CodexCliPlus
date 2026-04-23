using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Constants;
using CPAD.Core.Enums;
using CPAD.Core.Models;
using CPAD.Infrastructure.Configuration;

namespace CPAD.Tests.Configuration;

public sealed class JsonAppConfigurationServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"cpad-config-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoadAsyncStoresManagementKeyOutsideDesktopJson()
    {
        var pathService = new TestPathService(_rootDirectory);
        var service = new JsonAppConfigurationService(pathService);
        var expected = new AppSettings
        {
            BackendPort = 9417,
            ManagementKey = "test-key",
            ManagementKeyReference = "desktop-management-key",
            PreferredCodexSource = CodexSourceKind.Cpa,
            ThemeMode = AppThemeMode.Dark,
            MinimumLogLevel = AppLogLevel.Warning,
            EnableDebugTools = true,
            LastRepositoryPath = @"C:\repo"
        };

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();
        var persistedJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);

        Assert.Equal(9417, actual.BackendPort);
        Assert.Equal("test-key", actual.ManagementKey);
        Assert.Equal("desktop-management-key", actual.ManagementKeyReference);
        Assert.Equal(CodexSourceKind.Cpa, actual.PreferredCodexSource);
        Assert.Equal(AppThemeMode.Dark, actual.ThemeMode);
        Assert.Equal(AppLogLevel.Warning, actual.MinimumLogLevel);
        Assert.True(actual.EnableDebugTools);
        Assert.Equal(@"C:\repo", actual.LastRepositoryPath);
        Assert.DoesNotContain("test-key", persistedJson, StringComparison.Ordinal);
        Assert.Contains("managementKeyReference", persistedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsyncMigratesLegacyPlaintextManagementKey()
    {
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();

        await File.WriteAllTextAsync(
            pathService.Directories.SettingsFilePath,
            """
            {
              "backendPort": 9527,
              "managementKey": "legacy-secret",
              "themeMode": "Light"
            }
            """);

        var service = new JsonAppConfigurationService(pathService);
        var settings = await service.LoadAsync();
        var persistedJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);

        Assert.Equal("legacy-secret", settings.ManagementKey);
        Assert.Equal(AppConstants.DefaultManagementKeyReference, settings.ManagementKeyReference);
        Assert.DoesNotContain("legacy-secret", persistedJson, StringComparison.Ordinal);
        Assert.Contains("managementKeyReference", persistedJson, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(pathService.Directories.ConfigDirectory, "secrets", "management-key.bin")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private sealed class TestPathService : IPathService
    {
        public TestPathService(string rootDirectory)
        {
            Directories = new AppDirectories(
                rootDirectory,
                Path.Combine(rootDirectory, "logs"),
                Path.Combine(rootDirectory, "config"),
                Path.Combine(rootDirectory, "backend"),
                Path.Combine(rootDirectory, "cache"),
                Path.Combine(rootDirectory, "config", "desktop.json"),
                Path.Combine(rootDirectory, "config", "cliproxyapi.yaml"));
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
    }
}
