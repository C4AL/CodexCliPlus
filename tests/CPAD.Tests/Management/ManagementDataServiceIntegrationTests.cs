using System.Diagnostics;
using System.Net;

using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Models;
using CPAD.Infrastructure.Backend;
using CPAD.Infrastructure.Configuration;
using CPAD.Infrastructure.Logging;
using CPAD.Infrastructure.Management;
using CPAD.Infrastructure.Processes;

using Microsoft.Extensions.DependencyInjection;

namespace CPAD.Tests.Management;

[Collection("BackendProcessManager")]
public sealed class ManagementDataServiceIntegrationTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"cpad-management-run-{Guid.NewGuid():N}");

    [Fact]
    public async Task ServicesReadLiveManagementDataFromManagedBackend()
    {
        var manager = CreateManager();
        var running = await manager.StartAsync();

        try
        {
            Assert.Equal(Core.Enums.BackendStateKind.Running, running.State);
            Assert.NotNull(running.Runtime);

            var services = new ServiceCollection()
                .AddHttpClient()
                .BuildServiceProvider();
            var connectionProvider = new BackendManagementConnectionProvider(manager);
            var apiClient = new ManagementApiClient(connectionProvider, services.GetRequiredService<IHttpClientFactory>());
            var configService = new ManagementConfigurationService(apiClient);
            var authService = new ManagementAuthService(apiClient);
            var usageService = new ManagementUsageService(apiClient);
            var logsService = new ManagementLogsService(apiClient);
            var systemService = new ManagementSystemService(apiClient);
            var overviewService = new ManagementOverviewService(connectionProvider, configService, authService, usageService, systemService);

            var config = await configService.GetConfigAsync();
            Assert.Contains("sk-dummy", config.Value.ApiKeys);
            Assert.True(config.Value.LoggingToFile);

            var configYaml = await configService.GetConfigYamlAsync();
            Assert.Contains("disable-control-panel: true", configYaml.Value, StringComparison.Ordinal);

            var apiKeys = await authService.GetApiKeysAsync();
            Assert.Contains("sk-dummy", apiKeys.Value);

            var authFiles = await authService.GetAuthFilesAsync();
            Assert.NotNull(authFiles.Value);

            var usage = await usageService.GetUsageAsync();
            Assert.True(usage.Value.TotalRequests >= 0);

            var logs = await logsService.GetLogsAsync(limit: 25);
            Assert.True(logs.Value.LineCount >= 0);

            var errorLogs = await logsService.GetRequestErrorLogsAsync();
            Assert.NotNull(errorLogs.Value);

            var codexModels = await authService.GetModelDefinitionsAsync("codex");
            Assert.NotNull(codexModels.Value);

            var overview = await overviewService.GetOverviewAsync();
            Assert.Equal(running.Runtime.ManagementApiBaseUrl, overview.Value.ManagementApiBaseUrl);
            Assert.True(overview.Value.ApiKeyCount >= 1);
            Assert.False(string.IsNullOrWhiteSpace(overview.Value.ServerVersion));
            Assert.True(
                !string.IsNullOrWhiteSpace(overview.Value.LatestVersion) ||
                !string.IsNullOrWhiteSpace(overview.Value.LatestVersionError));
            Assert.True(overview.Value.Usage.TotalRequests >= 0);
        }
        finally
        {
            await manager.StopAsync();
        }
    }

    [Fact]
    public async Task AuthServiceMutatesLiveBackendStateThroughAuditedEndpoints()
    {
        var manager = CreateManager();
        var running = await manager.StartAsync();

        try
        {
            Assert.Equal(Core.Enums.BackendStateKind.Running, running.State);

            var services = new ServiceCollection()
                .AddHttpClient()
                .BuildServiceProvider();
            var connectionProvider = new BackendManagementConnectionProvider(manager);
            var apiClient = new ManagementApiClient(connectionProvider, services.GetRequiredService<IHttpClientFactory>());
            var authService = new ManagementAuthService(apiClient);

            await authService.ReplaceApiKeysAsync(["sk-phase82-a", "sk-phase82-b", "sk-phase82-a"]);
            var apiKeys = await authService.GetApiKeysAsync();
            Assert.Equal(["sk-phase82-a", "sk-phase82-b"], apiKeys.Value);

            const string authFileName = "phase82-cookie.json";
            const string authJson = """{"type":"codex","email":"phase82@example.com","metadata":{"cookie":"session=phase82"}}""";

            var upload = await authService.UploadAuthFileAsync(authFileName, authJson);
            Assert.Equal("ok", upload.Value.Status);

            var authFiles = await authService.GetAuthFilesAsync();
            var uploadedAuth = Assert.Single(authFiles.Value, item => item.Name == authFileName);
            Assert.Equal("phase82@example.com", uploadedAuth.Email);

            var disable = await authService.SetAuthFileDisabledAsync(authFileName, disabled: true);
            Assert.Equal("ok", disable.Value.Status);

            authFiles = await authService.GetAuthFilesAsync();
            Assert.True(authFiles.Value.Single(item => item.Name == authFileName).Disabled);

            var delete = await authService.DeleteAuthFileAsync(authFileName);
            Assert.Equal("ok", delete.Value.Status);

            authFiles = await authService.GetAuthFilesAsync();
            Assert.DoesNotContain(authFiles.Value, item => item.Name == authFileName);
        }
        finally
        {
            await manager.StopAsync();
        }
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
        var configurationService = new JsonAppConfigurationService(pathService);
        var logger = new FileAppLogger(pathService);
        var services = new ServiceCollection()
            .AddHttpClient()
            .BuildServiceProvider();

        return new BackendProcessManager(
            new BackendAssetService(new HttpClient(), pathService, logger),
            new BackendConfigWriter(configurationService, pathService),
            new BackendHealthChecker(services.GetRequiredService<IHttpClientFactory>()),
            configurationService,
            new SystemProcessService(),
            logger);
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
