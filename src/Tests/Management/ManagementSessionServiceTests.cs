using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Processes;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Core.Models.Security;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Infrastructure.Configuration;
using CodexCliPlus.Infrastructure.Management;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Tests.Management;

[Trait("Category", "Fast")]
public sealed class ManagementSessionServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-session-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task GetConnectionAsyncUsesRunningRuntimeWithoutRestart()
    {
        using var manager = CreateManager();
        var provider = new FakeConnectionProvider(CreateConnection("fresh"));
        SetCurrentStatus(
            manager,
            new BackendStatusSnapshot
            {
                State = BackendStateKind.Running,
                Runtime = CreateRuntime("running"),
            }
        );
        var service = new ManagementSessionService(manager, provider);

        var connection = await service.GetConnectionAsync();

        Assert.Equal("running", connection.ManagementKey);
        Assert.Equal(0, provider.CallCount);
    }

    [Theory]
    [InlineData(BackendStateKind.Stopped)]
    [InlineData(BackendStateKind.Error)]
    public async Task GetConnectionAsyncRefreshesStaleRuntimeWhenBackendIsNotRunning(
        BackendStateKind state
    )
    {
        using var manager = CreateManager();
        var provider = new FakeConnectionProvider(CreateConnection("fresh"));
        SetCurrentStatus(
            manager,
            new BackendStatusSnapshot
            {
                State = state,
                Runtime = CreateRuntime("stale"),
            }
        );
        var service = new ManagementSessionService(manager, provider);

        var connection = await service.GetConnectionAsync();

        Assert.Equal("fresh", connection.ManagementKey);
        Assert.Equal(1, provider.CallCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private BackendProcessManager CreateManager()
    {
        var pathService = new TestPathService(_rootDirectory);
        var logger = new NoopLogger(pathService.Directories.LogsDirectory);
        var configurationService = new JsonAppConfigurationService(pathService);

        return new BackendProcessManager(
            new BackendAssetService(new HttpClient(), pathService, logger),
            new BackendConfigWriter(configurationService, pathService),
            new BackendHealthChecker(new StaticHttpClientFactory()),
            configurationService,
            pathService,
            new ThrowingProcessService(),
            logger,
            new SecretBrokerService(new NoopSecretVault(), logger)
        );
    }

    private static BackendRuntimeInfo CreateRuntime(string managementKey)
    {
        var baseUrl = "http://127.0.0.1:1327";
        return new BackendRuntimeInfo
        {
            RequestedPort = AppConstants.DefaultBackendPort,
            Port = AppConstants.DefaultBackendPort,
            PortWasAdjusted = false,
            ManagementKey = managementKey,
            ConfigPath = "backend.yaml",
            BaseUrl = baseUrl,
            HealthUrl = $"{baseUrl}/healthz",
            ManagementApiBaseUrl = $"{baseUrl}/v0/management",
        };
    }

    private static ManagementConnectionInfo CreateConnection(string managementKey)
    {
        var baseUrl = "http://127.0.0.1:1327";
        return new ManagementConnectionInfo
        {
            BaseUrl = baseUrl,
            ManagementApiBaseUrl = $"{baseUrl}/v0/management",
            ManagementKey = managementKey,
        };
    }

    private static void SetCurrentStatus(
        BackendProcessManager manager,
        BackendStatusSnapshot status
    )
    {
        var property = typeof(BackendProcessManager).GetProperty(
            nameof(BackendProcessManager.CurrentStatus)
        );
        var setter = property?.GetSetMethod(nonPublic: true);
        Assert.NotNull(setter);
        setter.Invoke(manager, [status]);
    }

    private sealed class FakeConnectionProvider : IManagementConnectionProvider
    {
        private readonly ManagementConnectionInfo _connection;

        public FakeConnectionProvider(ManagementConnectionInfo connection)
        {
            _connection = connection;
        }

        public int CallCount { get; private set; }

        public Task<ManagementConnectionInfo> GetConnectionAsync(
            CancellationToken cancellationToken = default
        )
        {
            CallCount++;
            return Task.FromResult(_connection);
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

    private sealed class NoopLogger : IAppLogger
    {
        public NoopLogger(string logDirectory)
        {
            LogFilePath = Path.Combine(logDirectory, "desktop.log");
        }

        public string LogFilePath { get; }

        public void Info(string message) { }

        public void Warn(string message) { }

        public void LogError(string message, Exception? exception = null) { }
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class ThrowingProcessService : IProcessService
    {
        public Task<IManagedProcess> StartAsync(
            ManagedProcessStartInfo startInfo,
            Action<string>? standardOutput = null,
            Action<string>? standardError = null,
            CancellationToken cancellationToken = default
        )
        {
            throw new InvalidOperationException("测试不应启动后端进程。");
        }
    }

    private sealed class NoopSecretVault : ISecretVault
    {
        public Task<SecretRecord> SaveSecretAsync(
            SecretKind kind,
            string value,
            string source,
            IReadOnlyDictionary<string, string>? metadata = null,
            string? secretId = null,
            CancellationToken cancellationToken = default
        )
        {
            throw new InvalidOperationException("测试不应保存密钥。");
        }

        public Task<string?> RevealSecretAsync(
            string secretId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>(null);

        public Task<SecretRecord?> GetSecretAsync(
            string secretId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<SecretRecord?>(null);

        public Task<IReadOnlyList<SecretRecord>> ListSecretsAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<SecretRecord>>(Array.Empty<SecretRecord>());

        public Task SetSecretStatusAsync(
            string secretId,
            SecretStatus status,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task RevokeAllAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteSecretAsync(
            string secretId,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }
}
