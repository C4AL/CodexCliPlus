using System.Globalization;
using System.Text;
using CodexCliPlus.Core.Abstractions.LocalEnvironment;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models.LocalEnvironment;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Infrastructure.LocalEnvironment;

internal sealed class LocalDependencyRepairCommandExecutor
{
    private const int RecentOutputLineLimit = 20;
    private const string LatestCodexPackage = "@openai/codex@latest";
    private const string RepairWingetScript =
        "$ErrorActionPreference = 'Stop'\n"
        + "$ProgressPreference = 'SilentlyContinue'\n"
        + "Install-PackageProvider -Name NuGet -Force | Out-Null\n"
        + "Install-Module -Name Microsoft.WinGet.Client -Repository PSGallery -Scope AllUsers -Force -AllowClobber | Out-Null\n"
        + "Import-Module Microsoft.WinGet.Client -Force\n"
        + "Repair-WinGetPackageManager -AllUsers -Latest -Force";
    private static readonly TimeSpan RepairCommandTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan DefaultRepairProgressHeartbeatInterval = TimeSpan.FromSeconds(5);

    private readonly IPathService _pathService;
    private readonly IAppLogger _logger;
    private readonly ILocalEnvironmentProcessRunner _processRunner;
    private readonly Func<string, bool> _directoryExists;
    private readonly Action<string> _createDirectory;
    private readonly Func<EnvironmentVariableTarget, string?> _userPathReader;
    private readonly Action<string> _userPathWriter;
    private readonly Action _environmentChangeBroadcaster;
    private readonly Action _processPathRefresher;
    private readonly TimeSpan _repairProgressHeartbeatInterval;

    public LocalDependencyRepairCommandExecutor(
        IPathService pathService,
        IAppLogger logger,
        ILocalEnvironmentProcessRunner processRunner,
        Func<string, bool> directoryExists,
        Action<string> createDirectory,
        Func<EnvironmentVariableTarget, string?> userPathReader,
        Action<string> userPathWriter,
        Action environmentChangeBroadcaster,
        Action processPathRefresher,
        TimeSpan? repairProgressHeartbeatInterval = null
    )
    {
        _pathService = pathService;
        _logger = logger;
        _processRunner = processRunner;
        _directoryExists = directoryExists;
        _createDirectory = createDirectory;
        _userPathReader = userPathReader;
        _userPathWriter = userPathWriter;
        _environmentChangeBroadcaster = environmentChangeBroadcaster;
        _processPathRefresher = processPathRefresher;
        _repairProgressHeartbeatInterval =
            repairProgressHeartbeatInterval is { } interval && interval > TimeSpan.Zero
                ? interval
                : DefaultRepairProgressHeartbeatInterval;
    }

