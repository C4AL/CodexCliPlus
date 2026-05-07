using CodexCliPlus.Core.Abstractions.Configuration;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Processes;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.Security;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Infrastructure.Configuration;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Tests.Backend;

[Trait("Category", "Fast")]
public sealed class BackendProcessManagerTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-backend-manager-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task StopAsyncFastExitIsIdempotentWhenAlreadyStopped()
    {
        var manager = CreateManager();
        var runtimeDirectory = Path.Combine(_rootDirectory, "runtime");
        var sentinelPath = Path.Combine(runtimeDirectory, "sentinel.txt");
        Directory.CreateDirectory(runtimeDirectory);
        await File.WriteAllTextAsync(sentinelPath, "keep");

        var stopped = await manager.StopAsync(BackendProcessStopOptions.FastExit);

        Assert.Equal(BackendStateKind.Stopped, stopped.State);
        Assert.True(File.Exists(sentinelPath));
    }

    [Fact]
    public async Task StopAsyncFastExitUsesFastProcessStopAndDoesNotCleanTwice()
    {
        var manager = CreateManager();
        var managedProcess = new TestManagedProcess();
        SetManagedProcess(manager, managedProcess);

        var runtimeDirectory = Path.Combine(_rootDirectory, "runtime");
        var stalePath = Path.Combine(runtimeDirectory, "stale.txt");
        Directory.CreateDirectory(runtimeDirectory);
        await File.WriteAllTextAsync(stalePath, "stale");

        var stopped = await manager.StopAsync(BackendProcessStopOptions.FastExit);

        Assert.Equal(BackendStateKind.Stopped, stopped.State);
        Assert.Equal(1, managedProcess.StopCallCount);
        Assert.Equal(ManagedProcessStopOptions.FastExit, managedProcess.LastStopOptions);
        Assert.True(managedProcess.Disposed);
        Assert.False(File.Exists(stalePath));

        var sentinelPath = Path.Combine(runtimeDirectory, "sentinel.txt");
        await File.WriteAllTextAsync(sentinelPath, "keep");

        var stoppedAgain = await manager.StopAsync(BackendProcessStopOptions.FastExit);

        Assert.Equal(BackendStateKind.Stopped, stoppedAgain.State);
        Assert.Equal(1, managedProcess.StopCallCount);
        Assert.True(File.Exists(sentinelPath));
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

    private static void SetManagedProcess(
        BackendProcessManager manager,
        IManagedProcess managedProcess
    )
    {
        var field = typeof(BackendProcessManager).GetField(
            "_managedProcess",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        Assert.NotNull(field);
        field.SetValue(manager, managedProcess);
    }

    private sealed class TestManagedProcess : IManagedProcess
    {
        public int ProcessId => 12345;

        public bool HasExited { get; private set; }

        public int? ExitCode => HasExited ? 0 : null;

        public int StopCallCount { get; private set; }

        public ManagedProcessStopOptions? LastStopOptions { get; private set; }

        public bool Disposed { get; private set; }

        public event EventHandler? Exited;

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return StopAsync(ManagedProcessStopOptions.Default, cancellationToken);
        }

        public Task StopAsync(
            ManagedProcessStopOptions stopOptions,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCallCount++;
            LastStopOptions = stopOptions;
            HasExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Disposed = true;
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
            Directory.CreateDirectory(Directories.DiagnosticsDirectory);
            Directory.CreateDirectory(Directories.RuntimeDirectory);
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

        public Task RevokeAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteSecretAsync(
            string secretId,
            CancellationToken cancellationToken = default
        )
        {
            return Task.CompletedTask;
        }
    }
}
