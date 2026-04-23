using DesktopHost.Core.Abstractions.Configuration;
using DesktopHost.Core.Abstractions.Logging;
using DesktopHost.Core.Abstractions.Processes;
using DesktopHost.Core.Enums;
using DesktopHost.Core.Models;

namespace DesktopHost.Infrastructure.Backend;

public sealed class BackendProcessManager : IDisposable
{
    private readonly BackendAssetService _assetService;
    private readonly BackendConfigWriter _configWriter;
    private readonly BackendHealthChecker _healthChecker;
    private readonly IDesktopConfigurationService _configurationService;
    private readonly IProcessService _processService;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _recentLogLines = [];

    private IManagedProcess? _managedProcess;

    public BackendProcessManager(
        BackendAssetService assetService,
        BackendConfigWriter configWriter,
        BackendHealthChecker healthChecker,
        IDesktopConfigurationService configurationService,
        IProcessService processService,
        IAppLogger logger)
    {
        _assetService = assetService;
        _configWriter = configWriter;
        _healthChecker = healthChecker;
        _configurationService = configurationService;
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
            if (_managedProcess is not null && !_managedProcess.HasExited)
            {
                return CurrentStatus;
            }

            UpdateStatus(new BackendStatusSnapshot
            {
                State = BackendStateKind.Starting,
                Message = "正在准备 CLIProxyAPI 资源..."
            });

            var assetLayout = await _assetService.EnsureAssetsAsync(cancellationToken);
            var settings = await _configurationService.LoadAsync(cancellationToken);
            var runtime = await _configWriter.WriteAsync(settings, assetLayout, cancellationToken);

            _managedProcess = await _processService.StartAsync(
                new ManagedProcessStartInfo(
                    assetLayout.ExecutablePath,
                    $"-config \"{runtime.ConfigPath}\"",
                    assetLayout.WorkingDirectory,
                    new Dictionary<string, string?>
                    {
                        ["MANAGEMENT_STATIC_PATH"] = assetLayout.ManagementHtmlPath
                    }),
                line => AppendLogLine($"[stdout] {line}"),
                line => AppendLogLine($"[stderr] {line}"),
                cancellationToken);

            _managedProcess.Exited += OnProcessExited;
            AppendLogLine($"后端进程已启动，PID={_managedProcess.ProcessId}。");

            var isHealthy = await _healthChecker.WaitUntilHealthyAsync(
                runtime.HealthUrl,
                TimeSpan.FromSeconds(30),
                cancellationToken);

            if (!isHealthy)
            {
                UpdateStatus(new BackendStatusSnapshot
                {
                    State = BackendStateKind.Error,
                    Message = "后端启动后健康检查失败。",
                    LastError = "30 秒内未通过 /healthz 检查。",
                    Runtime = runtime,
                    ProcessId = _managedProcess.ProcessId
                });

                await StopManagedProcessAsync(cancellationToken);
                return CurrentStatus;
            }

            var runningMessage = runtime.PortWasAdjusted
                ? $"后端运行中，端口 {runtime.Port}（原请求端口 {runtime.RequestedPort} 已避让）。"
                : $"后端运行中，端口 {runtime.Port}。";

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
            _logger.LogError("后端启动失败。", exception);
            await StopManagedProcessAsync(cancellationToken);
            UpdateStatus(new BackendStatusSnapshot
            {
                State = BackendStateKind.Error,
                Message = "后端启动失败。",
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
            await StopManagedProcessAsync(cancellationToken);

            UpdateStatus(new BackendStatusSnapshot
            {
                State = BackendStateKind.Stopped,
                Message = "后端已停止。",
                Runtime = CurrentStatus.Runtime
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
        UpdateStatus(new BackendStatusSnapshot
        {
            State = BackendStateKind.Error,
            Message = "后端进程已退出。",
            LastError = "CLIProxyAPI 进程提前结束，请检查日志。",
            Runtime = CurrentStatus.Runtime
        });
    }

    private void AppendLogLine(string line)
    {
        _logger.Info(line);
        lock (_recentLogLines)
        {
            _recentLogLines.Add($"{DateTimeOffset.Now:HH:mm:ss} {line}");
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

        _managedProcess.Exited -= OnProcessExited;
        await _managedProcess.StopAsync(cancellationToken);
        _managedProcess.Dispose();
        _managedProcess = null;
    }
}
