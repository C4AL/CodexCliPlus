using CodexCliPlus.Core.Abstractions.Configuration;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Processes;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Infrastructure.Backend;

public sealed class BackendProcessManager : IDisposable
{
    private readonly BackendAssetService _assetService;
    private readonly BackendConfigWriter _configWriter;
    private readonly BackendHealthChecker _healthChecker;
    private readonly IAppConfigurationService _configurationService;
    private readonly IPathService _pathService;
    private readonly IProcessService _processService;
    private readonly IAppLogger _logger;
    private readonly SecretBrokerService _secretBrokerService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _recentLogLines = [];

    private IManagedProcess? _managedProcess;
    private CancellationTokenSource _operationCts = new();
    private bool _stopRequested;
    private BackendProcessStopOptions _requestedStopOptions = BackendProcessStopOptions.Default;
    private bool _disposed;

    public BackendProcessManager(
        BackendAssetService assetService,
        BackendConfigWriter configWriter,
        BackendHealthChecker healthChecker,
        IAppConfigurationService configurationService,
        IPathService pathService,
        IProcessService processService,
        IAppLogger logger,
        SecretBrokerService secretBrokerService
    )
    {
        _assetService = assetService;
        _configWriter = configWriter;
        _healthChecker = healthChecker;
        _configurationService = configurationService;
        _pathService = pathService;
        _processService = processService;
        _logger = logger;
        _secretBrokerService = secretBrokerService;
    }

    public event EventHandler<BackendStatusSnapshot>? StatusChanged;

    public BackendStatusSnapshot CurrentStatus { get; private set; } = new();

    public string RecentLogText
    {
        get
        {
            lock (_recentLogLines)
            {
                return string.Join(Environment.NewLine, _recentLogLines);
            }
        }
    }

