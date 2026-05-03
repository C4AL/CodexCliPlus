using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexCliPlus.Core.Abstractions.LocalEnvironment;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models.LocalEnvironment;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Infrastructure.LocalEnvironment;

public sealed class LocalDependencyRepairService
{
    private const int ElevationCancelledErrorCode = 1223;
    private const int RecentOutputLineLimit = 20;
    private static readonly TimeSpan RepairCommandTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan RepairProcessPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RepairProcessTimeout = TimeSpan.FromMinutes(30);
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
        Action? environmentChangeBroadcaster = null
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

        await _pathService.EnsureCreatedAsync(cancellationToken);
        var statusPath = Path.Combine(
            _pathService.Directories.RuntimeDirectory,
            $"local-environment-repair-{Guid.NewGuid():N}.json"
        );
        var initialProgress = CreateProgress(
            actionId,
            "starting",
            "正在请求管理员权限。",
            logPath: GetRepairLogPath()
        );
        await WriteStatusAsync(statusPath, initialProgress, cancellationToken);
        progressReporter?.Invoke(initialProgress);

        var executablePath = _currentProcessPathResolver();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return Failure(
                actionId,
                "无法定位桌面程序。",
                "当前进程路径不可用，无法进入提权修复模式。"
            );
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

        try
        {
            _logger.Info($"Starting elevated local dependency repair '{actionId}'.");
            var process = _processStarter(startInfo);
            if (process is null)
            {
                return Failure(actionId, "修复进程未启动。", "系统未返回提权修复进程。");
            }

            return await WaitForRepairStatusAsync(
                process,
                actionId,
                statusPath,
                progressReporter,
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(actionId, "修复进程等待超时。", "提权修复进程未在预期时间内返回状态。");
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode == ElevationCancelledErrorCode)
        {
            return Failure(actionId, "用户取消了提权授权。", exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError($"Failed to start local dependency repair '{actionId}'.", exception);
            return Failure(actionId, "启动修复失败。", exception.Message);
        }
    }

    public async Task<LocalDependencyRepairResult> ExecuteRepairModeAsync(
        string actionId,
        string statusPath,
        CancellationToken cancellationToken = default
    )
    {
        await _pathService.EnsureCreatedAsync(cancellationToken);
        await WriteStatusAsync(
            statusPath,
            CreateProgress(
                actionId,
                "starting",
                "提权修复进程已启动。",
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
                        statusPath,
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
                        statusPath,
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.InstallWsl => await RunCommandRepairAsync(
                        actionId,
                        "安装 WSL",
                        [new RepairCommand("wsl.exe", ["--install"])],
                        statusPath,
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.UpdateWsl => await RunCommandRepairAsync(
                        actionId,
                        "更新 WSL",
                        [new RepairCommand("wsl.exe", ["--update"])],
                        statusPath,
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.InstallCodexCli => await RunCommandRepairAsync(
                        actionId,
                        "安装 Codex CLI",
                        [
                            new RepairCommand(
                                "cmd.exe",
                                ["/d", "/c", "npm", "install", "-g", "@openai/codex"]
                            ),
                        ],
                        statusPath,
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.RepairUserPath => await RepairUserPathAsync(
                        actionId,
                        statusPath,
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

        var previousProgress = TryReadStatus(statusPath, out var progress) ? progress : null;
        await WriteStatusAsync(
            statusPath,
            CreateCompletedProgress(result, previousProgress),
            cancellationToken
        );
        return result;
    }

    private async Task<LocalDependencyRepairResult> RunCommandRepairAsync(
        string actionId,
        string summary,
        IReadOnlyList<RepairCommand> commands,
        string statusPath,
        CancellationToken cancellationToken
    )
    {
        var logPath = GetRepairLogPath();
        await WriteStatusAsync(
            statusPath,
            CreateProgress(actionId, "running", $"{summary}准备开始。", logPath: logPath),
            cancellationToken
        );

        foreach (var command in commands)
        {
            var commandLine = BuildCommandLine(command);
            await AppendRepairLogAsync(
                logPath,
                $"$ {commandLine}",
                cancellationToken
            );
            await WriteStatusAsync(
                statusPath,
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
                result = await _processRunner.RunAsync(
                    command.FileName,
                    command.Arguments,
                    RepairCommandTimeout,
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

            await AppendRepairLogAsync(
                logPath,
                BuildCommandLog(command, result),
                cancellationToken
            );
            var recentOutput = ExtractRecentOutput(result.StandardOutput, result.StandardError);
            await WriteStatusAsync(
                statusPath,
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

            if (result.ExitCode != 0)
            {
                var detail =
                    FirstNonEmptyLine(result.StandardError, result.StandardOutput)
                    ?? "命令返回非零退出码。";
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
        string statusPath,
        CancellationToken cancellationToken
    )
    {
        var logPath = GetRepairLogPath();
        await WriteStatusAsync(
            statusPath,
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
            _userPathWriter(string.Join(System.IO.Path.PathSeparator, updatedEntries));
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

    private async Task<LocalDependencyRepairResult> WaitForRepairStatusAsync(
        Process process,
        string actionId,
        string statusPath,
        Action<LocalDependencyRepairProgress>? progressReporter,
        CancellationToken cancellationToken
    )
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RepairProcessTimeout);
        DateTimeOffset? lastProgressUpdate = null;

        while (!process.HasExited)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            if (TryReadStatus(statusPath, out var interimProgress))
            {
                if (lastProgressUpdate != interimProgress.UpdatedAt)
                {
                    lastProgressUpdate = interimProgress.UpdatedAt;
                    progressReporter?.Invoke(interimProgress);
                }

                if (interimProgress.IsCompleted)
                {
                    return CreateResult(interimProgress);
                }
            }

            await Task.Delay(RepairProcessPollInterval, timeoutCts.Token);
        }

        await process.WaitForExitAsync(cancellationToken);
        if (TryReadStatus(statusPath, out var result))
        {
            if (lastProgressUpdate != result.UpdatedAt)
            {
                progressReporter?.Invoke(result);
            }

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
        var directory = Path.GetDirectoryName(statusPath)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(
            directory,
            $"{Path.GetFileName(statusPath)}.{Guid.NewGuid():N}.tmp"
        );
        await File.WriteAllTextAsync(
            tempPath,
            JsonSerializer.Serialize(result, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken
        );
        File.Move(tempPath, statusPath, overwrite: true);
    }

    private string GetRepairLogPath()
    {
        Directory.CreateDirectory(_pathService.Directories.LogsDirectory);
        return Path.Combine(_pathService.Directories.LogsDirectory, "local-environment-repair.log");
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
            LogPath = progress.LogPath,
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

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
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

    private sealed record RepairCommand(string FileName, IReadOnlyList<string> Arguments);

    private sealed record AllowedPathDirectory(string Path, bool CreateIfMissing = false);

    private sealed record UserPathCleanupResult(
        IReadOnlyList<string> Entries,
        int DuplicateEntriesRemoved,
        int UnreachableEntriesRemoved
    );
}
