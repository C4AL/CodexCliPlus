using System.Reflection;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Backend;

namespace CodexCliPlus.Tests.Backend;

[Trait("Category", "LocalIntegration")]
public sealed class BackendAssetServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-backend-assets-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task EnsureAssetsAsyncRemovesLegacyManagedUpstreamExecutableAfterManagedExecutableIsValid()
    {
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();

        var managedPath = Path.Combine(
            pathService.Directories.BackendDirectory,
            BackendExecutableNames.ManagedExecutableFileName
        );
        var legacyPath = Path.Combine(
            pathService.Directories.BackendDirectory,
            BackendExecutableNames.UpstreamExecutableFileName
        );
        var service = new BackendAssetService(
            new HttpClient(),
            pathService,
            new NullAppLogger(_rootDirectory)
        );

        await File.WriteAllTextAsync(managedPath, "managed executable placeholder");
        await InvokeWriteVersionCacheAsync(service, managedPath, BackendReleaseMetadata.Version);

        await File.WriteAllTextAsync(legacyPath, "legacy managed executable");

        var layout = await service.EnsureAssetsAsync();

        Assert.Equal(managedPath, layout.ExecutablePath);
        Assert.True(File.Exists(managedPath));
        Assert.False(File.Exists(legacyPath));
    }

    [Fact]
    public async Task EnsureAssetsAsyncInitializesPathsOnlyOnceWhenRepairIsNeeded()
    {
        var pathService = new TestPathService(_rootDirectory);
        var service = new BackendAssetService(
            new HttpClient(),
            pathService,
            new NullAppLogger(_rootDirectory)
        );

        var exception = await Record.ExceptionAsync(() => service.EnsureAssetsAsync());

        Assert.True(exception is null or InvalidOperationException, exception?.ToString());
        Assert.Equal(1, pathService.EnsureCreatedCalls);
    }

    [Fact]
    public async Task IsExecutableVersionCurrentAsyncUsesMatchingVersionCache()
    {
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        var executablePath = Path.Combine(
            pathService.Directories.BackendDirectory,
            BackendExecutableNames.ManagedExecutableFileName
        );
        await File.WriteAllTextAsync(executablePath, "not a runnable executable");

        var service = new BackendAssetService(
            new HttpClient(),
            pathService,
            new NullAppLogger(_rootDirectory)
        );

        await InvokeWriteVersionCacheAsync(service, executablePath, BackendReleaseMetadata.Version);
        var cachedResult = await InvokeIsExecutableVersionCurrentAsync(service, executablePath);

        await File.AppendAllTextAsync(executablePath, "changed");
        var staleCacheResult = await InvokeIsExecutableVersionCurrentAsync(service, executablePath);

        Assert.True(cachedResult);
        Assert.False(staleCacheResult);
    }

    [Fact]
    public async Task WriteExecutableVersionCacheAsyncPropagatesCancellation()
    {
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        var executablePath = Path.Combine(
            pathService.Directories.BackendDirectory,
            BackendExecutableNames.ManagedExecutableFileName
        );
        await File.WriteAllTextAsync(executablePath, "managed executable placeholder");
        var service = new BackendAssetService(
            new HttpClient(),
            pathService,
            new NullAppLogger(_rootDirectory)
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Record.ExceptionAsync(() =>
            InvokeWriteVersionCacheAsync(
                service,
                executablePath,
                BackendReleaseMetadata.Version,
                cancellation.Token
            )
        );

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task WriteExecutableVersionCacheAsyncKeepsExistingCacheWhenReplacementFails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        var executablePath = Path.Combine(
            pathService.Directories.BackendDirectory,
            BackendExecutableNames.ManagedExecutableFileName
        );
        await File.WriteAllTextAsync(executablePath, "managed executable placeholder");
        var logger = new NullAppLogger(_rootDirectory);
        var service = new BackendAssetService(new HttpClient(), pathService, logger);
        var cachePath = Path.Combine(
            pathService.Directories.CacheDirectory,
            "backend-version-cache.json"
        );
        await InvokeWriteVersionCacheAsync(service, executablePath, BackendReleaseMetadata.Version);
        var originalJson = await File.ReadAllTextAsync(cachePath);
        await using var lockedCache = new FileStream(
            cachePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );

        await InvokeWriteVersionCacheAsync(service, executablePath, "0.0.0");

        Assert.Equal(originalJson, await File.ReadAllTextAsync(cachePath));
        Assert.Empty(
            Directory.EnumerateFiles(
                pathService.Directories.CacheDirectory,
                "backend-version-cache.json.*.tmp"
            )
        );
        Assert.Contains(
            logger.Warnings,
            warning =>
                warning.Contains(
                    "Failed to write CLIProxyAPI version cache",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public async Task IsExecutableVersionCurrentAsyncPropagatesCallerCancellation()
    {
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        var service = new BackendAssetService(
            new HttpClient(),
            pathService,
            new NullAppLogger(_rootDirectory)
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Record.ExceptionAsync(() =>
            InvokeIsExecutableVersionCurrentAsync(service, Environment.ProcessPath!, cancellation.Token)
        );

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task IsExecutableVersionCurrentAsyncPropagatesCallerCancellationBeforeCacheHit()
    {
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        var executablePath = Path.Combine(
            pathService.Directories.BackendDirectory,
            BackendExecutableNames.ManagedExecutableFileName
        );
        await File.WriteAllTextAsync(executablePath, "managed executable placeholder");
        var service = new BackendAssetService(
            new HttpClient(),
            pathService,
            new NullAppLogger(_rootDirectory)
        );
        await InvokeWriteVersionCacheAsync(service, executablePath, BackendReleaseMetadata.Version);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Record.ExceptionAsync(() =>
            InvokeIsExecutableVersionCurrentAsync(service, executablePath, cancellation.Token)
        );

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
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
                Path.Combine(rootDirectory, "config", "appsettings.json"),
                Path.Combine(rootDirectory, "config", "backend.yaml")
            );
        }

        public AppDirectories Directories { get; }

        public int EnsureCreatedCalls { get; private set; }

        public Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
        {
            EnsureCreatedCalls++;
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

    private static async Task InvokeWriteVersionCacheAsync(
        BackendAssetService service,
        string executablePath,
        string version,
        CancellationToken cancellationToken = default
    )
    {
        var method = typeof(BackendAssetService).GetMethod(
            "WriteExecutableVersionCacheAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(method);
        var task = (Task)
            method!.Invoke(service, [executablePath, version, cancellationToken])!;
        await task;
    }

    private static async Task<bool> InvokeIsExecutableVersionCurrentAsync(
        BackendAssetService service,
        string executablePath,
        CancellationToken cancellationToken = default
    )
    {
        var method = typeof(BackendAssetService).GetMethod(
            "IsExecutableVersionCurrentAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(method);
        return await (Task<bool>)method!.Invoke(service, [executablePath, cancellationToken])!;
    }

    private sealed class NullAppLogger : IAppLogger
    {
        private readonly List<string> _warnings = [];

        public NullAppLogger(string rootDirectory)
        {
            LogFilePath = Path.Combine(rootDirectory, "logs", "test.log");
        }

        public string LogFilePath { get; }

        public IReadOnlyList<string> Warnings => _warnings;

        public void Info(string message) { }

        public void Warn(string message)
        {
            _warnings.Add(message);
        }

        public void LogError(string message, Exception? exception = null) { }
    }
}
