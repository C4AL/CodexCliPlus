using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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
    private const int MsiRebootRequiredSuccessExitCode = 3010;
    private const int WingetSourcesInvalidExitCode = unchecked((int)0x8A15000B);
    private const int WingetSourceDataMissingExitCode = unchecked((int)0x8A15000F);
    private const int WingetSourceNameNotFoundExitCode = unchecked((int)0x8A150012);
    private const int WingetDownloadFailedExitCode = unchecked((int)0x8A150008);
    private const int WingetCommandRequiresAdminExitCode = unchecked((int)0x8A150019);
    private const int WingetSourceDataIntegrityFailureExitCode = unchecked((int)0x8A15003F);
    private const int WingetPackageAgreementsNotAcceptedExitCode = unchecked((int)0x8A150041);
    private const int WingetSourceOpenFailedExitCode = unchecked((int)0x8A150045);
    private const int WingetSourceAgreementsNotAcceptedExitCode = unchecked((int)0x8A150046);
    private const int WingetFailedToOpenAllSourcesExitCode = unchecked((int)0x8A15004B);
    private const int WingetPackageAlreadyInstalledExitCode = unchecked((int)0x8A150061);
    private const int WingetInstallCancelledByUserExitCode = unchecked((int)0x8A15010C);
    private const string NetworkFailureKind = "network";
    private const string LatestCodexPackage = "@openai/codex@latest";
    private const string NodeDistributionBaseUrl = "https://nodejs.org/dist/";
    private const string RepairWingetScript =
        "$ErrorActionPreference = 'Stop'\n"
        + "$ProgressPreference = 'SilentlyContinue'\n"
        + "Install-PackageProvider -Name NuGet -Force | Out-Null\n"
        + "Install-Module -Name Microsoft.WinGet.Client -Repository PSGallery -Scope AllUsers -Force -AllowClobber | Out-Null\n"
        + "Import-Module Microsoft.WinGet.Client -Force\n"
        + "Repair-WinGetPackageManager -AllUsers -Latest -Force";
    private static readonly TimeSpan RepairCommandTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan DefaultRepairProgressHeartbeatInterval = TimeSpan.FromSeconds(5);
    private static readonly Uri NodeDistributionIndexUri = new(NodeDistributionBaseUrl + "index.json");
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
    };
    private static readonly JsonSerializerOptions NodeReleaseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IPathService _pathService;
    private readonly IAppLogger _logger;
    private readonly ILocalEnvironmentProcessRunner _processRunner;
    private readonly Func<Uri, CancellationToken, Task<string>> _downloadStringAsync;
    private readonly Func<Uri, string, CancellationToken, Task> _downloadFileAsync;
    private readonly Func<Architecture> _osArchitectureProvider;
    private readonly Func<string, bool> _directoryExists;
    private readonly Action<string> _createDirectory;
    private readonly Func<EnvironmentVariableTarget, string?> _userPathReader;
    private readonly Action<string> _userPathWriter;
    private readonly Action _environmentChangeBroadcaster;
    private readonly Action _processPathRefresher;
    private readonly TimeSpan _repairProgressHeartbeatInterval;
    private readonly LocalEnvironmentOfflinePackageService _offlinePackageService;

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
        _downloadStringAsync = downloadStringAsync ?? DownloadStringAsync;
        _downloadFileAsync = downloadFileAsync ?? DownloadFileAsync;
        _osArchitectureProvider = osArchitectureProvider ?? (() => RuntimeInformation.OSArchitecture);
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
        _offlinePackageService =
            offlinePackageService ?? new LocalEnvironmentOfflinePackageService(_pathService, _logger);
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
                    LocalDependencyRepairActionIds.InstallNodeNpm =>
                        await InstallNodeNpmAsync(actionId, progressSink, cancellationToken),
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
                        cancellationToken,
                        repairWingetSourceOnInstallFailure: true
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
                    LocalDependencyRepairActionIds.RepairRequiredEnvInstallBundledCodex =>
                        await RepairRequiredEnvironmentAndInstallBundledCodexAsync(
                            actionId,
                            progressSink,
                            cancellationToken
                        ),
                    LocalDependencyRepairActionIds.UpgradeBundledEnvInstallLatestCodex =>
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
        CancellationToken cancellationToken,
        bool repairWingetSourceOnInstallFailure = false
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
                cancellationToken,
                repairWingetSourceOnInstallFailure
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
            Detail = "CodexCliPlus 会立即重新检测；已打开的外部终端需重新打开后读取更新后的环境。",
            LogPath = logPath,
        };
    }

    private async Task<LocalDependencyRepairResult> InstallNodeNpmAsync(
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
                "正在准备内置 Node.js LTS 和 npm 安装。",
                logPath: logPath
            ),
            cancellationToken
        );

        var installFailure = await InstallNodeLtsFromOfficialInstallerAsync(
            actionId,
            progressSink,
            logPath,
            cancellationToken
        );
        if (installFailure is not null)
        {
            return installFailure;
        }

        _processPathRefresher();
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
            _logger.Info($"Local dependency repair '{actionId}' installed Node.js and npm.");
            return new LocalDependencyRepairResult
            {
                ActionId = actionId,
                Succeeded = true,
                ExitCode = 0,
                Summary = "安装 Node.js LTS 和 npm 已完成。",
                Detail = "已通过内置安装流程安装 Node.js 官方 LTS 安装包；CodexCliPlus 会立即重新检测，已打开的外部终端需重新打开后读取更新后的环境。",
                LogPath = logPath,
            };
        }

        return CreateNodeNpmUnavailableAfterInstallFailure(actionId, logPath);
    }

    private async Task<LocalDependencyRepairResult?> InstallNodeLtsFromOfficialInstallerAsync(
        string actionId,
        ILocalDependencyRepairProgressSink progressSink,
        string logPath,
        CancellationToken cancellationToken
    )
    {
        NodeLtsInstallPlan installPlan;
        try
        {
            await AppendRepairLogAsync(
                logPath,
                $"Resolving Node.js LTS release from {NodeDistributionIndexUri}.",
                cancellationToken
            );
            await progressSink.ReportAsync(
                CreateProgress(
                    actionId,
                    "running",
                    "正在解析 Node.js 官方 LTS 安装包。",
                    commandLine: NodeDistributionIndexUri.ToString(),
                    logPath: logPath
                ),
                cancellationToken
            );
            installPlan = await BuildNodeLtsInstallPlanAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await AppendRepairLogAsync(
                logPath,
                $"Failed to resolve Node.js LTS installer.{Environment.NewLine}{exception.Message}",
                cancellationToken
            );
            return new LocalDependencyRepairResult
            {
                ActionId = actionId,
                Succeeded = false,
                Summary = "解析 Node.js LTS 安装包失败。",
                Detail = exception.Message,
                FailureKind = IsNetworkException(exception) ? NetworkFailureKind : null,
                RecommendedFallbackActionId = IsNetworkException(exception)
                    ? LocalDependencyRepairActionIds.RepairRequiredEnvInstallBundledCodex
                    : null,
                LogPath = logPath,
            };
        }

        var downloadFailure = await DownloadNodeInstallerAsync(
            actionId,
            installPlan,
            progressSink,
            logPath,
            cancellationToken
        );
        if (downloadFailure is not null)
        {
            return downloadFailure;
        }

        var installFailure = await RunRepairCommandStepAsync(
            actionId,
            "静默安装 Node.js LTS 和 npm",
            new RepairCommand(
                "msiexec.exe",
                ["/i", installPlan.InstallerPath, "/qn", "/norestart"],
                SuccessfulExitCodes: [0, MsiRebootRequiredSuccessExitCode]
            ),
            progressSink,
            logPath,
            cancellationToken
        );
        if (installFailure is null)
        {
            return null;
        }

        return new LocalDependencyRepairResult
        {
            ActionId = actionId,
            Succeeded = false,
            ExitCode = installFailure.ExitCode,
            Summary = "安装 Node.js LTS 和 npm失败。",
            Detail = installFailure.Detail,
            LogPath = logPath,
        };
    }

    private async Task<LocalDependencyRepairResult?> DownloadNodeInstallerAsync(
        string actionId,
        NodeLtsInstallPlan installPlan,
        ILocalDependencyRepairProgressSink progressSink,
        string logPath,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(installPlan.CacheDirectory);
        var temporaryPath = installPlan.InstallerPath + ".download";
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            await AppendRepairLogAsync(
                logPath,
                $"Downloading Node.js {installPlan.Version} installer from {installPlan.DownloadUri}.",
                cancellationToken
            );
            await progressSink.ReportAsync(
                CreateProgress(
                    actionId,
                    "commandRunning",
                    $"正在下载 Node.js {installPlan.Version} LTS 官方安装包。",
                    commandLine: installPlan.DownloadUri.ToString(),
                    logPath: logPath
                ),
                cancellationToken
            );
            await _downloadFileAsync(installPlan.DownloadUri, temporaryPath, cancellationToken);

            var downloadedFile = new FileInfo(temporaryPath);
            if (!downloadedFile.Exists || downloadedFile.Length <= 0)
            {
                throw new InvalidOperationException("下载后的 Node.js 安装包为空。");
            }

            File.Move(temporaryPath, installPlan.InstallerPath, overwrite: true);
            await AppendRepairLogAsync(
                logPath,
                $"Downloaded Node.js installer to {installPlan.InstallerPath}.",
                cancellationToken
            );
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await AppendRepairLogAsync(
                logPath,
                $"Failed to download Node.js installer.{Environment.NewLine}{exception.Message}",
                cancellationToken
            );
            return new LocalDependencyRepairResult
            {
                ActionId = actionId,
                Succeeded = false,
                Summary = "下载 Node.js LTS 安装包失败。",
                Detail = exception.Message,
                FailureKind = IsNetworkException(exception) ? NetworkFailureKind : null,
                RecommendedFallbackActionId = IsNetworkException(exception)
                    ? LocalDependencyRepairActionIds.RepairRequiredEnvInstallBundledCodex
                    : null,
                LogPath = logPath,
            };
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
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
        CancellationToken cancellationToken,
        bool repairWingetSourceOnInstallFailure = false
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
        var shouldRepairWingetSource =
            repairWingetSourceOnInstallFailure
            && ShouldRetryAfterWingetSourceRepair(command, result.ExitCode);
        var commandSucceeded = IsSuccessExitCode(command, result.ExitCode);
        await progressSink.ReportAsync(
            CreateProgress(
                actionId,
                commandSucceeded ? "commandCompleted" : shouldRepairWingetSource ? "running" : "failed",
                commandSucceeded
                    ? $"{summary}命令已完成。"
                    : shouldRepairWingetSource
                        ? "检测到 winget 源数据异常，正在修复源后重试。"
                        : $"{summary}命令失败。",
                commandLine: commandLine,
                recentOutput: recentOutput,
                logPath: logPath,
                exitCode: result.ExitCode
            ),
            cancellationToken
        );

        if (commandSucceeded)
        {
            return null;
        }

        if (shouldRepairWingetSource)
        {
            return await RepairWingetSourceAndRetryCommandAsync(
                actionId,
                summary,
                command,
                progressSink,
                logPath,
                cancellationToken
            );
        }

        var detail = BuildCommandFailureDetail(command, result);
        var npmNetworkFailure = IsNpmOnlineInstallCommand(command)
            && IsNpmNetworkFailureOutput(result.StandardOutput, result.StandardError);
        return new LocalDependencyRepairResult
        {
            ActionId = actionId,
            Succeeded = false,
            ExitCode = result.ExitCode,
            Summary = $"{summary}失败。",
            Detail = detail,
            FailureKind = npmNetworkFailure ? NetworkFailureKind : null,
            RecommendedFallbackActionId = npmNetworkFailure
                ? LocalDependencyRepairActionIds.RepairRequiredEnvInstallBundledCodex
                : null,
            LogPath = logPath,
        };
    }

    private async Task<LocalDependencyRepairResult?> RepairWingetSourceAndRetryCommandAsync(
        string actionId,
        string summary,
        RepairCommand command,
        ILocalDependencyRepairProgressSink progressSink,
        string logPath,
        CancellationToken cancellationToken
    )
    {
        var commandLine = BuildCommandLine(command);
        var sourceUpdateFailure = await RunRepairCommandStepAsync(
            actionId,
            "更新 winget 源",
            CreateWingetSourceUpdateCommand(),
            progressSink,
            logPath,
            cancellationToken
        );

        if (sourceUpdateFailure is null)
        {
            await progressSink.ReportAsync(
                CreateProgress(
                    actionId,
                    "commandRunning",
                    $"winget 源已更新，正在重试：{summary}。",
                    commandLine: commandLine,
                    logPath: logPath
                ),
                cancellationToken
            );

            var retryFailure = await RunRepairCommandStepAsync(
                actionId,
                summary,
                command,
                progressSink,
                logPath,
                cancellationToken
            );
            if (
                retryFailure is null
                || retryFailure.ExitCode is not { } retryExitCode
                || !ShouldRetryAfterWingetSourceRepair(command, retryExitCode)
            )
            {
                return retryFailure;
            }

            await progressSink.ReportAsync(
                CreateProgress(
                    actionId,
                    "running",
                    "更新 winget 源后仍无法读取源，正在重置源后重试。",
                    commandLine: commandLine,
                    recentOutput: [retryFailure.Detail],
                    logPath: logPath,
                    exitCode: retryFailure.ExitCode
                ),
                cancellationToken
            );
        }
        else
        {
            await progressSink.ReportAsync(
                CreateProgress(
                    actionId,
                    "running",
                    "更新 winget 源失败，正在重置源后重试。",
                    recentOutput: [sourceUpdateFailure.Detail],
                    logPath: logPath,
                    exitCode: sourceUpdateFailure.ExitCode
                ),
                cancellationToken
            );
        }

        var sourceResetFailure = await RunRepairCommandStepAsync(
            actionId,
            "重置 winget 源",
            CreateWingetSourceResetCommand(),
            progressSink,
            logPath,
            cancellationToken
        );
        if (sourceResetFailure is not null)
        {
            return new LocalDependencyRepairResult
            {
                ActionId = actionId,
                Succeeded = false,
                ExitCode = sourceResetFailure.ExitCode,
                Summary = $"{summary}失败。",
                Detail = $"winget 源数据不可用，自动重置源失败：{sourceResetFailure.Detail}",
                LogPath = logPath,
            };
        }

        var sourceUpdateAfterResetFailure = await RunRepairCommandStepAsync(
            actionId,
            "更新 winget 源",
            CreateWingetSourceUpdateCommand(),
            progressSink,
            logPath,
            cancellationToken
        );
        if (sourceUpdateAfterResetFailure is not null)
        {
            return new LocalDependencyRepairResult
            {
                ActionId = actionId,
                Succeeded = false,
                ExitCode = sourceUpdateAfterResetFailure.ExitCode,
                Summary = $"{summary}失败。",
                Detail = $"winget 源已重置，但自动更新源失败：{sourceUpdateAfterResetFailure.Detail}",
                LogPath = logPath,
            };
        }

        await progressSink.ReportAsync(
            CreateProgress(
                actionId,
                "commandRunning",
                $"winget 源已重置并更新，正在重试：{summary}。",
                commandLine: commandLine,
                logPath: logPath
            ),
            cancellationToken
        );

        return await RunRepairCommandStepAsync(
            actionId,
            summary,
            command,
            progressSink,
            logPath,
            cancellationToken
        );
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
            Detail = "已处理 Node.js/npm、Codex CLI 和用户 PATH；重新检测后应能读取最新状态。",
            LogPath = logPath,
        };
    }

    private async Task<LocalDependencyRepairResult> RepairRequiredEnvironmentAndInstallBundledCodexAsync(
        string actionId,
        ILocalDependencyRepairProgressSink progressSink,
        CancellationToken cancellationToken
    )
    {
        var logPath = GetRepairLogPath();
        await AppendRepairLogAsync(
            logPath,
            "Starting bundled local environment repair.",
            cancellationToken
        );
        await progressSink.ReportAsync(
            CreateProgress(
                actionId,
                "running",
                "正在准备内置离线包修复本地环境。",
                logPath: logPath
            ),
            cancellationToken
        );

        LocalEnvironmentBundledInstallPlan installPlan;
        try
        {
            installPlan = await _offlinePackageService.PrepareInstallAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await AppendRepairLogAsync(
                logPath,
                $"Bundled local environment package validation failed.{Environment.NewLine}{exception.Message}",
                cancellationToken
            );
            return new LocalDependencyRepairResult
            {
                ActionId = actionId,
                Succeeded = false,
                Summary = "内置离线包不可用。",
                Detail = exception.Message,
                LogPath = logPath,
            };
        }

        _processPathRefresher();
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
        if (!nodeReady || !npmReady)
        {
            var nodeInstallFailure = await RunRepairCommandStepAsync(
                actionId,
                "安装内置 Node.js LTS 和 npm",
                new RepairCommand(
                    "msiexec.exe",
                    ["/i", installPlan.NodeInstallerPath, "/qn", "/norestart"],
                    SuccessfulExitCodes: [0, MsiRebootRequiredSuccessExitCode]
                ),
                progressSink,
                logPath,
                cancellationToken
            );
            if (nodeInstallFailure is not null)
            {
                return new LocalDependencyRepairResult
                {
                    ActionId = actionId,
                    Succeeded = false,
                    ExitCode = nodeInstallFailure.ExitCode,
                    Summary = "安装内置 Node.js LTS 和 npm 失败。",
                    Detail = nodeInstallFailure.Detail,
                    LogPath = logPath,
                };
            }

            _processPathRefresher();
            nodeReady = await ProbeRepairCommandAsync(
                actionId,
                "Node.js",
                new RepairCommand("node", ["--version"]),
                progressSink,
                logPath,
                cancellationToken
            );
            npmReady = await ProbeRepairCommandAsync(
                actionId,
                "npm",
                new RepairCommand("cmd.exe", ["/d", "/c", "npm", "--version"]),
                progressSink,
                logPath,
                cancellationToken
            );
            if (!nodeReady || !npmReady)
            {
                return CreateNodeNpmUnavailableAfterInstallFailure(actionId, logPath);
            }
        }

        var codexPackage = $"@openai/codex@{installPlan.Manifest.Codex.Version}";
        var codexFailure = await RunRepairCommandStepAsync(
            actionId,
            "使用内置离线缓存安装 Codex CLI",
            new RepairCommand(
                "cmd.exe",
                [
                    "/d",
                    "/c",
                    "npm",
                    "install",
                    "-g",
                    codexPackage,
                    "--offline",
                    "--cache",
                    installPlan.WritableNpmCachePath,
                ]
            ),
            progressSink,
            logPath,
            cancellationToken
        );
        if (codexFailure is not null)
        {
            return new LocalDependencyRepairResult
            {
                ActionId = actionId,
                Succeeded = false,
                ExitCode = codexFailure.ExitCode,
                Summary = "离线安装 Codex CLI 失败。",
                Detail = codexFailure.Detail,
                LogPath = logPath,
            };
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
                Summary = "离线修复失败。",
                Detail = pathResult.Detail,
                LogPath = logPath,
            };
        }

        _processPathRefresher();
        var codexReady = await ProbeRepairCommandAsync(
            actionId,
            "Codex CLI",
            new RepairCommand("cmd.exe", ["/d", "/c", "codex", "--version"]),
            progressSink,
            logPath,
            cancellationToken
        );
        if (!codexReady)
        {
            return new LocalDependencyRepairResult
            {
                ActionId = actionId,
                Succeeded = false,
                Summary = "离线安装后 Codex CLI 仍不可用。",
                Detail = "无法确认 codex 命令可执行，请查看修复日志确认 npm 全局安装目录和 PATH。",
                LogPath = logPath,
            };
        }

        await _offlinePackageService.WritePendingUpgradeAsync(
            installPlan.Manifest,
            cancellationToken
        );

        _logger.Info($"Local dependency repair '{actionId}' installed bundled Codex.");
        return new LocalDependencyRepairResult
        {
            ActionId = actionId,
            Succeeded = true,
            ExitCode = 0,
            Summary = "已使用内置离线包临时安装本地环境。",
            Detail = "已安装 Node.js/npm 和 Codex CLI；应用会在网络恢复后提示升级到最新版本。",
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

        var installFailure = await InstallNodeLtsFromOfficialInstallerAsync(
            actionId,
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

        return CreateNodeNpmUnavailableAfterInstallFailure(actionId, logPath);
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

    private async Task<NodeLtsInstallPlan> BuildNodeLtsInstallPlanAsync(
        CancellationToken cancellationToken
    )
    {
        var indexJson = await _downloadStringAsync(NodeDistributionIndexUri, cancellationToken);
        var releases = JsonSerializer.Deserialize<List<NodeReleaseIndexEntry>>(
            indexJson,
            NodeReleaseJsonOptions
        );
        if (releases is null || releases.Count == 0)
        {
            throw new InvalidOperationException("Node.js 官方版本索引为空。");
        }

        var architecture = ResolveNodeWindowsArchitecture(_osArchitectureProvider());
        var assetKey = $"win-{architecture}-msi";
        var release = releases.FirstOrDefault(candidate =>
            IsLtsRelease(candidate)
            && IsValidNodeVersion(candidate.Version)
            && candidate.Files is not null
            && candidate.Files.Contains(assetKey, StringComparer.OrdinalIgnoreCase)
        );
        if (release is null || string.IsNullOrWhiteSpace(release.Version))
        {
            throw new InvalidOperationException(
                $"未在 Node.js 官方版本索引中找到适用于 win-{architecture} 的 LTS 安装包。"
            );
        }

        var version = release.Version.Trim();
        var installerFileName = $"node-{version}-{architecture}.msi";
        var cacheDirectory = Path.Combine(
            _pathService.Directories.CacheDirectory,
            "local-environment",
            "nodejs",
            version
        );
        return new NodeLtsInstallPlan(
            version,
            architecture,
            new Uri($"{NodeDistributionBaseUrl}{version}/{installerFileName}"),
            cacheDirectory,
            Path.Combine(cacheDirectory, installerFileName)
        );
    }

    private static string ResolveNodeWindowsArchitecture(Architecture architecture) =>
        architecture switch
        {
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            _ => "x64",
        };

    private static bool IsLtsRelease(NodeReleaseIndexEntry release)
    {
        return release.Lts.ValueKind
            is not JsonValueKind.False
                and not JsonValueKind.Null
                and not JsonValueKind.Undefined;
    }

    private static bool IsValidNodeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || !version.StartsWith('v'))
        {
            return false;
        }

        return version.Skip(1).All(character => char.IsDigit(character) || character == '.');
    }

    private static LocalDependencyRepairResult CreateNodeNpmUnavailableAfterInstallFailure(
        string actionId,
        string logPath
    )
    {
        return new LocalDependencyRepairResult
        {
            ActionId = actionId,
            Succeeded = false,
            Summary = "Node.js/npm 安装后仍不可用。",
            Detail = "无法继续安装 Codex CLI，请查看修复日志确认 node 和 npm 状态。",
            LogPath = logPath,
        };
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
            FailureKind = result.FailureKind,
            RecommendedFallbackActionId = result.RecommendedFallbackActionId,
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

    private static async Task<string> DownloadStringAsync(
        Uri uri,
        CancellationToken cancellationToken
    )
    {
        using var response = await SharedHttpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async Task DownloadFileAsync(
        Uri uri,
        string destinationPath,
        CancellationToken cancellationToken
    )
    {
        using var response = await SharedHttpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true
        );
        await input.CopyToAsync(output, cancellationToken);
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

    private static bool IsSuccessExitCode(RepairCommand command, int exitCode) =>
        command.SuccessfulExitCodes?.Contains(exitCode) ?? exitCode == 0;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }

    private static string[] ExtractRecentOutput(params string[] values)
    {
        return values
            .SelectMany(SplitOutputLines)
            .Where(IsMeaningfulOutputLine)
            .TakeLast(RecentOutputLineLimit)
            .ToArray();
    }

    private static string BuildCommandFailureDetail(
        RepairCommand command,
        LocalEnvironmentProcessResult result
    )
    {
        var knownReason = DescribeKnownExitCode(command, result.ExitCode);
        var outputDetail = FirstMeaningfulOutputLine(result.StandardError, result.StandardOutput);
        var exitCode = FormatExitCode(result.ExitCode);

        if (!string.IsNullOrWhiteSpace(knownReason))
        {
            return string.IsNullOrWhiteSpace(outputDetail)
                ? $"退出码 {exitCode}：{knownReason}"
                : $"退出码 {exitCode}：{knownReason}；{outputDetail}";
        }

        return string.IsNullOrWhiteSpace(outputDetail)
            ? $"退出码 {exitCode}：命令返回非零退出码。"
            : $"退出码 {exitCode}：{outputDetail}";
    }

    private static string FormatExitCode(int exitCode)
    {
        if (exitCode >= 0)
        {
            return exitCode.ToString(CultureInfo.InvariantCulture);
        }

        var hexCode = unchecked((uint)exitCode);
        return string.Create(CultureInfo.InvariantCulture, $"{exitCode}（0x{hexCode:X8}）");
    }

    private static string? DescribeKnownExitCode(RepairCommand command, int exitCode)
    {
        if (!IsWingetCommand(command))
        {
            return null;
        }

        return exitCode switch
        {
            WingetSourceDataMissingExitCode => "winget 源数据缺失或索引未就绪，无法读取安装包清单。",
            WingetSourceNameNotFoundExitCode => "winget 未找到指定源名称，源可能已被重置或注册信息缺失。",
            WingetSourcesInvalidExitCode => "winget 源配置无效，需要更新或重置源。",
            WingetSourceDataIntegrityFailureExitCode => "winget 源数据完整性校验失败，需要重新更新源。",
            WingetSourceOpenFailedExitCode => "winget 无法打开包源，可能是源索引损坏或暂时不可用。",
            WingetFailedToOpenAllSourcesExitCode => "winget 无法打开所有包源，需要更新源或检查网络。",
            WingetDownloadFailedExitCode => "winget 下载安装包失败，请检查网络、代理或下载源。",
            WingetCommandRequiresAdminExitCode => "winget 命令需要管理员权限。",
            WingetPackageAgreementsNotAcceptedExitCode => "winget 包协议未接受。",
            WingetSourceAgreementsNotAcceptedExitCode => "winget 源协议未接受。",
            WingetPackageAlreadyInstalledExitCode => "目标包已安装。",
            WingetInstallCancelledByUserExitCode => "安装已被用户取消。",
            _ => null,
        };
    }

    private static bool IsNetworkException(Exception exception)
    {
        if (exception is HttpRequestException or SocketException)
        {
            return true;
        }

        if (
            exception.InnerException is not null
            && !ReferenceEquals(exception.InnerException, exception)
        )
        {
            return IsNetworkException(exception.InnerException);
        }

        var message = exception.Message;
        return ContainsAnyNetworkToken(message);
    }

    private static bool IsNpmOnlineInstallCommand(RepairCommand command)
    {
        if (
            !string.Equals(command.FileName, "cmd.exe", StringComparison.OrdinalIgnoreCase)
            || command.Arguments.Count < 6
            || !command.Arguments.Any(argument =>
                string.Equals(argument, "npm", StringComparison.OrdinalIgnoreCase)
            )
            || !command.Arguments.Any(argument =>
                string.Equals(argument, "install", StringComparison.OrdinalIgnoreCase)
            )
            || command.Arguments.Any(argument =>
                string.Equals(argument, "--offline", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return false;
        }

        return command.Arguments.Any(argument =>
            argument.StartsWith("@openai/codex@", StringComparison.OrdinalIgnoreCase)
        );
    }

    private static bool IsNpmNetworkFailureOutput(params string[] values)
    {
        return values.Any(ContainsAnyNetworkToken);
    }

    private static bool ContainsAnyNetworkToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.ToLowerInvariant();
        return text.Contains("enotfound", StringComparison.Ordinal)
            || text.Contains("eai_again", StringComparison.Ordinal)
            || text.Contains("econnreset", StringComparison.Ordinal)
            || text.Contains("etimedout", StringComparison.Ordinal)
            || text.Contains("network", StringComparison.Ordinal)
            || text.Contains("socket", StringComparison.Ordinal)
            || text.Contains("registry.npmjs.org", StringComparison.Ordinal)
            || text.Contains("nodejs.org", StringComparison.Ordinal)
            || text.Contains("name resolution", StringComparison.Ordinal)
            || text.Contains("dns", StringComparison.Ordinal);
    }

    private static RepairCommand CreateWingetSourceUpdateCommand() =>
        new("winget", ["source", "update", "--name", "winget", "--disable-interactivity"]);

    private static RepairCommand CreateWingetSourceResetCommand() =>
        new(
            "winget",
            ["source", "reset", "--name", "winget", "--force", "--disable-interactivity"]
        );

    private static bool ShouldRetryAfterWingetSourceRepair(RepairCommand command, int exitCode) =>
        IsWingetInstallCommand(command) && IsWingetSourceRepairableExitCode(exitCode);

    private static bool IsWingetSourceRepairableExitCode(int exitCode) =>
        exitCode
            is WingetSourcesInvalidExitCode
                or WingetSourceDataMissingExitCode
                or WingetSourceNameNotFoundExitCode
                or WingetSourceDataIntegrityFailureExitCode
                or WingetSourceOpenFailedExitCode
                or WingetFailedToOpenAllSourcesExitCode;

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

    private static string? FirstMeaningfulOutputLine(params string[] values)
    {
        return values
            .SelectMany(SplitOutputLines)
            .FirstOrDefault(IsMeaningfulOutputLine);
    }

    private static IEnumerable<string> SplitOutputLines(string value)
    {
        return value.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
    }

    private static bool IsMeaningfulOutputLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed is "/" or "-" or "\\" or "|")
        {
            return false;
        }

        return !IsProgressOnlyOutputLine(trimmed);
    }

    private static bool IsProgressOnlyOutputLine(string line)
    {
        if (!line.Any(IsProgressBarCharacter))
        {
            return false;
        }

        foreach (var character in line)
        {
            if (
                char.IsDigit(character)
                || char.IsWhiteSpace(character)
                || IsProgressBarCharacter(character)
                || character is '%' or '.' or '/' or 'K' or 'k' or 'M' or 'm' or 'G' or 'g' or 'B' or 'b'
            )
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsProgressBarCharacter(char character) =>
        character is '█' or '▒' or '▓' or '░';

    private static bool IsWingetCommand(RepairCommand command) =>
        string.Equals(command.FileName, "winget", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command.FileName, "winget.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsWingetInstallCommand(RepairCommand command) =>
        IsWingetCommand(command)
        && command.Arguments.Any(argument =>
            string.Equals(argument, "install", StringComparison.OrdinalIgnoreCase)
        );

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

        return $"已{string.Join("，", changes)}。CodexCliPlus 会立即读取更新后的 PATH；已打开的外部终端需重新打开。";
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

    private sealed record RepairCommand(
        string FileName,
        IReadOnlyList<string> Arguments,
        IReadOnlyCollection<int>? SuccessfulExitCodes = null
    );

    private sealed record AllowedPathDirectory(string Path, bool CreateIfMissing = false);

    private sealed record UserPathCleanupResult(
        IReadOnlyList<string> Entries,
        int DuplicateEntriesRemoved,
        int UnreachableEntriesRemoved
    );

    private sealed record NodeLtsInstallPlan(
        string Version,
        string Architecture,
        Uri DownloadUri,
        string CacheDirectory,
        string InstallerPath
    );

    private sealed class NodeReleaseIndexEntry
    {
        public string? Version { get; init; }

        public JsonElement Lts { get; init; }

        public string[]? Files { get; init; }
    }
}
