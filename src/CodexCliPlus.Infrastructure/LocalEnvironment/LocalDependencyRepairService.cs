using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexCliPlus.Core.Abstractions.LocalEnvironment;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models.LocalEnvironment;
using CodexCliPlus.Infrastructure.Utilities;

namespace CodexCliPlus.Infrastructure.LocalEnvironment;

public sealed class LocalDependencyRepairService
{
    private const int ElevationCancelledErrorCode = 1223;
    private static readonly TimeSpan DefaultElevationAuthorizationTimeout = TimeSpan.FromSeconds(
        120
    );
    private static readonly TimeSpan ElevationAuthorizationProgressInterval =
        TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RepairProcessPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RepairProcessTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RepairProcessTerminationWaitTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly IPathService _pathService;
    private readonly IAppLogger _logger;
    private readonly ILocalEnvironmentProcessRunner _processRunner;
    private readonly Func<string?> _currentProcessPathResolver;
    private readonly Func<ProcessStartInfo, Process?> _processStarter;
    private readonly Func<string, bool> _directoryExists;
    private readonly Action<string> _createDirectory;
    private readonly Func<EnvironmentVariableTarget, string?> _userPathReader;
    private readonly Action<string> _userPathWriter;
    private readonly Action _environmentChangeBroadcaster;
    private readonly Action _processPathRefresher;
    private readonly Func<bool> _currentProcessAdministratorChecker;
    private readonly TimeSpan _elevationAuthorizationTimeout;
    private readonly TimeSpan _repairProcessFirstProgressTimeout;
    private readonly LocalEnvironmentOfflinePackageService _offlinePackageService;
    private readonly LocalDependencyRepairCommandExecutor _commandExecutor;

    public LocalDependencyRepairService(
        IPathService pathService,
        IAppLogger logger,
        ILocalEnvironmentProcessRunner processRunner,
        Func<string?>? currentProcessPathResolver = null,
        Func<ProcessStartInfo, Process?>? processStarter = null,
        Func<string, bool>? directoryExists = null,
        Action<string>? createDirectory = null,
        Func<EnvironmentVariableTarget, string?>? userPathReader = null,
        Action<string>? userPathWriter = null,
        Action? environmentChangeBroadcaster = null,
        Action? processPathRefresher = null,
        TimeSpan? elevationAuthorizationTimeout = null,
        Func<bool>? currentProcessAdministratorChecker = null,
        TimeSpan? repairProcessFirstProgressTimeout = null,
        TimeSpan? repairProgressHeartbeatInterval = null,
        Func<Uri, CancellationToken, Task<string>>? downloadStringAsync = null,
        Func<Uri, string, CancellationToken, Task>? downloadFileAsync = null,
        Func<Architecture>? osArchitectureProvider = null,
        LocalEnvironmentOfflinePackageService? offlinePackageService = null
    )
    {
        _pathService = pathService;
        _logger = logger;
        _processRunner = processRunner;
        _currentProcessPathResolver =
            currentProcessPathResolver
            ?? (() => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName);
        _processStarter = processStarter ?? Process.Start;
        _directoryExists = directoryExists ?? Directory.Exists;
        _createDirectory = createDirectory ?? (path => Directory.CreateDirectory(path));
        _userPathReader =
            userPathReader ?? (target => Environment.GetEnvironmentVariable("Path", target));
        _userPathWriter =
            userPathWriter
            ?? (
                value =>
                    Environment.SetEnvironmentVariable(
                        "Path",
                        value,
                        EnvironmentVariableTarget.User
                    )
            );
        _environmentChangeBroadcaster = environmentChangeBroadcaster ?? BroadcastEnvironmentChange;
        _processPathRefresher = processPathRefresher ?? RefreshCurrentProcessPath;
        _currentProcessAdministratorChecker =
            currentProcessAdministratorChecker ?? IsCurrentProcessAdministrator;
        _elevationAuthorizationTimeout =
            elevationAuthorizationTimeout is { } timeout && timeout > TimeSpan.Zero
                ? timeout
                : DefaultElevationAuthorizationTimeout;
        _repairProcessFirstProgressTimeout =
            repairProcessFirstProgressTimeout is { } firstProgressTimeout
            && firstProgressTimeout > TimeSpan.Zero
                ? firstProgressTimeout
                : TimeSpan.FromSeconds(5);
        _offlinePackageService =
            offlinePackageService ?? new LocalEnvironmentOfflinePackageService(_pathService, _logger);
        _commandExecutor = new LocalDependencyRepairCommandExecutor(
            _pathService,
            _logger,
            _processRunner,
            _directoryExists,
            _createDirectory,
            _userPathReader,
            _userPathWriter,
            _environmentChangeBroadcaster,
            _processPathRefresher,
            repairProgressHeartbeatInterval,
            downloadStringAsync,
            downloadFileAsync,
            osArchitectureProvider,
            _offlinePackageService
        );
    }

