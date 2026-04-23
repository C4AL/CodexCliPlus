using System.Diagnostics;
using System.Net;

using DesktopHost.Core.Abstractions.Processes;
using DesktopHost.Core.Abstractions.Paths;
using DesktopHost.Core.Models;
using DesktopHost.Infrastructure.Backend;
using DesktopHost.Infrastructure.Configuration;
using DesktopHost.Infrastructure.Logging;
using DesktopHost.Infrastructure.Processes;

using Microsoft.Extensions.DependencyInjection;

namespace DesktopHost.Tests.Backend;

[Collection("BackendProcessManager")]
public sealed class BackendProcessManagerIntegrationTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"cpad-backend-run-{Guid.NewGuid():N}");

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

        var stopped = await manager.StopAsync();
        Assert.Equal(Core.Enums.BackendStateKind.Stopped, stopped.State);
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
        Assert.Contains("提前结束", manager.CurrentStatus.LastError ?? string.Empty, StringComparison.Ordinal);
        await manager.StopAsync();
    }

    [Fact]
    public async Task StartFailureWritesChineseErrorToDesktopLog()
    {
        var pathService = new TestPathService(_rootDirectory);
        var configurationService = new JsonDesktopConfigurationService(pathService);
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
            new ThrowingProcessService(new FileNotFoundException("系统找不到指定的文件。")),
            logger);

        var status = await manager.StartAsync();
        var logText = await File.ReadAllTextAsync(logger.LogFilePath);

        Assert.Equal(Core.Enums.BackendStateKind.Error, status.State);
        Assert.Contains("后端启动失败", logText, StringComparison.Ordinal);
        Assert.Contains("系统找不到指定的文件", logText, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var process in Process.GetProcessesByName("cli-proxy-api"))
        {
            try
            {
                if (string.Equals(
                        process.MainModule?.FileName,
                        Path.Combine(_rootDirectory, "backend", "cli-proxy-api.exe"),
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
        var configurationService = new JsonDesktopConfigurationService(pathService);
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
