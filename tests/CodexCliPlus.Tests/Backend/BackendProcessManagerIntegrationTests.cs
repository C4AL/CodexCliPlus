using System.Diagnostics;
using System.Net;

using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Processes;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Infrastructure.Configuration;
using CodexCliPlus.Infrastructure.Logging;
using CodexCliPlus.Infrastructure.Processes;

using Microsoft.Extensions.DependencyInjection;

namespace CodexCliPlus.Tests.Backend;

[Collection("BackendProcessManager")]
public sealed class BackendProcessManagerIntegrationTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"codexcliplus-backend-run-{Guid.NewGuid():N}");

    [Fact]
    public async Task StartAsyncStartsOfficialBackendAndStopAsyncStopsIt()
    {
        var manager = CreateManager();

        var running = await manager.StartAsync();

        Assert.Equal(Core.Enums.BackendStateKind.Running, running.State);
        Assert.NotNull(running.Runtime);
        Assert.NotNull(running.ProcessId);

        using var client = new HttpClient();
        using var response = await client.GetAsync(running.Runtime.HealthUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var runtimeDirectory = Path.Combine(_rootDirectory, "runtime");
        Directory.CreateDirectory(Path.Combine(runtimeDirectory, "nested"));
        await File.WriteAllTextAsync(Path.Combine(runtimeDirectory, "stale.tmp"), "runtime");
        await File.WriteAllTextAsync(Path.Combine(runtimeDirectory, "nested", "stale.txt"), "runtime");

        var stopped = await manager.StopAsync();
        Assert.Equal(Core.Enums.BackendStateKind.Stopped, stopped.State);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(running.ProcessId.Value));
        Assert.Empty(Directory.EnumerateFileSystemEntries(runtimeDirectory));
    }

    [Fact]
    public async Task UnexpectedBackendExitTransitionsToErrorState()
    {
        var manager = CreateManager();

        var running = await manager.StartAsync();
        Assert.Equal(Core.Enums.BackendStateKind.Running, running.State);
        Assert.NotNull(running.ProcessId);

        using var process = Process.GetProcessById(running.ProcessId.Value);
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (manager.CurrentStatus.State != Core.Enums.BackendStateKind.Error && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(200);
        }

        Assert.Equal(Core.Enums.BackendStateKind.Error, manager.CurrentStatus.State);
        Assert.Contains("exited unexpectedly", manager.CurrentStatus.LastError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        await manager.StopAsync();
    }

    [Fact]
    public async Task StartFailureWritesErrorToDesktopLog()
    {
        var pathService = new TestPathService(_rootDirectory);
        var configurationService = new JsonAppConfigurationService(pathService);
        var logger = new FileAppLogger(pathService);
        var assetService = new BackendAssetService(new HttpClient(), pathService, logger);
        await assetService.EnsureAssetsAsync();

        var services = new ServiceCollection()
            .AddHttpClient()
            .BuildServiceProvider();

        var manager = new BackendProcessManager(
            assetService,
            new BackendConfigWriter(configurationService, pathService),
            new BackendHealthChecker(services.GetRequiredService<IHttpClientFactory>()),
            configurationService,
            pathService,
            new ThrowingProcessService(new FileNotFoundException("The system cannot find the file specified.")),
            logger);

        var status = await manager.StartAsync();
        var logText = await File.ReadAllTextAsync(logger.LogFilePath);

        Assert.Equal(Core.Enums.BackendStateKind.Error, status.State);
        Assert.Contains("Failed to start backend process.", logText, StringComparison.Ordinal);
        Assert.Contains("The system cannot find the file specified.", logText, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var process in Process.GetProcessesByName(
                     Path.GetFileNameWithoutExtension(BackendExecutableNames.ManagedExecutableFileName)))
        {
            try
            {
                if (string.Equals(
                        process.MainModule?.FileName,
                        Path.Combine(
                            _rootDirectory,
                            "backend",
                            BackendExecutableNames.ManagedExecutableFileName),
                        StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch
            {
                // Ignore cleanup races in teardown.
            }
        }

        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private BackendProcessManager CreateManager()
    {
        var pathService = new TestPathService(_rootDirectory);
        var configurationService = new JsonAppConfigurationService(pathService);
        var logger = new FileAppLogger(pathService);
        var services = new ServiceCollection()
            .AddHttpClient()
            .BuildServiceProvider();
        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();

        return new BackendProcessManager(
            new BackendAssetService(new HttpClient(), pathService, logger),
            new BackendConfigWriter(configurationService, pathService),
            new BackendHealthChecker(httpClientFactory),
            configurationService,
            pathService,
            new SystemProcessService(),
            logger);
    }

    private sealed class ThrowingProcessService : IProcessService
    {
        private readonly Exception _exception;

        public ThrowingProcessService(Exception exception)
        {
            _exception = exception;
        }

        public Task<IManagedProcess> StartAsync(
            ManagedProcessStartInfo startInfo,
            Action<string>? standardOutput = null,
            Action<string>? standardError = null,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
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
                Path.Combine(rootDirectory, "config", "backend.yaml"));
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