    public Task<LocalDependencyRepairResult> RunElevatedRepairAsync(
        string actionId,
        CancellationToken cancellationToken = default
    ) => RunElevatedRepairAsync(actionId, progressReporter: null, cancellationToken);

    public async Task<LocalDependencyRepairResult> RunElevatedRepairAsync(
        string actionId,
        Action<LocalDependencyRepairProgress>? progressReporter,
        CancellationToken cancellationToken = default
    )
    {
        if (!LocalDependencyRepairActionIds.IsKnown(actionId))
        {
            return Failure(actionId, "未知修复动作。", "前端只能调用内置白名单动作。");
        }

        if (IsCurrentProcessRunningAsAdministrator())
        {
            return await RunRepairInCurrentProcessAsync(
                actionId,
                progressReporter,
                cancellationToken
            );
        }

        string? statusPath = null;
        LocalDependencyRepairProgress? initialProgress = null;
        try
        {
            statusPath = Path.Combine(
                _pathService.Directories.RuntimeDirectory,
                $"local-environment-repair-{Guid.NewGuid():N}.json"
            );
            await _pathService.EnsureCreatedAsync(cancellationToken);
            initialProgress = CreateProgress(
                actionId,
                "starting",
                "正在准备修复。",
                logPath: GetRepairLogPath()
            );
            await WriteStatusAsync(statusPath, initialProgress, cancellationToken);
            progressReporter?.Invoke(initialProgress);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError($"Failed to prepare local dependency repair '{actionId}'.", exception);
            var failure = Failure(
                actionId,
                "准备修复失败。",
                exception.Message,
                TryGetRepairLogPath()
            );
            if (!string.IsNullOrWhiteSpace(statusPath))
            {
                await TryWriteCompletedStatusAsync(statusPath, failure, cancellationToken);
            }

            TryReportProgress(progressReporter, CreateCompletedProgress(failure, initialProgress));
            return failure;
        }

        var executablePath = _currentProcessPathResolver();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            var failure = Failure(
                actionId,
                "无法定位桌面程序。",
                "当前进程路径不可用，无法进入提权修复模式。",
                GetRepairLogPath()
            );
            await TryWriteCompletedStatusAsync(statusPath, failure, cancellationToken);
            return failure;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"--repair {QuoteArgument(actionId)} --status {QuoteArgument(statusPath)}",
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process? process = null;
        try
        {
            _logger.Info($"Starting elevated local dependency repair '{actionId}'.");
            process = await StartElevatedRepairProcessAsync(
                startInfo,
                actionId,
                statusPath,
                progressReporter,
                cancellationToken
            );
            if (process is null)
            {
                var failure = Failure(
                    actionId,
                    "修复进程未启动。",
                    "系统未返回提权修复进程。",
                    GetRepairLogPath()
                );
                await TryWriteCompletedStatusAsync(statusPath, failure, cancellationToken);
                return failure;
            }

            var result = await WaitForRepairStatusAsync(
                process,
                actionId,
                statusPath,
                progressReporter,
                cancellationToken
            );
            if (result.Succeeded)
            {
                _processPathRefresher();
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await TerminateRepairProcessAsync(process);
            return Failure(actionId, "修复进程等待超时。", "提权修复进程未在预期时间内返回状态。");
        }
        catch (OperationCanceledException)
        {
            await TerminateRepairProcessAsync(process);
            throw;
        }
        catch (TimeoutException exception)
        {
            var failure = Failure(
                actionId,
                "等待管理员授权超时。",
                exception.Message,
                GetRepairLogPath()
            );
            await TryWriteCompletedStatusAsync(statusPath, failure, cancellationToken);
            return failure;
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode == ElevationCancelledErrorCode)
        {
            var failure = Failure(
                actionId,
                "用户取消了提权授权。",
                exception.Message,
                GetRepairLogPath()
            );
            await TryWriteCompletedStatusAsync(statusPath, failure, cancellationToken);
            return failure;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError($"Failed to start local dependency repair '{actionId}'.", exception);
            var failure = Failure(actionId, "启动修复失败。", exception.Message, GetRepairLogPath());
            await TryWriteCompletedStatusAsync(statusPath, failure, cancellationToken);
            return failure;
        }
    }

    private async Task<LocalDependencyRepairResult> RunRepairInCurrentProcessAsync(
        string actionId,
        Action<LocalDependencyRepairProgress>? progressReporter,
        CancellationToken cancellationToken
    )
    {
        using var repairCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        repairCancellation.CancelAfter(RepairProcessTimeout);
        var progressSink = new CallbackLocalDependencyRepairProgressSink(
            progressReporter,
            _logger
        );
        LocalDependencyRepairResult result;
        Task<LocalDependencyRepairResult>? repairTask = null;
        try
        {
            _logger.Info(
                $"Executing local dependency repair '{actionId}' in current elevated process."
            );
            TryReportProgress(
                progressReporter,
                CreateProgress(
                    actionId,
                    "starting",
                    "正在准备修复。",
                    logPath: TryGetRepairLogPath()
                )
            );
            repairTask = Task.Run(
                () => _commandExecutor.ExecuteAsync(actionId, progressSink, repairCancellation.Token),
                CancellationToken.None
            );
            result = await repairTask;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            repairCancellation.Cancel();
            ObserveRepairTask(repairTask);
            var failure = Failure(
                actionId,
                "修复任务等待超时。",
                "修复任务未在预期时间内返回状态。",
                TryGetRepairLogPath()
            );
            TryReportProgress(
                progressReporter,
                CreateCompletedProgress(failure, progressSink.LastProgress)
            );
            return failure;
        }
        catch (OperationCanceledException)
        {
            repairCancellation.Cancel();
            ObserveRepairTask(repairTask);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                $"Failed to execute local dependency repair '{actionId}' in current process.",
                exception
            );
            var failure = Failure(actionId, "启动修复失败。", exception.Message, TryGetRepairLogPath());
            TryReportProgress(
                progressReporter,
                CreateCompletedProgress(failure, progressSink.LastProgress)
            );
            return failure;
        }

        if (result.Succeeded)
        {
            _processPathRefresher();
        }

        return result;
    }

