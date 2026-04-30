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
            userPathReader
            ?? (target => Environment.GetEnvironmentVariable("Path", target));
        _userPathWriter =
            userPathWriter
            ?? (value => Environment.SetEnvironmentVariable("Path", value, EnvironmentVariableTarget.User));
        _environmentChangeBroadcaster = environmentChangeBroadcaster ?? BroadcastEnvironmentChange;
    }

    public async Task<LocalDependencyRepairResult> RunElevatedRepairAsync(
        string actionId,
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
        var executablePath = _currentProcessPathResolver();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return Failure(actionId, "无法定位桌面程序。", "当前进程路径不可用，无法进入提权修复模式。");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments =
                $"--repair {QuoteArgument(actionId)} --status {QuoteArgument(statusPath)}",
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

            return await WaitForRepairStatusAsync(process, actionId, statusPath, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(actionId, "修复进程等待超时。", "提权修复进程未在预期时间内返回状态。");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ElevationCancelledErrorCode)
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
                                "install --id OpenJS.NodeJS.LTS -e --source winget --accept-package-agreements --accept-source-agreements"
                            ),
                        ],
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.InstallPowerShell => await RunCommandRepairAsync(
                        actionId,
                        "安装 PowerShell 7",
                        [
                            new RepairCommand(
                                "winget",
                                "install --id Microsoft.PowerShell -e --source winget --accept-package-agreements --accept-source-agreements"
                            ),
                        ],
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.InstallWsl => await RunCommandRepairAsync(
                        actionId,
                        "安装 WSL",
                        [new RepairCommand("wsl.exe", "--install")],
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.UpdateWsl => await RunCommandRepairAsync(
                        actionId,
                        "更新 WSL",
                        [new RepairCommand("wsl.exe", "--update")],
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.InstallCodexCli => await RunCommandRepairAsync(
                        actionId,
                        "安装 Codex CLI",
                        [new RepairCommand("cmd.exe", "/d /c npm install -g @openai/codex")],
                        cancellationToken
                    ),
                    LocalDependencyRepairActionIds.RepairUserPath => await RepairUserPathAsync(
                        actionId,
                        cancellationToken
                    ),
                    _ => Failure(actionId, "未知修复动作。", "修复模式拒绝执行非白名单动作。"),
                };
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError($"Local dependency repair '{actionId}' failed.", exception);
            result = Failure(actionId, "修复执行失败。", exception.Message);
        }

        await WriteStatusAsync(statusPath, result, cancellationToken);
        return result;
    }

    private async Task<LocalDependencyRepairResult> RunCommandRepairAsync(
        string actionId,
        string summary,
        IReadOnlyList<RepairCommand> commands,
        CancellationToken cancellationToken
    )
    {
        var logPath = GetRepairLogPath();
        foreach (var command in commands)
        {
            await AppendRepairLogAsync(
                logPath,
                $"$ {command.FileName} {command.Arguments}",
                cancellationToken
            );
            var result = await _processRunner.RunAsync(
                command.FileName,
                command.Arguments,
                RepairCommandTimeout,
                cancellationToken
            );
            await AppendRepairLogAsync(
                logPath,
                BuildCommandLog(command, result),
                cancellationToken
            );

            if (result.ExitCode != 0)
            {
                return new LocalDependencyRepairResult
                {
                    ActionId = actionId,
                    Succeeded = false,
                    ExitCode = result.ExitCode,
                    Summary = $"{summary}失败。",
                    Detail = FirstNonEmptyLine(result.StandardError, result.StandardOutput)
                        ?? "命令返回非零退出码。",
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
        CancellationToken cancellationToken
    )
    {
        var logPath = GetRepairLogPath();
        var userPath = _userPathReader(EnvironmentVariableTarget.User) ?? string.Empty;
        var existingEntries = SplitPath(userPath).ToList();
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

        if (additions.Length == 0)
        {
            return new LocalDependencyRepairResult
            {
                ActionId = actionId,
                Succeeded = true,
                ExitCode = 0,
                Summary = "用户 PATH 无需修复。",
                Detail = "没有发现需要补齐的安全目录。",
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
                $"Added {additions.Length} safe user PATH entries."
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
            Detail = "已补齐安全目录。新终端和重启后的 CodexCliPlus 会读取更新后的 PATH。",
            LogPath = logPath,
        };
    }

    private async Task<LocalDependencyRepairResult> WaitForRepairStatusAsync(
        Process process,
        string actionId,
        string statusPath,
        CancellationToken cancellationToken
    )
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RepairProcessTimeout);

        while (!process.HasExited)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            if (TryReadStatus(statusPath, out var interimResult))
            {
                return interimResult;
            }

            await Task.Delay(RepairProcessPollInterval, timeoutCts.Token);
        }

        await process.WaitForExitAsync(cancellationToken);
        if (TryReadStatus(statusPath, out var result))
        {
            return result;
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

    private static bool TryReadStatus(string statusPath, out LocalDependencyRepairResult result)
    {
        result = new LocalDependencyRepairResult();
        if (!File.Exists(statusPath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(statusPath, Encoding.UTF8);
            var parsed = JsonSerializer.Deserialize<LocalDependencyRepairResult>(json, JsonOptions);
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
        LocalDependencyRepairResult result,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statusPath)!);
        await File.WriteAllTextAsync(
            statusPath,
            JsonSerializer.Serialize(result, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken
        );
    }

    private string GetRepairLogPath()
    {
        Directory.CreateDirectory(_pathService.Directories.LogsDirectory);
        return Path.Combine(_pathService.Directories.LogsDirectory, "local-environment-repair.log");
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
            $"Command '{command.FileName} {command.Arguments}' exited {result.ExitCode}.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}"
        );
    }

    private static LocalDependencyRepairResult Failure(
        string actionId,
        string summary,
        string detail
    )
    {
        return new LocalDependencyRepairResult
        {
            ActionId = actionId,
            Succeeded = false,
            Summary = summary,
            Detail = detail,
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

    private sealed record RepairCommand(string FileName, string Arguments);

    private sealed record AllowedPathDirectory(string Path, bool CreateIfMissing = false);
}
