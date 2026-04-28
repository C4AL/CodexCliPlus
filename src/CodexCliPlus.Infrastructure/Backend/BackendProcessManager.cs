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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _recentLogLines = [];

    private IManagedProcess? _managedProcess;
    private bool _stopRequested;

    public BackendProcessManager(
        BackendAssetService assetService,
        BackendConfigWriter configWriter,
        BackendHealthChecker healthChecker,
        IAppConfigurationService configurationService,
        IPathService pathService,
        IProcessService processService,
        IAppLogger logger)
    {
        _assetService = assetService;
        _configWriter = configWriter;
        _healthChecker = healthChecker;
        _configurationService = configurationService;
        _pathService = pathService;
        _processService = processService;
        _logger = logger;
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

    public async Task<BackendStatusSnapshot> StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_managedProcess is not null && _managedProcess.HasExited)
            {
                _managedProcess.Dispose();
                _managedProcess = null;
            }

            if (_managedProcess is not null && !_managedProcess.HasExited)
            {
                return CurrentStatus;
            }

            UpdateStatus(new BackendStatusSnapshot
            {
                State = BackendStateKind.Starting,
                Message = $"Starting {BackendExecutableNames.ManagedExecutableFileName} backend..."
            });

            var assetLayout = await _assetService.EnsureAssetsAsync(cancellationToken);
            CleanupRuntimeArtifacts();
            var settings = await _configurationService.LoadAsync(cancellationToken);
            var runtime = await _configWriter.WriteAsync(settings, cancellationToken);

            _stopRequested = false;
            _managedProcess = await _processService.StartAsync(
                new ManagedProcessStartInfo(
                    assetLayout.ExecutablePath,
                    $"-config \"{runtime.ConfigPath}\"",
                    assetLayout.WorkingDirectory),
                line => AppendLogLine($"[stdout] {line}"),
                line => AppendLogLine($"[stderr] {line}"),
                cancellationToken);

            _managedProcess.Exited += OnProcessExited;
            AppendLogLine(
                $"Started {BackendExecutableNames.ManagedExecutableFileName} backend process (PID {_managedProcess.ProcessId}).");

            var isHealthy = await _healthChecker.WaitUntilHealthyAsync(
                runtime.HealthUrl,
                TimeSpan.FromSeconds(30),
                cancellationToken);

            if (!isHealthy)
            {
                _logger.Warn("Backend health check did not pass within 30 seconds.");
                UpdateStatus(new BackendStatusSnapshot
                {
                    State = BackendStateKind.Error,
                    Message = "Backend failed to become ready.",
                    LastError = "Backend health check did not pass within 30 seconds.",
                    Runtime = runtime,
                    ProcessId = _managedProcess.ProcessId
                });

                await StopManagedProcessAsync(cancellationToken);
                return CurrentStatus;
            }

            var runningMessage = runtime.PortWasAdjusted
                ? $"Backend is running on port {runtime.Port} (requested {runtime.RequestedPort})."
                : $"Backend is running on port {runtime.Port}.";

            UpdateStatus(new BackendStatusSnapshot
            {
                State = BackendStateKind.Running,
                Message = runningMessage,
                Runtime = runtime,
                ProcessId = _managedProcess.ProcessId
            });

            return CurrentStatus;
        }
        catch (Exception exception)
        {
            _logger.LogError("Failed to start backend process.", exception);
            await StopManagedProcessAsync(cancellationToken);
            CleanupRuntimeArtifacts();
            UpdateStatus(new BackendStatusSnapshot
            {
                State = BackendStateKind.Error,
                Message = "Failed to start backend process.",
                LastError = exception.Message
            });

            return CurrentStatus;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BackendStatusSnapshot> StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var runtime = CurrentStatus.Runtime;
            _stopRequested = true;
            await StopManagedProcessAsync(cancellationToken);
            CleanupRuntimeArtifacts();

            UpdateStatus(new BackendStatusSnapshot
            {
                State = BackendStateKind.Stopped,
                Message = "Backend is stopped.",
                Runtime = runtime
            });

            return CurrentStatus;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BackendStatusSnapshot> RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        return await StartAsync(cancellationToken);
    }

    public void Dispose()
    {
        _gate.Dispose();
        _managedProcess?.Dispose();
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_stopRequested)
        {
            return;
        }

        AppendLogLine($"{BackendExecutableNames.ManagedExecutableFileName} backend process exited unexpectedly.");
        UpdateStatus(new BackendStatusSnapshot
        {
            State = BackendStateKind.Error,
            Message = "Backend process exited unexpectedly.",
            LastError =
                $"{BackendExecutableNames.ManagedExecutableFileName} process exited unexpectedly. Check the desktop log for details.",
            Runtime = CurrentStatus.Runtime,
            ProcessId = CurrentStatus.ProcessId
        });
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
            UpdatedAt = DateTimeOffset.Now
        };

        StatusChanged?.Invoke(this, CurrentStatus);
    }

    private async Task StopManagedProcessAsync(CancellationToken cancellationToken)
    {
        if (_managedProcess is null)
        {
            return;
        }

        try
        {
            _managedProcess.Exited -= OnProcessExited;
            await _managedProcess.StopAsync(cancellationToken);
        }
        finally
        {
            _managedProcess.Dispose();
            _managedProcess = null;
        }
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
            _logger.Warn($"Failed to clean runtime artifacts in {runtimeDirectory}: {exception.Message}");
        }
    }
}