    private bool IsCurrentProcessRunningAsAdministrator()
    {
        try
        {
            return _currentProcessAdministratorChecker();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Warn(
                $"Failed to determine current process administrator state: {exception.Message}"
            );
            return false;
        }
    }

    private async Task<Process?> StartElevatedRepairProcessAsync(
        ProcessStartInfo startInfo,
        string actionId,
        string statusPath,
        Action<LocalDependencyRepairProgress>? progressReporter,
        CancellationToken cancellationToken
    )
    {
        var launchTask = Task.Run(() => _processStarter(startInfo));
        var startedAt = DateTimeOffset.UtcNow;
        var nextProgressAt = startedAt + ElevationAuthorizationProgressInterval;

        while (!launchTask.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var now = DateTimeOffset.UtcNow;
            if (now - startedAt >= _elevationAuthorizationTimeout)
            {
                ObserveTimedOutLaunchTask(launchTask);
                throw new TimeoutException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"超过 {_elevationAuthorizationTimeout.TotalSeconds:0} 秒未完成管理员授权，修复未启动。"
                    )
                );
            }

            var remainingUntilProgress = nextProgressAt - now;
            if (remainingUntilProgress < TimeSpan.Zero)
            {
                remainingUntilProgress = TimeSpan.Zero;
            }