    public async Task<LocalDependencyRepairResult> ExecuteAsync(
        string actionId,
        ILocalDependencyRepairProgressSink progressSink,
        CancellationToken cancellationToken = default
    )
    {
        await _pathService.EnsureCreatedAsync(cancellationToken);
        await progressSink.ReportAsync(
            CreateProgress(
                actionId,
                "starting",
                "修复工作进程已启动。",
                logPath: GetRepairLogPath()
            ),
            cancellationToken
        );

        LocalDependencyRepairResult result;
        try
        {
            if (!LocalDependencyRepairActionIds.IsKnown(actionId))
            {
                result = Failure(actionId, "未知修复动作。", "修复模式拒绝执行非白名单动作。");
            }
            else
            {
                _logger.Info($"Executing local dependency repair '{actionId}'.");
                result = actionId switch
                {
                    LocalDependencyRepairActionIds.InstallNodeNpm => await RunCommandRepairAsync(
                        actionId,
                        "安装 Node.js LTS 和 npm",
                        [
                            new RepairCommand(
                                "winget",
                                [
                                    "install",
                                    "--id",
                                    "OpenJS.NodeJS.LTS",
                                    "-e",
                                    "--source",
                                    "winget",
                                    "--accept-package-agreements",
                                    "--accept-source-agreements",
                                ]
                            ),
                        ],
                        progressSink,
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.InstallPowerShell => await RunCommandRepairAsync(
                        actionId,
                        "安装 PowerShell 7",
                        [
                            new RepairCommand(
                                "winget",
                                [
                                    "install",
                                    "--id",
                                    "Microsoft.PowerShell",
                                    "-e",
                                    "--source",
                                    "winget",
                                    "--accept-package-agreements",
                                    "--accept-source-agreements",
                                ]
                            ),
                        ],
                        progressSink,
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.RepairWinget => await RunCommandRepairAsync(
                        actionId,
                        "修复 winget",
                        [
                            new RepairCommand(
                                "powershell.exe",
                                [
                                    "-NoLogo",
                                    "-NoProfile",
                                    "-NonInteractive",
                                    "-ExecutionPolicy",
                                    "Bypass",
                                    "-Command",
                                    RepairWingetScript,
                                ]
                            ),
                        ],
                        progressSink,
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.RepairRequiredEnvInstallLatestCodex =>
                        await RepairRequiredEnvironmentAndInstallLatestCodexAsync(
                            actionId,
                            progressSink,
                            cancellationToken
                        ),
                    LocalDependencyRepairActionIds.InstallWsl => await RunCommandRepairAsync(
                        actionId,
                        "安装 WSL",
                        [new RepairCommand("wsl.exe", ["--install"])],
                        progressSink,
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.UpdateWsl => await RunCommandRepairAsync(
                        actionId,
                        "更新 WSL",
                        [new RepairCommand("wsl.exe", ["--update"])],
                        progressSink,
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.InstallCodexCli => await RunCommandRepairAsync(
                        actionId,
                        "安装 Codex CLI",
                        [
                            new RepairCommand(
                                "cmd.exe",
                                ["/d", "/c", "npm", "install", "-g", LatestCodexPackage]
                            ),
                        ],
                        progressSink,
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.RepairUserPath => await RepairUserPathAsync(
                        actionId,
                        progressSink,
                        cancellationToken
                    ),
                    _ => Failure(actionId, "未知修复动作。", "修复模式拒绝执行非白名单动作。"),
                };
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError($"Local dependency repair '{actionId}' failed.", exception);
            result = Failure(actionId, "修复执行失败。", exception.Message, GetRepairLogPath());
        }

        await progressSink.ReportAsync(
            CreateCompletedProgress(result, progressSink.LastProgress),
            cancellationToken
        );
        return result;
    }

    private async Task<LocalDependencyRepairResult> RunCommandRepairAsync(
        string actionId,
        string summary,
        IReadOnlyList<RepairCommand> commands,
        ILocalDependencyRepairProgressSink progressSink,
        CancellationToken cancellationToken
    )
    {
        var logPath = GetRepairLogPath();
        await progressSink.ReportAsync(
            CreateProgress(actionId, "running", $"{summary}准备开始。", logPath: logPath),
            cancellationToken
        );

        foreach (var command in commands)
        {
            var failure = await RunRepairCommandStepAsync(
                actionId,
                summary,
                command,
                progressSink,
                logPath,
                cancellationToken
            );
            if (failure is not null)
            {
                return failure;
            }
        }

        _logger.Info($"Local dependency repair '{actionId}' finished.");
        return new LocalDependencyRepairResult
        {
            ActionId = actionId,
            Succeeded = true,
            ExitCode = 0,
            Summary = $"{summary}已完成。",
            Detail = "请重新打开终端或重启 CodexCliPlus 后再次检测。",
            LogPath = logPath,
        };
    }

    private async Task<LocalDependencyRepairResult> RepairUserPathAsync(
        string actionId,
        ILocalDependencyRepairProgressSink progressSink,
        CancellationToken cancellationToken
    )
    {
        var logPath = GetRepairLogPath();
        await progressSink.ReportAsync(
            CreateProgress(
                actionId,
                "running",
                "正在检查并修复用户 PATH。",
                logPath: logPath
            ),
            cancellationToken
        );

        try
        {
            var userPath = _userPathReader(EnvironmentVariableTarget.User) ?? string.Empty;
            var cleanup = CleanUserPathEntries(SplitPath(userPath));
            var existingEntries = cleanup.Entries.ToList();
            var additions = GetAllowedUserPathRepairDirectories()
                .Where(directory => directory.CreateIfMissing || _directoryExists(directory.Path))
                .Select(directory =>
                {
                    if (directory.CreateIfMissing)
                    {
                        _createDirectory(directory.Path);
                    }

                    return directory.Path;
                })
                .Where(directory => !existingEntries.Any(entry => IsSameDirectory(entry, directory)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (
                additions.Length == 0
                && cleanup.DuplicateEntriesRemoved == 0
                && cleanup.UnreachableEntriesRemoved == 0
            )
            {
                await AppendRepairLogAsync(
                    logPath,
                    "User PATH repair did not find required changes.",
                    cancellationToken
                );
                return new LocalDependencyRepairResult
                {
                    ActionId = actionId,
                    Succeeded = true,
                    ExitCode = 0,
                    Summary = "用户 PATH 无需修复。",
                    Detail = "没有发现需要补齐或清理的用户 PATH 项。",
                    LogPath = logPath,
                };
            }

            var updatedEntries = existingEntries.Concat(additions).ToArray();
            _userPathWriter(string.Join(Path.PathSeparator, updatedEntries));
            _environmentChangeBroadcaster();
            await AppendRepairLogAsync(
                logPath,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Added {additions.Length} safe user PATH entries; removed {cleanup.DuplicateEntriesRemoved} duplicate entries; removed {cleanup.UnreachableEntriesRemoved} unreachable entries."
                ),
                cancellationToken
            );
            _logger.Info($"Local dependency repair '{actionId}' updated user PATH.");

            return new LocalDependencyRepairResult
            {
                ActionId = actionId,
                Succeeded = true,
                ExitCode = 0,
                Summary = "用户 PATH 已修复。",
                Detail = BuildUserPathRepairDetail(
                    additions.Length,
                    cleanup.DuplicateEntriesRemoved,
                    cleanup.UnreachableEntriesRemoved
                ),
                LogPath = logPath,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await AppendRepairLogAsync(
                logPath,
                $"User PATH repair failed.{Environment.NewLine}{exception.Message}",
                cancellationToken
            );
            return new LocalDependencyRepairResult
            {
                ActionId = actionId,
                Succeeded = false,
                Summary = "用户 PATH 修复失败。",
                Detail = exception.Message,
                LogPath = logPath,
            };
        }
    }

    private UserPathCleanupResult CleanUserPathEntries(IEnumerable<string> entries)
    {
        var cleanedEntries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateEntriesRemoved = 0;
        var unreachableEntriesRemoved = 0;

        foreach (var entry in entries)
        {
            if (!TryNormalizeResolvedPath(entry, out var normalized))
            {
                cleanedEntries.Add(entry);
                continue;
            }

            if (!_directoryExists(normalized))
            {
                unreachableEntriesRemoved++;
                continue;
            }

            if (!seen.Add(normalized))
            {
                duplicateEntriesRemoved++;
                continue;
            }

            cleanedEntries.Add(entry);
        }

        return new UserPathCleanupResult(
            cleanedEntries,
            duplicateEntriesRemoved,
            unreachableEntriesRemoved
        );
    }

    private async Task<LocalDependencyRepairResult?> RunRepairCommandStepAsync(
        string actionId,
        string summary,
        RepairCommand command,
        ILocalDependencyRepairProgressSink progressSink,
        string logPath,
        CancellationToken cancellationToken
    )
    {
        var commandLine = BuildCommandLine(command);
        await AppendRepairLogAsync(logPath, $"$ {commandLine}", cancellationToken);
        await progressSink.ReportAsync(
            CreateProgress(
                actionId,
                "commandRunning",
                $"正在执行：{summary}。",
                commandLine: commandLine,
                logPath: logPath
            ),
            cancellationToken
        );

        LocalEnvironmentProcessResult result;
        try
        {
            result = await RunProcessWithHeartbeatAsync(
                actionId,
                command,
                RepairCommandTimeout,
                () =>
                    CreateProgress(
                        actionId,
                        "commandRunning",
                        $"正在执行：{summary}。",
                        commandLine: commandLine,
                        logPath: logPath
                    ),
                progressSink,
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await AppendRepairLogAsync(
                logPath,
                $"Command '{commandLine}' failed before completion.{Environment.NewLine}{exception.Message}",
                cancellationToken
            );
            return new LocalDependencyRepairResult
            {
                ActionId = actionId,
                Succeeded = false,
                Summary = $"{summary}失败。",
                Detail = exception.Message,
                LogPath = logPath,
            };
        }

        await AppendRepairLogAsync(logPath, BuildCommandLog(command, result), cancellationToken);
        var recentOutput = ExtractRecentOutput(result.StandardOutput, result.StandardError);
        await progressSink.ReportAsync(
            CreateProgress(
                actionId,
                result.ExitCode == 0 ? "commandCompleted" : "failed",
                result.ExitCode == 0 ? $"{summary}命令已完成。" : $"{summary}命令失败。",
                commandLine: commandLine,
                recentOutput: recentOutput,
                logPath: logPath,
                exitCode: result.ExitCode
            ),
            cancellationToken
        );

        if (result.ExitCode == 0)
        {
            return null;
        }

        var detail =
            FirstNonEmptyLine(result.StandardError, result.StandardOutput) ?? "命令返回非零退出码。";
        return new LocalDependencyRepairResult
        {
            ActionId = actionId,
            Succeeded = false,
            ExitCode = result.ExitCode,
            Summary = $"{summary}失败。",
            Detail = string.Create(
                CultureInfo.InvariantCulture,
                $"退出码 {result.ExitCode}：{detail}"
            ),
            LogPath = logPath,
        };
    }

    private async Task<LocalDependencyRepairResult> RepairRequiredEnvironmentAndInstallLatestCodexAsync(
        string actionId,
        ILocalDependencyRepairProgressSink progressSink,
        CancellationToken cancellationToken
    )
    {
        var logPath = GetRepairLogPath();
        await AppendRepairLogAsync(
            logPath,
            "Starting required local environment repair and latest Codex install.",
            cancellationToken
        );
        await progressSink.ReportAsync(
            CreateProgress(
                actionId,
                "running",
                "正在准备一键修复必备环境并安装最新 Codex。",
                logPath: logPath
            ),
            cancellationToken
        );

        _processPathRefresher();
        var wingetFailure = await EnsureWingetAvailableAsync(
            actionId,
            progressSink,
            logPath,
            cancellationToken
        );
        if (wingetFailure is not null)
        {
            return wingetFailure;
        }

        var nodeNpmFailure = await EnsureNodeNpmAvailableAsync(
            actionId,
            progressSink,
            logPath,
            cancellationToken
        );
        if (nodeNpmFailure is not null)
        {
            return nodeNpmFailure;
        }

        var codexFailure = await RunRepairCommandStepAsync(
            actionId,
            "安装或升级最新 Codex CLI",
            new RepairCommand("cmd.exe", ["/d", "/c", "npm", "install", "-g", LatestCodexPackage]),
            progressSink,
            logPath,
            cancellationToken
        );
        if (codexFailure is not null)
        {
            return codexFailure;
        }

        _processPathRefresher();
        var pathResult = await RepairUserPathAsync(actionId, progressSink, cancellationToken);
        if (!pathResult.Succeeded)
        {
            return new LocalDependencyRepairResult
            {
                ActionId = actionId,
                Succeeded = false,
                ExitCode = pathResult.ExitCode,
                Summary = "一键修复失败。",
                Detail = pathResult.Detail,
                LogPath = logPath,
            };
        }

        _logger.Info($"Local dependency repair '{actionId}' finished.");
        return new LocalDependencyRepairResult
        {
            ActionId = actionId,
            Succeeded = true,
            ExitCode = 0,
            Summary = "一键修复并安装最新 Codex 已完成。",
            Detail = "已处理 winget、Node.js/npm、Codex CLI 和用户 PATH；重新检测后应能读取最新状态。",
            LogPath = logPath,
        };
    }

    private async Task<LocalDependencyRepairResult?> EnsureWingetAvailableAsync(
        string actionId,
        ILocalDependencyRepairProgressSink progressSink,
        string logPath,
        CancellationToken cancellationToken
    )
    {
        if (
            await ProbeRepairCommandAsync(
                actionId,
                "winget",
                new RepairCommand("winget", ["--version"]),
                progressSink,
                logPath,
                cancellationToken
            )
        )
        {
            return null;
        }

        await progressSink.ReportAsync(
            CreateProgress(actionId, "running", "未检测到可用 winget，正在尝试修复。", logPath: logPath),
            cancellationToken
        );
        var repairFailure = await RunRepairCommandStepAsync(
            actionId,
            "修复 winget",
            new RepairCommand(
                "powershell.exe",
                [
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    RepairWingetScript,
                ]
            ),
            progressSink,
            logPath,
            cancellationToken
        );
        if (repairFailure is not null)
        {
            return repairFailure;
        }

        _processPathRefresher();
        if (
            await ProbeRepairCommandAsync(
                actionId,
                "winget",
                new RepairCommand("winget", ["--version"]),
                progressSink,
                logPath,
                cancellationToken
            )
        )
        {
            return null;
        }

        return new LocalDependencyRepairResult
        {
            ActionId = actionId,
            Succeeded = false,
            Summary = "winget 修复后仍不可用。",
            Detail = "无法继续安装 Node.js/npm，请查看修复日志确认 winget 状态。",
            LogPath = logPath,
        };
    }

    private async Task<LocalDependencyRepairResult?> EnsureNodeNpmAvailableAsync(
        string actionId,
        ILocalDependencyRepairProgressSink progressSink,
        string logPath,
        CancellationToken cancellationToken
    )
    {
        var nodeReady = await ProbeRepairCommandAsync(
            actionId,
            "Node.js",
            new RepairCommand("node", ["--version"]),
            progressSink,
            logPath,
            cancellationToken
        );
        var npmReady = await ProbeRepairCommandAsync(
            actionId,
            "npm",
            new RepairCommand("cmd.exe", ["/d", "/c", "npm", "--version"]),
            progressSink,
            logPath,
            cancellationToken
        );
        if (nodeReady && npmReady)
        {
            return null;
        }

        var installFailure = await RunRepairCommandStepAsync(
            actionId,
            "安装 Node.js LTS 和 npm",
            new RepairCommand(
                "winget",
                [
                    "install",
                    "--id",
                    "OpenJS.NodeJS.LTS",
                    "-e",
                    "--source",
                    "winget",
                    "--accept-package-agreements",
                    "--accept-source-agreements",
                ]
            ),
            progressSink,
            logPath,
            cancellationToken
        );
        if (installFailure is not null)
        {
            return installFailure;
        }

        _processPathRefresher();
        var installedNodeReady = await ProbeRepairCommandAsync(
            actionId,
            "Node.js",
            new RepairCommand("node", ["--version"]),
            progressSink,
            logPath,
            cancellationToken
        );
        var installedNpmReady = await ProbeRepairCommandAsync(
            actionId,
            "npm",
            new RepairCommand("cmd.exe", ["/d", "/c", "npm", "--version"]),
            progressSink,
            logPath,
            cancellationToken
        );
        if (installedNodeReady && installedNpmReady)
        {
            return null;
        }

        return new LocalDependencyRepairResult
        {
            ActionId = actionId,
            Succeeded = false,
            Summary = "Node.js/npm 安装后仍不可用。",
            Detail = "无法继续安装 Codex CLI，请查看修复日志确认 node 和 npm 状态。",
            LogPath = logPath,
        };
    }

    private async Task<bool> ProbeRepairCommandAsync(
        string actionId,
        string name,
        RepairCommand command,
        ILocalDependencyRepairProgressSink progressSink,
        string logPath,
        CancellationToken cancellationToken
    )
    {
        var commandLine = BuildCommandLine(command);
        await AppendRepairLogAsync(logPath, $"$ {commandLine}", cancellationToken);
        await progressSink.ReportAsync(
            CreateProgress(
                actionId,
                "commandRunning",
                $"正在检测：{name}。",
                commandLine: commandLine,
                logPath: logPath
            ),
            cancellationToken
        );

        try
        {
            var result = await RunProcessWithHeartbeatAsync(
                actionId,
                command,
                TimeSpan.FromSeconds(15),
                () =>
                    CreateProgress(
                        actionId,
                        "commandRunning",
                        $"正在检测：{name}。",
                        commandLine: commandLine,
                        logPath: logPath
                    ),
                progressSink,
                cancellationToken
            );
            await AppendRepairLogAsync(logPath, BuildCommandLog(command, result), cancellationToken);
            await progressSink.ReportAsync(
                CreateProgress(
                    actionId,
                    result.ExitCode == 0 ? "commandCompleted" : "running",
                    result.ExitCode == 0 ? $"{name}检测通过。" : $"{name}检测未通过。",
                    commandLine: commandLine,
                    recentOutput: ExtractRecentOutput(
                        result.StandardOutput,
                        result.StandardError
                    ),
                    logPath: logPath,
                    exitCode: result.ExitCode
                ),
                cancellationToken
            );
            return result.ExitCode == 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await AppendRepairLogAsync(
                logPath,
                $"Probe '{commandLine}' failed before completion.{Environment.NewLine}{exception.Message}",
                cancellationToken
            );
            await progressSink.ReportAsync(
                CreateProgress(
                    actionId,
                    "running",
                    $"{name}检测未通过。",
                    commandLine: commandLine,
                    recentOutput: [exception.Message],
                    logPath: logPath
                ),
                cancellationToken
            );
            return false;
        }
    }

    private async Task<LocalEnvironmentProcessResult> RunProcessWithHeartbeatAsync(
        string actionId,
        RepairCommand command,
        TimeSpan timeout,
        Func<LocalDependencyRepairProgress> heartbeatFactory,
        ILocalDependencyRepairProgressSink progressSink,
        CancellationToken cancellationToken
    )
    {
        var processTask = _processRunner.RunAsync(
            command.FileName,
            command.Arguments,
            timeout,
            cancellationToken
        );

        while (true)
        {
            var completedTask = await Task.WhenAny(
                processTask,
                Task.Delay(_repairProgressHeartbeatInterval, cancellationToken)
            );
            if (ReferenceEquals(completedTask, processTask))
            {
                return await processTask;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var heartbeat = heartbeatFactory();
            await progressSink.ReportAsync(heartbeat, cancellationToken);
            _logger.Info(
                $"Local dependency repair '{actionId}' heartbeat: {heartbeat.Phase}."
            );
        }
    }

    private string GetRepairLogPath()
    {
        var logPath = Path.Combine(
            _pathService.Directories.LogsDirectory,
            "local-environment-repair.log"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        return logPath;
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

    private static List<AllowedPathDirectory> GetAllowedUserPathRepairDirectories()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new List<AllowedPathDirectory>();
        if (!string.IsNullOrWhiteSpace(appData))
        {
            candidates.Add(
                new AllowedPathDirectory(Path.Combine(appData, "npm"), CreateIfMissing: true)
            );
        }

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(new AllowedPathDirectory(Path.Combine(programFiles, "nodejs")));
            candidates.Add(new AllowedPathDirectory(Path.Combine(programFiles, "PowerShell", "7")));
        }

        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            candidates.Add(new AllowedPathDirectory(Path.Combine(programFilesX86, "nodejs")));
        }

        return candidates;
    }

    private static async Task AppendRepairLogAsync(
        string logPath,
        string message,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var line =
            $"{DateTimeOffset.Now:O} {SensitiveDataRedactor.Redact(message)}{Environment.NewLine}";
        await File.AppendAllTextAsync(
            logPath,
            line,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken
        );
    }

    private static string BuildCommandLog(
        RepairCommand command,
        LocalEnvironmentProcessResult result
    )
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Command '{BuildCommandLine(command)}' exited {result.ExitCode}.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}"
        );
    }

    private static string BuildCommandLine(RepairCommand command)
    {
        return $"{command.FileName} {string.Join(" ", command.Arguments)}";
    }

    private static string[] ExtractRecentOutput(params string[] values)
    {
        return values
            .SelectMany(value =>
                value.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(RecentOutputLineLimit)
            .ToArray();
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

    private static string? FirstNonEmptyLine(params string[] values)
    {
        return values
            .SelectMany(value =>
                value.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
    }

    private static string BuildUserPathRepairDetail(
        int additions,
        int duplicateEntriesRemoved,
        int unreachableEntriesRemoved
    )
    {
        var changes = new List<string>();
        if (additions > 0)
        {
            changes.Add(
                string.Create(CultureInfo.InvariantCulture, $"补齐 {additions} 个安全目录")
            );
        }

        if (duplicateEntriesRemoved > 0)
        {
            changes.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"清理 {duplicateEntriesRemoved} 个重复目录"
                )
            );
        }

        if (unreachableEntriesRemoved > 0)
        {
            changes.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"清理 {unreachableEntriesRemoved} 个失效目录"
                )
            );
        }

        return $"已{string.Join("，", changes)}。新终端和重启后的 CodexCliPlus 会读取更新后的 PATH。";
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

    private static bool TryNormalizeResolvedPath(string path, out string normalized)
    {
        normalized = string.Empty;
        var trimmed = path.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return false;
        }

        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        if (expanded.Contains('%', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(expanded)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalized.Length > 0;
        }
        catch
        {
            return false;
        }
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

    private sealed record RepairCommand(string FileName, IReadOnlyList<string> Arguments);

    private sealed record AllowedPathDirectory(string Path, bool CreateIfMissing = false);

    private sealed record UserPathCleanupResult(
        IReadOnlyList<string> Entries,
        int DuplicateEntriesRemoved,
        int UnreachableEntriesRemoved
    );
}
