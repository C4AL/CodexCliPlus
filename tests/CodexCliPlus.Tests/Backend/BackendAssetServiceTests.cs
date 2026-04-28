using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Backend;

namespace CodexCliPlus.Tests.Backend;

public sealed class BackendAssetServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-backend-assets-{Guid.NewGuid():N}");

    [Fact]
    public async Task EnsureAssetsAsyncRemovesLegacyManagedUpstreamExecutableAfterManagedExecutableIsValid()
    {
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();

        var managedPath = Path.Combine(
            pathService.Directories.BackendDirectory,
            BackendExecutableNames.ManagedExecutableFileName);
        var legacyPath = Path.Combine(
            pathService.Directories.BackendDirectory,
            BackendExecutableNames.UpstreamExecutableFileName);
        File.Copy(FindRepositoryBackendExecutable(), managedPath);
        await File.WriteAllTextAsync(legacyPath, "legacy managed executable");

        var service = new BackendAssetService(new HttpClient(), pathService, new NullAppLogger(_rootDirectory));

        var layout = await service.EnsureAssetsAsync();

        Assert.Equal(managedPath, layout.ExecutablePath);
        Assert.True(File.Exists(managedPath));
        Assert.False(File.Exists(legacyPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private static string FindRepositoryBackendExecutable()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "resources",
                "backend",
                "windows-x64",
                BackendExecutableNames.ManagedExecutableFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find repository backend executable {BackendExecutableNames.ManagedExecutableFileName}.");
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

    private sealed class NullAppLogger : IAppLogger
    {
        public NullAppLogger(string rootDirectory)
        {
            LogFilePath = Path.Combine(rootDirectory, "logs", "test.log");
        }

        public string LogFilePath { get; }

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void LogError(string message, Exception? exception = null)
        {
        }
    }
}
