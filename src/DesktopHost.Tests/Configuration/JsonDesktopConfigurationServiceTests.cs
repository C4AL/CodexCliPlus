using DesktopHost.Core.Abstractions.Paths;
using DesktopHost.Core.Enums;
using DesktopHost.Core.Models;
using DesktopHost.Infrastructure.Configuration;

namespace DesktopHost.Tests.Configuration;

public sealed class JsonDesktopConfigurationServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"cpad-config-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoadAsyncRoundTripsDesktopSettings()
    {
        var pathService = new TestPathService(_rootDirectory);
        var service = new JsonDesktopConfigurationService(pathService);
        var expected = new DesktopSettings
        {
            OnboardingCompleted = true,
            BackendPort = 9417,
            ManagementKey = "test-key",
            PreferredCodexSource = CodexSourceKind.Cpa,
            EnableDebugTools = true,
            LastRepositoryPath = @"C:\repo"
        };

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();

        Assert.True(actual.OnboardingCompleted);
        Assert.Equal(9417, actual.BackendPort);
        Assert.Equal("test-key", actual.ManagementKey);
        Assert.Equal(CodexSourceKind.Cpa, actual.PreferredCodexSource);
        Assert.True(actual.EnableDebugTools);
        Assert.Equal(@"C:\repo", actual.LastRepositoryPath);
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

            return Task.CompletedTask;
        }
    }
}