    public async Task<BackendStatusSnapshot> StartAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _operationCts.Token
            );
            var operationToken = linkedCts.Token;

            if (_managedProcess is not null && _managedProcess.HasExited)
            {
                _managedProcess.Dispose();
                _managedProcess = null;
            }

            if (_managedProcess is not null && !_managedProcess.HasExited)
            {
                return CurrentStatus;
            }

            UpdateStatus(
                new BackendStatusSnapshot
                {
                    State = BackendStateKind.Starting,
                    Message =
                        $"Starting {BackendExecutableNames.ManagedExecutableFileName} backend...",
                }
            );

            var assetLayout = await _assetService.EnsureAssetsAsync(operationToken)
                .ConfigureAwait(false);
            CleanupRuntimeArtifacts();
            var settings = await _configurationService.LoadAsync(operationToken)
                .ConfigureAwait(false);
            var runtime = await _configWriter.WriteAsync(settings, operationToken)
                .ConfigureAwait(false);
            var secretBrokerSession = await _secretBrokerService.StartAsync(operationToken)
                .ConfigureAwait(false);

            _stopRequested = false;
            _requestedStopOptions = BackendProcessStopOptions.Default;
            _managedProcess = await _processService
                .StartAsync(
                    new ManagedProcessStartInfo(
                        assetLayout.ExecutablePath,
                        $"-config \"{runtime.ConfigPath}\"",
                        assetLayout.WorkingDirectory,
                        new Dictionary<string, string?>
                        {
                            [SecretBrokerService.BrokerUrlEnvironmentVariable] =
                                secretBrokerSession.BaseUrl,
                            [SecretBrokerService.BrokerTokenEnvironmentVariable] =
                                secretBrokerSession.Token,
                        }
                    ),
                    line => AppendLogLine($"[stdout] {line}"),
                    line => AppendLogLine($"[stderr] {line}"),
                    operationToken
                )
                .ConfigureAwait(false);

            _managedProcess.Exited += OnProcessExited;
            AppendLogLine(
                $"Started {BackendExecutableNames.ManagedExecutableFileName} backend process (PID {_managedProcess.ProcessId})."
            );

            var isHealthy = await _healthChecker
                .WaitUntilHealthyAsync(runtime.HealthUrl, TimeSpan.FromSeconds(30), operationToken)
                .ConfigureAwait(false);

            if (!isHealthy)
            {
                _logger.Warn("Backend health check did not pass within 30 seconds.");
                UpdateStatus(
                    new BackendStatusSnapshot
                    {
                        State = BackendStateKind.Error,
                        Message = "Backend failed to become ready.",
                        LastError = "Backend health check did not pass within 30 seconds.",
                        Runtime = runtime,
                        ProcessId = _managedProcess.ProcessId,
                    }
                );

                await StopManagedProcessAsync(BackendProcessStopOptions.Default, CancellationToken.None)
                    .ConfigureAwait(false);
                await _secretBrokerService.StopAsync(CancellationToken.None).ConfigureAwait(false);
                return CurrentStatus;
            }

            var runningMessage = runtime.PortWasAdjusted
                ? $"Backend is running on port {runtime.Port} (requested {runtime.RequestedPort})."
                : $"Backend is running on port {runtime.Port}.";

            UpdateStatus(
                new BackendStatusSnapshot
                {
                    State = BackendStateKind.Running,
                    Message = runningMessage,
                    Runtime = runtime,
                    ProcessId = _managedProcess.ProcessId,
                }
            );

            return CurrentStatus;
        }
        catch (OperationCanceledException)
            when (_stopRequested || _operationCts.IsCancellationRequested)
        {
            await StopManagedProcessAsync(_requestedStopOptions, CancellationToken.None)
                .ConfigureAwait(false);
            await _secretBrokerService.StopAsync(CancellationToken.None).ConfigureAwait(false);
            CleanupRuntimeArtifacts();
            UpdateStatus(
                new BackendStatusSnapshot
                {
                    State = BackendStateKind.Stopped,
                    Message = "Backend is stopped.",
                    Runtime = CurrentStatus.Runtime,
                }
            );

            return CurrentStatus;
        }
        catch (Exception exception)
        {
            _logger.LogError("Failed to start backend process.", exception);
            await StopManagedProcessAsync(BackendProcessStopOptions.Default, CancellationToken.None)
                .ConfigureAwait(false);
            await _secretBrokerService.StopAsync(CancellationToken.None).ConfigureAwait(false);
            CleanupRuntimeArtifacts();
            UpdateStatus(
                new BackendStatusSnapshot
                {
                    State = BackendStateKind.Error,
                    Message = "Failed to start backend process.",
                    LastError = exception.Message,
                }
            );

            return CurrentStatus;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BackendStatusSnapshot> StopAsync(
        CancellationToken cancellationToken = default
    )
    {
        return await StopAsync(BackendProcessStopOptions.Default, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<BackendStatusSnapshot> StopAsync(
        BackendProcessStopOptions stopOptions,
        CancellationToken cancellationToken = default
    )
    {
        _stopRequested = true;
        _requestedStopOptions = stopOptions;
        CancelActiveOperation();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsStopComplete())
            {
                return CurrentStatus;
            }

            var runtime = CurrentStatus.Runtime;
            await StopManagedProcessAsync(stopOptions, cancellationToken).ConfigureAwait(false);
            await _secretBrokerService.StopAsync(cancellationToken).ConfigureAwait(false);
            CleanupRuntimeArtifacts();

            UpdateStatus(
                new BackendStatusSnapshot
                {
                    State = BackendStateKind.Stopped,
                    Message = "Backend is stopped.",
                    Runtime = runtime,
                }
            );

            return CurrentStatus;
        }
        finally
        {
            ResetActiveOperation();
            _requestedStopOptions = BackendProcessStopOptions.Default;
            _gate.Release();
        }
    }

    public async Task<BackendStatusSnapshot> RestartAsync(
        CancellationToken cancellationToken = default
    )
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        return await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Task.Run(() => StopAsync(BackendProcessStopOptions.FastExit)).GetAwaiter().GetResult();
        }
        catch { }

        _operationCts.Dispose();
        _gate.Dispose();
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_stopRequested)
        {
            return;
        }

        AppendLogLine(
            $"{BackendExecutableNames.ManagedExecutableFileName} backend process exited unexpectedly."
        );
        UpdateStatus(
            new BackendStatusSnapshot
            {
                State = BackendStateKind.Error,
                Message = "Backend process exited unexpectedly.",
                LastError =
                    $"{BackendExecutableNames.ManagedExecutableFileName} process exited unexpectedly. Check the desktop log for details.",
                Runtime = CurrentStatus.Runtime,
                ProcessId = CurrentStatus.ProcessId,
            }
        );
    }

    private void AppendLogLine(string line)
    {
        var safeLine = SensitiveDataRedactor.Redact(line);
        _logger.Info(safeLine);
        lock (_recentLogLines)
        {
            _recentLogLines.Add($"{DateTimeOffset.Now:HH:mm:ss} {safeLine}");
            if (_recentLogLines.Count > 200)
            {
                _recentLogLines.RemoveRange(0, _recentLogLines.Count - 200);
            }
        }
    }

    private void UpdateStatus(BackendStatusSnapshot snapshot)
    {
        CurrentStatus = new BackendStatusSnapshot
        {
            State = snapshot.State,
            Message = snapshot.Message,
            LastError = snapshot.LastError,
            Runtime = snapshot.Runtime,
            ProcessId = snapshot.ProcessId,
            UpdatedAt = DateTimeOffset.Now,
        };

        StatusChanged?.Invoke(this, CurrentStatus);
    }

    private async Task StopManagedProcessAsync(
        BackendProcessStopOptions stopOptions,
        CancellationToken cancellationToken
    )
    {
        if (_managedProcess is null)
        {
            return;
        }

        try
        {
            _managedProcess.Exited -= OnProcessExited;
            await _managedProcess
                .StopAsync(ToManagedProcessStopOptions(stopOptions), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _managedProcess.Dispose();
            _managedProcess = null;
        }
    }

    private bool IsStopComplete()
    {
        return _managedProcess is null
            && _secretBrokerService.CurrentSession is null
            && CurrentStatus.State == BackendStateKind.Stopped;
    }

    private static ManagedProcessStopOptions ToManagedProcessStopOptions(
        BackendProcessStopOptions stopOptions
    )
    {
        return stopOptions == BackendProcessStopOptions.FastExit
            ? ManagedProcessStopOptions.FastExit
            : ManagedProcessStopOptions.Default;
    }

    private void CancelActiveOperation()
    {
        try
        {
            _operationCts.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    private void ResetActiveOperation()
    {
        if (!_operationCts.IsCancellationRequested)
        {
            return;
        }

        _operationCts.Dispose();
        _operationCts = new CancellationTokenSource();
    }

    private void CleanupRuntimeArtifacts()
    {
        var runtimeDirectory = _pathService.Directories.RuntimeDirectory;

        try
        {
            Directory.CreateDirectory(runtimeDirectory);

            foreach (var file in Directory.EnumerateFiles(runtimeDirectory))
            {
                File.Delete(file);
            }

            foreach (var directory in Directory.EnumerateDirectories(runtimeDirectory))
            {
                Directory.Delete(directory, recursive: true);
            }

            AppendLogLine($"Cleaned runtime artifacts in {runtimeDirectory}.");
        }
        catch (Exception exception)
        {
            _logger.Warn(
                $"Failed to clean runtime artifacts in {runtimeDirectory}: {exception.Message}"
            );
        }
    }
}