            var remainingUntilTimeout = _elevationAuthorizationTimeout - (now - startedAt);
            var delay = remainingUntilProgress < remainingUntilTimeout
                ? remainingUntilProgress
                : remainingUntilTimeout;
            if (delay <= TimeSpan.Zero)
            {
                delay = TimeSpan.FromMilliseconds(1);
            }

            var completedTask = await Task.WhenAny(
                launchTask,
                Task.Delay(delay, cancellationToken)
            );
            if (ReferenceEquals(completedTask, launchTask))
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            now = DateTimeOffset.UtcNow;
            if (now >= nextProgressAt)
            {
                nextProgressAt = now + ElevationAuthorizationProgressInterval;
                var progress = CreateProgress(
                    actionId,
                    "starting",
                    "等待管理员授权。",
                    logPath: GetRepairLogPath()
                );
                await TryWriteProgressStatusAsync(statusPath, progress, cancellationToken);
                progressReporter?.Invoke(progress);
            }
        }

        return await launchTask.WaitAsync(cancellationToken);
    }

    private void ObserveTimedOutLaunchTask(Task<Process?> launchTask)
    {
        _ = launchTask.ContinueWith(
            completedTask =>
            {
                if (completedTask.IsFaulted)
                {
                    _logger.LogError(
                        "Timed out elevated local dependency repair launch later failed.",
                        completedTask.Exception
                    );
                    return;
                }

                if (completedTask.Status == TaskStatus.RanToCompletion)
                {
                    TerminateRepairProcessAsync(completedTask.Result).GetAwaiter().GetResult();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private void ObserveRepairTask(Task<LocalDependencyRepairResult>? repairTask)
    {
        if (repairTask is null || repairTask.IsCompletedSuccessfully)
        {
            return;
        }

        _ = repairTask.ContinueWith(
            completedTask =>
            {
                if (completedTask.IsFaulted)
                {
                    _logger.LogError(
                        "Observed failed local dependency repair task.",
                        completedTask.Exception
                    );
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private async Task TryWriteProgressStatusAsync(
        string statusPath,
        LocalDependencyRepairProgress progress,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await WriteStatusAsync(statusPath, progress, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Warn(
                $"Failed to write local dependency repair progress status: {exception.Message}"
            );
        }
    }

    private void TryReportProgress(
        Action<LocalDependencyRepairProgress>? progressReporter,
        LocalDependencyRepairProgress progress
    )
    {
        try
        {
            progressReporter?.Invoke(progress);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Warn(
                $"Failed to report local dependency repair progress: {exception.Message}"
            );
        }
    }

    private async Task TryWriteCompletedStatusAsync(
        string statusPath,
        LocalDependencyRepairResult result,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var previousProgress = TryReadStatus(statusPath, out var progress) ? progress : null;
            await WriteStatusAsync(
                statusPath,
                CreateCompletedProgress(result, previousProgress),
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Warn(
                $"Failed to write local dependency repair completed status: {exception.Message}"
            );
        }
    }

    private static async Task TerminateRepairProcessAsync(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (Win32Exception)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(RepairProcessTerminationWaitTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
    }

    public async Task<LocalDependencyRepairResult> ExecuteRepairModeAsync(
        string actionId,
        string statusPath,
        CancellationToken cancellationToken = default
    )
    {
        var progressSink = new FileLocalDependencyRepairProgressSink(statusPath);
        return await _commandExecutor.ExecuteAsync(actionId, progressSink, cancellationToken);
    }

    private async Task<LocalDependencyRepairResult> WaitForRepairStatusAsync(
        Process process,
        string actionId,
        string statusPath,
        Action<LocalDependencyRepairProgress>? progressReporter,
        CancellationToken cancellationToken
    )
    {
        var firstProgress = await WaitForRepairWorkerProgressAsync(
            process,
            statusPath,
            progressReporter,
            cancellationToken
        );
        if (firstProgress.Result is not null)
        {
            return firstProgress.Result;
        }

        if (!firstProgress.HasWorkerProgress)
        {
            await TerminateRepairProcessAsync(process);
            return CreateMissingWorkerProgressFailure(actionId, statusPath, process);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RepairProcessTimeout);
        var pollResult = await PollRepairStatusUntilAsync(
            () => process.HasExited,
            statusPath,
            progressReporter,
            timeoutCts.Token,
            firstProgress.LastProgressUpdate
        );
        if (pollResult.Result is not null)
        {
            return pollResult.Result;
        }

        await process.WaitForExitAsync(cancellationToken);
        var lastProgressUpdate = pollResult.LastProgressUpdate;
        if (TryReadStatus(statusPath, out var result))
        {
            ReportProgressIfUpdated(result, progressReporter, ref lastProgressUpdate);

            if (result.IsCompleted)
            {
                return CreateResult(result);
            }
        }

        return new LocalDependencyRepairResult
        {
            ActionId = actionId,
            Succeeded = false,
            ExitCode = process.ExitCode,
            Summary = "修复进程未返回状态。",
            Detail = "请查看桌面日志和 repair log 获取详细错误。",
            LogPath = GetRepairLogPath(),
        };
    }

    private async Task<RepairStatusPollResult> WaitForRepairWorkerProgressAsync(
        Process process,
        string statusPath,
        Action<LocalDependencyRepairProgress>? progressReporter,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTimeOffset.UtcNow + _repairProcessFirstProgressTimeout;
        DateTimeOffset? lastProgressUpdate = null;

        while (DateTimeOffset.UtcNow < deadline && !process.HasExited)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryReadStatus(statusPath, out var progress))
            {
                ReportProgressIfUpdated(progress, progressReporter, ref lastProgressUpdate);

                if (progress.IsCompleted)
                {
                    return new RepairStatusPollResult(
                        CreateResult(progress),
                        lastProgressUpdate,
                        HasWorkerProgress: true
                    );
                }

                if (IsRepairWorkerProgress(progress))
                {
                    return new RepairStatusPollResult(
                        null,
                        lastProgressUpdate,
                        HasWorkerProgress: true
                    );
                }
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var delay =
                remaining < RepairProcessPollInterval ? remaining : RepairProcessPollInterval;
            await Task.Delay(delay, cancellationToken);
        }

        if (TryReadStatus(statusPath, out var finalProgress))
        {
            ReportProgressIfUpdated(finalProgress, progressReporter, ref lastProgressUpdate);
            if (finalProgress.IsCompleted)
            {
                return new RepairStatusPollResult(
                    CreateResult(finalProgress),
                    lastProgressUpdate,
                    HasWorkerProgress: true
                );
            }

            if (IsRepairWorkerProgress(finalProgress))
            {
                return new RepairStatusPollResult(
                    null,
                    lastProgressUpdate,
                    HasWorkerProgress: true
                );
            }
        }

        return new RepairStatusPollResult(null, lastProgressUpdate, HasWorkerProgress: false);
    }

    private LocalDependencyRepairResult CreateMissingWorkerProgressFailure(
        string actionId,
        string statusPath,
        Process process
    )
    {
        var detail = string.Create(
            CultureInfo.InvariantCulture,
            $"5 秒内未收到修复工作进程状态。日志：{GetRepairLogPath()}；状态文件：{statusPath}"
        );
        if (process.HasExited)
        {
            detail = string.Create(
                CultureInfo.InvariantCulture,
                $"{detail}；修复进程退出码：{process.ExitCode}"
            );
        }

        return new LocalDependencyRepairResult
        {
            ActionId = actionId,
            Succeeded = false,
            ExitCode = process.HasExited ? process.ExitCode : null,
            Summary = "修复进程未回传进度。",
            Detail = detail,
            LogPath = GetRepairLogPath(),
        };
    }

    private static bool IsRepairWorkerProgress(LocalDependencyRepairProgress progress)
    {
        return progress.IsCompleted
            || !string.Equals(progress.Phase, "starting", StringComparison.Ordinal)
            || string.Equals(progress.Message, "修复工作进程已启动。", StringComparison.Ordinal);
    }

    private static async Task<RepairStatusPollResult> PollRepairStatusUntilAsync(
        Func<bool> shouldStop,
        string statusPath,
        Action<LocalDependencyRepairProgress>? progressReporter,
        CancellationToken cancellationToken,
        DateTimeOffset? lastProgressUpdate = null
    )
    {
        while (!shouldStop())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryReadStatus(statusPath, out var interimProgress))
            {
                ReportProgressIfUpdated(interimProgress, progressReporter, ref lastProgressUpdate);

                if (interimProgress.IsCompleted)
                {
                    return new RepairStatusPollResult(
                        CreateResult(interimProgress),
                        lastProgressUpdate,
                        HasWorkerProgress: true
                    );
                }
            }

            await Task.Delay(RepairProcessPollInterval, cancellationToken);
        }

        return new RepairStatusPollResult(null, lastProgressUpdate, HasWorkerProgress: false);
    }

    private static void ReportProgressIfUpdated(
        LocalDependencyRepairProgress progress,
        Action<LocalDependencyRepairProgress>? progressReporter,
        ref DateTimeOffset? lastProgressUpdate
    )
    {
        if (lastProgressUpdate == progress.UpdatedAt)
        {
            return;
        }

        lastProgressUpdate = progress.UpdatedAt;
        progressReporter?.Invoke(progress);
    }

    private static bool TryReadStatus(string statusPath, out LocalDependencyRepairProgress result)
    {
        result = new LocalDependencyRepairProgress();
        if (!File.Exists(statusPath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(statusPath, Encoding.UTF8);
            var parsed = JsonSerializer.Deserialize<LocalDependencyRepairProgress>(
                json,
                JsonOptions
            );
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.ActionId))
            {
                return false;
            }

            result = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task WriteStatusAsync(
        string statusPath,
        LocalDependencyRepairProgress result,
        CancellationToken cancellationToken
    )
    {
        await AtomicFileWriter.WriteUtf8NoBomTextAsync(
            statusPath,
            JsonSerializer.Serialize(result, JsonOptions),
            cancellationToken
        );
    }

    private string GetRepairLogPath()
    {
        var logPath = GetRepairLogPathWithoutCreatingDirectory();
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        return logPath;
    }

    private string GetRepairLogPathWithoutCreatingDirectory() =>
        Path.Combine(_pathService.Directories.LogsDirectory, "local-environment-repair.log");

    private string? TryGetRepairLogPath()
    {
        try
        {
            return GetRepairLogPathWithoutCreatingDirectory();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Warn($"Failed to resolve local dependency repair log path: {exception.Message}");
            return null;
        }
    }

    private static LocalDependencyRepairProgress CreateProgress(
        string actionId,
        string phase,
        string message,
        string? commandLine = null,
        IReadOnlyList<string>? recentOutput = null,
        string? logPath = null,
        int? exitCode = null
    )
    {
        return new LocalDependencyRepairProgress
        {
            ActionId = actionId,
            Phase = phase,
            Message = message,
            CommandLine = commandLine,
            RecentOutput = recentOutput ?? [],
            LogPath = logPath,
            UpdatedAt = DateTimeOffset.Now,
            ExitCode = exitCode,
            IsCompleted = false,
            Succeeded = false,
            Summary = message,
            Detail = string.Empty,
        };
    }

    private static LocalDependencyRepairProgress CreateCompletedProgress(
        LocalDependencyRepairResult result,
        LocalDependencyRepairProgress? previousProgress
    )
    {
        return new LocalDependencyRepairProgress
        {
            ActionId = result.ActionId,
            Phase = result.Succeeded ? "completed" : "failed",
            Message = result.Summary,
            CommandLine = previousProgress?.CommandLine,
            RecentOutput = previousProgress?.RecentOutput ?? [],
            LogPath = result.LogPath,
            UpdatedAt = DateTimeOffset.Now,
            ExitCode = result.ExitCode,
            IsCompleted = true,
            Succeeded = result.Succeeded,
            Summary = result.Summary,
            Detail = result.Detail,
        };
    }

    private static LocalDependencyRepairResult CreateResult(LocalDependencyRepairProgress progress)
    {
        return new LocalDependencyRepairResult
        {
            ActionId = progress.ActionId,
            Succeeded = progress.Succeeded,
            ExitCode = progress.ExitCode,
            Summary = string.IsNullOrWhiteSpace(progress.Summary)
                ? progress.Message
                : progress.Summary,
            Detail = progress.Detail,
            FailureKind = progress.FailureKind,
            RecommendedFallbackActionId = progress.RecommendedFallbackActionId,
            LogPath = progress.LogPath,
        };
    }

    private static LocalDependencyRepairResult Failure(
        string actionId,
        string summary,
        string detail,
        string? logPath = null
    )
    {
        return new LocalDependencyRepairResult
        {
            ActionId = actionId,
            Succeeded = false,
            Summary = summary,
            Detail = detail,
            LogPath = logPath,
        };
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static void RefreshCurrentProcessPath()
    {
        var entries = new List<string>();
        AddPathEntries(entries, Environment.GetEnvironmentVariable("PATH"));
        AddPathEntries(
            entries,
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)
        );
        AddPathEntries(
            entries,
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)
        );

        foreach (var directory in GetCurrentProcessPathRefreshDirectories())
        {
            if (Directory.Exists(directory))
            {
                AddDistinctPathEntry(entries, directory);
            }
        }

        if (entries.Count > 0)
        {
            Environment.SetEnvironmentVariable(
                "PATH",
                string.Join(Path.PathSeparator, entries),
                EnvironmentVariableTarget.Process
            );
        }
    }

    private static void AddPathEntries(List<string> entries, string? value)
    {
        foreach (var entry in SplitPath(value ?? string.Empty))
        {
            AddDistinctPathEntry(entries, entry);
        }
    }

    private static void AddDistinctPathEntry(List<string> entries, string entry)
    {
        if (!entries.Any(existing => IsSameDirectory(existing, entry)))
        {
            entries.Add(entry);
        }
    }

    private static List<string> GetCurrentProcessPathRefreshDirectories()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(appData))
        {
            candidates.Add(Path.Combine(appData, "npm"));
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "Microsoft", "WindowsApps"));
        }

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "nodejs"));
            candidates.Add(Path.Combine(programFiles, "PowerShell", "7"));
        }

        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            candidates.Add(Path.Combine(programFilesX86, "nodejs"));
        }

        return candidates;
    }

    private static string[] SplitPath(string value)
    {
        return value
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim().Trim('"'))
            .Where(item => item.Length > 0)
            .ToArray();
    }

    private static bool IsSameDirectory(string left, string right)
    {
        return string.Equals(
            NormalizePath(left),
            NormalizePath(right),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static void BroadcastEnvironmentChange()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _ = SendMessageTimeout(
            new IntPtr(0xffff),
            0x001a,
            IntPtr.Zero,
            "Environment",
            0x0002,
            5000,
            out _
        );
    }

    private static bool IsCurrentProcessAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out IntPtr result
    );

    private sealed record RepairStatusPollResult(
        LocalDependencyRepairResult? Result,
        DateTimeOffset? LastProgressUpdate,
        bool HasWorkerProgress
    );
}
