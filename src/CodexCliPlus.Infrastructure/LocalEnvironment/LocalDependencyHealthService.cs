using System.Globalization;
using CodexCliPlus.Core.Abstractions.LocalEnvironment;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models.LocalEnvironment;

namespace CodexCliPlus.Infrastructure.LocalEnvironment;

public sealed class LocalDependencyHealthService
{
    private static readonly TimeSpan ShortCommandTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan LoginCommandTimeout = TimeSpan.FromSeconds(6);

    private readonly ILocalEnvironmentProcessRunner _processRunner;
    private readonly Func<string, string?> _environmentVariableProvider;
    private readonly Func<EnvironmentVariableTarget, string?> _pathProvider;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<DateTimeOffset> _clock;

    public LocalDependencyHealthService(
        ILocalEnvironmentProcessRunner processRunner,
        Func<string, string?>? environmentVariableProvider = null,
        Func<EnvironmentVariableTarget, string?>? pathProvider = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null,
        Func<DateTimeOffset>? clock = null
    )
    {
        _processRunner = processRunner;
        _environmentVariableProvider =
            environmentVariableProvider ?? Environment.GetEnvironmentVariable;
        _pathProvider = pathProvider ?? (target => Environment.GetEnvironmentVariable("PATH", target));
        _fileExists = fileExists ?? File.Exists;
        _directoryExists = directoryExists ?? Directory.Exists;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public async Task<LocalDependencySnapshot> CheckAsync(
        CancellationToken cancellationToken = default
    )
    {
        var toolState = new ToolState();
        var items = new List<LocalDependencyItem>
        {
            await CheckNodeAsync(toolState, cancellationToken),
            await CheckNpmAsync(toolState, cancellationToken),
            await CheckCodexCliAsync(toolState, cancellationToken),
            await CheckPowerShellAsync(toolState, cancellationToken),
        };

        items.Add(CheckPath(toolState));
        items.Add(await CheckWslAsync(toolState, cancellationToken));
        items.Add(await CheckWingetAsync(toolState, cancellationToken));

        var readinessScore = CalculateReadinessScore(items);
        return new LocalDependencySnapshot
        {
            CheckedAt = _clock(),
            ReadinessScore = readinessScore,
            Summary = BuildSummary(items, readinessScore),
            Items = items,
            RepairCapabilities = BuildRepairCapabilities(items, toolState),
        };
    }

    private async Task<LocalDependencyItem> CheckCodexCliAsync(
        ToolState toolState,
        CancellationToken cancellationToken
    )
    {
        var pathCandidates = await FindExecutablePathsAsync("codex", cancellationToken);
        var appDataNpmPath = GetAppDataNpmPath();
        var fallbackPath = string.IsNullOrWhiteSpace(appDataNpmPath)
            ? null
            : System.IO.Path.Combine(appDataNpmPath, "codex.cmd");
        if (!string.IsNullOrWhiteSpace(fallbackPath) && _fileExists(fallbackPath))
        {
            pathCandidates.Add(fallbackPath);
        }

        var codexPath = PickExecutablePath(pathCandidates);
        toolState.CodexPath = codexPath;

        if (string.IsNullOrWhiteSpace(codexPath))
        {
            return new LocalDependencyItem
            {
                Id = "codex-cli",
                Name = "Codex CLI",
                Status = LocalDependencyStatus.Missing,
                Severity = LocalDependencySeverity.Required,
                Detail = "未在 PATH 或 npm 用户全局目录中找到 codex 命令。",
                Recommendation = "先确认 Node.js 和 npm 可用，再全局安装 @openai/codex。",
                RepairActionId = toolState.NpmUsable
                    ? LocalDependencyRepairActionIds.InstallCodexCli
                    : LocalDependencyRepairActionIds.InstallNodeNpm,
            };
        }

        var versionAttempt = await TryRunExecutableAsync(
            codexPath,
            "--version",
            ShortCommandTimeout,
            cancellationToken
        );
        var loginAttempt = await TryRunExecutableAsync(
            codexPath,
            "login status",
            LoginCommandTimeout,
            cancellationToken
        );
        var authProbe = ProbeCodexAuthFiles();
        var version = FirstOutputLine(versionAttempt.Output);
        var canReadVersion = versionAttempt.Succeeded && !string.IsNullOrWhiteSpace(version);
        var loginReady = loginAttempt.Succeeded;
        var hasAuthFiles = authProbe.HasAuthFile || authProbe.HasConfigFile;

        if (canReadVersion && (loginReady || hasAuthFiles))
        {
            return new LocalDependencyItem
            {
                Id = "codex-cli",
                Name = "Codex CLI",
                Status = LocalDependencyStatus.Ready,
                Severity = LocalDependencySeverity.Required,
                Version = version,
                Path = codexPath,
                Detail = loginReady ? "命令和登录状态检测通过。" : "命令可用，并检测到本地授权文件。",
                Recommendation = "无需处理。",
            };
        }

        return new LocalDependencyItem
        {
            Id = "codex-cli",
            Name = "Codex CLI",
            Status = canReadVersion ? LocalDependencyStatus.Warning : LocalDependencyStatus.Error,
            Severity = LocalDependencySeverity.Required,
            Version = version,
            Path = codexPath,
            Detail = canReadVersion
                ? "Codex CLI 可执行，但登录状态未通过本地检测。"
                : BuildFailureDetail("Codex CLI 版本检测失败", versionAttempt),
            Recommendation = canReadVersion
                ? "打开终端运行 codex login，或确认 Codex 授权文件仍然有效。"
                : "重新安装 @openai/codex，或检查 npm 用户全局目录是否在 PATH 中。",
            RepairActionId = LocalDependencyRepairActionIds.InstallCodexCli,
        };
    }

    private async Task<LocalDependencyItem> CheckNodeAsync(
        ToolState toolState,
        CancellationToken cancellationToken
    )
    {
        var paths = await FindExecutablePathsAsync("node", cancellationToken);
        var nodePath = PickExecutablePath(paths);
        toolState.NodePath = nodePath;

        if (string.IsNullOrWhiteSpace(nodePath))
        {
            return new LocalDependencyItem
            {
                Id = "node",
                Name = "Node.js",
                Status = LocalDependencyStatus.Missing,
                Severity = LocalDependencySeverity.Required,
                Detail = "未找到 node 命令。",
                Recommendation = "安装 Node.js LTS，安装后重新打开 CodexCliPlus。",
                RepairActionId = LocalDependencyRepairActionIds.InstallNodeNpm,
            };
        }

        var attempt = await TryRunExecutableAsync(
            nodePath,
            "--version",
            ShortCommandTimeout,
            cancellationToken
        );
        var version = FirstOutputLine(attempt.Output);
        if (attempt.Succeeded && !string.IsNullOrWhiteSpace(version))
        {
            return new LocalDependencyItem
            {
                Id = "node",
                Name = "Node.js",
                Status = LocalDependencyStatus.Ready,
                Severity = LocalDependencySeverity.Required,
                Version = version,
                Path = nodePath,
                Detail = "Node.js 命令检测通过。",
                Recommendation = "无需处理。",
            };
        }

        return new LocalDependencyItem
        {
            Id = "node",
            Name = "Node.js",
            Status = attempt.TimedOut ? LocalDependencyStatus.Warning : LocalDependencyStatus.Error,
            Severity = LocalDependencySeverity.Required,
            Path = nodePath,
            Detail = BuildFailureDetail("Node.js 版本检测失败", attempt),
            Recommendation = "重新安装 Node.js LTS，或检查 node.exe 是否可正常启动。",
            RepairActionId = LocalDependencyRepairActionIds.InstallNodeNpm,
        };
    }

    private async Task<LocalDependencyItem> CheckNpmAsync(
        ToolState toolState,
        CancellationToken cancellationToken
    )
    {
        var paths = await FindExecutablePathsAsync("npm", cancellationToken);
        var npmPath = PickExecutablePath(paths);
        toolState.NpmPath = npmPath;

        if (string.IsNullOrWhiteSpace(npmPath))
        {
            return new LocalDependencyItem
            {
                Id = "npm",
                Name = "npm",
                Status = LocalDependencyStatus.Missing,
                Severity = LocalDependencySeverity.Required,
                Detail = "未找到 npm 命令。",
                Recommendation = "安装 Node.js LTS 会同时安装 npm。",
                RepairActionId = LocalDependencyRepairActionIds.InstallNodeNpm,
            };
        }

        var versionAttempt = await TryRunExecutableAsync(
            npmPath,
            "--version",
            ShortCommandTimeout,
            cancellationToken
        );
        var prefixAttempt = await TryRunExecutableAsync(
            npmPath,
            "config get prefix",
            ShortCommandTimeout,
            cancellationToken
        );
        var version = FirstOutputLine(versionAttempt.Output);
        var prefix = FirstOutputLine(prefixAttempt.Output);
        toolState.NpmPrefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix;

        if (versionAttempt.Succeeded && !string.IsNullOrWhiteSpace(version))
        {
            toolState.NpmUsable = true;
            var appDataNpm = GetAppDataNpmPath();
            var pathHasAppDataNpm =
                string.IsNullOrWhiteSpace(appDataNpm) || IsDirectoryOnAnyPath(appDataNpm);
            return new LocalDependencyItem
            {
                Id = "npm",
                Name = "npm",
                Status = pathHasAppDataNpm
                    ? LocalDependencyStatus.Ready
                    : LocalDependencyStatus.Warning,
                Severity = LocalDependencySeverity.Required,
                Version = version,
                Path = npmPath,
                Detail = pathHasAppDataNpm
                    ? BuildNpmDetail(prefix)
                    : "npm 可用，但 npm 用户全局目录未出现在当前 PATH 中。",
                Recommendation = pathHasAppDataNpm
                    ? "无需处理。"
                    : "将 %APPDATA%\\npm 补充到用户 PATH，或重启桌面会话后重新检测。",
                RepairActionId = pathHasAppDataNpm
                    ? null
                    : LocalDependencyRepairActionIds.RepairUserPath,
            };
        }

        return new LocalDependencyItem
        {
            Id = "npm",
            Name = "npm",
            Status = versionAttempt.TimedOut
                ? LocalDependencyStatus.Warning
                : LocalDependencyStatus.Error,
            Severity = LocalDependencySeverity.Required,
            Path = npmPath,
            Detail = BuildFailureDetail("npm 版本检测失败", versionAttempt),
            Recommendation = "重新安装 Node.js LTS，或修复 npm 用户全局配置。",
            RepairActionId = LocalDependencyRepairActionIds.InstallNodeNpm,
        };
    }

    private async Task<LocalDependencyItem> CheckPowerShellAsync(
        ToolState toolState,
        CancellationToken cancellationToken
    )
    {
        var pwshPath = PickExecutablePath(await FindExecutablePathsAsync("pwsh", cancellationToken));
        var powershellPath = PickExecutablePath(
            await FindExecutablePathsAsync("powershell", cancellationToken)
        );
        var shellPath = string.IsNullOrWhiteSpace(pwshPath) ? powershellPath : pwshPath;
        toolState.PowerShellPath = shellPath;

        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return new LocalDependencyItem
            {
                Id = "powershell",
                Name = "PowerShell",
                Status = LocalDependencyStatus.Missing,
                Severity = LocalDependencySeverity.Required,
                Detail = "未找到 pwsh.exe 或 Windows PowerShell。",
                Recommendation = "安装 PowerShell 7，或修复 Windows PowerShell 5.1。",
                RepairActionId = LocalDependencyRepairActionIds.InstallPowerShell,
            };
        }

        var attempt = await TryRunExecutableAsync(
            shellPath,
            "-NoLogo -NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"",
            ShortCommandTimeout,
            cancellationToken
        );
        var version = FirstOutputLine(attempt.Output);
        if (attempt.Succeeded && !string.IsNullOrWhiteSpace(version))
        {
            return new LocalDependencyItem
            {
                Id = "powershell",
                Name = "PowerShell",
                Status = LocalDependencyStatus.Ready,
                Severity = LocalDependencySeverity.Required,
                Version = version,
                Path = shellPath,
                Detail = string.IsNullOrWhiteSpace(pwshPath)
                    ? "已使用 Windows PowerShell 兼容模式。"
                    : "PowerShell 7 检测通过。",
                Recommendation = "无需处理。",
            };
        }

        return new LocalDependencyItem
        {
            Id = "powershell",
            Name = "PowerShell",
            Status = attempt.TimedOut ? LocalDependencyStatus.Warning : LocalDependencyStatus.Error,
            Severity = LocalDependencySeverity.Required,
            Path = shellPath,
            Detail = BuildFailureDetail("PowerShell 版本检测失败", attempt),
            Recommendation = "安装 PowerShell 7，或检查当前 PowerShell 可执行文件。",
            RepairActionId = LocalDependencyRepairActionIds.InstallPowerShell,
        };
    }

    private LocalDependencyItem CheckPath(ToolState toolState)
    {
        var processPath = _environmentVariableProvider("PATH") ?? string.Empty;
        var userPath = _pathProvider(EnvironmentVariableTarget.User) ?? string.Empty;
        var machinePath = _pathProvider(EnvironmentVariableTarget.Machine) ?? string.Empty;
        var processEntries = SplitPath(processPath);
        var userEntries = SplitPath(userPath);
        var machineEntries = SplitPath(machinePath);
        var allEntries = processEntries.Concat(userEntries).Concat(machineEntries).ToArray();

        if (allEntries.Length == 0)
        {
            return new LocalDependencyItem
            {
                Id = "path",
                Name = "PATH",
                Status = LocalDependencyStatus.Error,
                Severity = LocalDependencySeverity.Required,
                Detail = "当前进程、用户和系统 PATH 均为空。",
                Recommendation = "恢复系统 PATH 后重新打开 CodexCliPlus。",
            };
        }

        var duplicateCount =
            CountDuplicatePathEntries(processEntries)
            + CountDuplicatePathEntries(userEntries)
            + CountDuplicatePathEntries(machineEntries);
        var unreachableCount = CountUnreachablePathEntries(
            allEntries.DistinctBy(NormalizePathForComparison, StringComparer.OrdinalIgnoreCase)
        );
        var missingSafeDirectories = FindMissingSafePathDirectories(toolState, processEntries);
        var userMachineEntries = userEntries.Concat(machineEntries).ToArray();
        var repairableMissingDirectories = missingSafeDirectories
            .Where(item => !userMachineEntries.Any(entry => IsSameDirectory(entry, item.Path)))
            .ToList();
        toolState.MissingSafePathDirectories = repairableMissingDirectories
            .Select(item => item.Path)
            .ToArray();

        var issues = new List<string>();
        if (missingSafeDirectories.Count > 0)
        {
            issues.Add(
                "缺少 "
                    + string.Join(
                        "、",
                        missingSafeDirectories.Select(item => item.DisplayName)
                    )
            );
        }

        if (duplicateCount > 0)
        {
            issues.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"发现 {duplicateCount} 个重复目录"
                )
            );
        }

        if (unreachableCount > 0)
        {
            issues.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"发现 {unreachableCount} 个不可达目录"
                )
            );
        }

        if (issues.Count == 0)
        {
            return new LocalDependencyItem
            {
                Id = "path",
                Name = "PATH",
                Status = LocalDependencyStatus.Ready,
                Severity = LocalDependencySeverity.Required,
                Detail = "关键命令目录已出现在 PATH 中。",
                Recommendation = "无需处理。",
            };
        }

        return new LocalDependencyItem
        {
            Id = "path",
            Name = "PATH",
            Status = LocalDependencyStatus.Warning,
            Severity = LocalDependencySeverity.Required,
            Detail = string.Join("；", issues) + "。",
            Recommendation = missingSafeDirectories.Count > 0
                ? repairableMissingDirectories.Count > 0
                    ? "可使用内置修复补齐安全的用户 PATH 目录。"
                    : "用户或系统 PATH 已包含关键目录，重启 CodexCliPlus 后重新检测。"
                : "清理重复或失效目录后重新检测。",
            RepairActionId = repairableMissingDirectories.Count > 0
                ? LocalDependencyRepairActionIds.RepairUserPath
                : null,
        };
    }

    private async Task<LocalDependencyItem> CheckWslAsync(
        ToolState toolState,
        CancellationToken cancellationToken
    )
    {
        var wslPath = PickExecutablePath(await FindExecutablePathsAsync("wsl", cancellationToken));
        toolState.WslPath = wslPath;
        if (string.IsNullOrWhiteSpace(wslPath))
        {
            return new LocalDependencyItem
            {
                Id = "wsl",
                Name = "WSL",
                Status = LocalDependencyStatus.OptionalUnavailable,
                Severity = LocalDependencySeverity.Optional,
                Detail = "未找到 wsl.exe；WSL 仅影响可选 Linux 工作流。",
                Recommendation = "需要 Linux 子系统时再安装 WSL。",
                RepairActionId = LocalDependencyRepairActionIds.InstallWsl,
            };
        }

        var statusAttempt = await TryRunExecutableAsync(
            wslPath,
            "--status",
            ShortCommandTimeout,
            cancellationToken
        );
        var listAttempt = await TryRunExecutableAsync(
            wslPath,
            "-l -q",
            ShortCommandTimeout,
            cancellationToken
        );
        var distros = CountNonEmptyLines(listAttempt.Output);

        if (statusAttempt.Succeeded && distros > 0)
        {
            return new LocalDependencyItem
            {
                Id = "wsl",
                Name = "WSL",
                Status = LocalDependencyStatus.Ready,
                Severity = LocalDependencySeverity.Optional,
                Version = FirstOutputLine(statusAttempt.Output),
                Path = wslPath,
                Detail = string.Create(CultureInfo.InvariantCulture, $"已检测到 {distros} 个 WSL 发行版。"),
                Recommendation = "无需处理。",
            };
        }

        return new LocalDependencyItem
        {
            Id = "wsl",
            Name = "WSL",
            Status = LocalDependencyStatus.OptionalUnavailable,
            Severity = LocalDependencySeverity.Optional,
            Path = wslPath,
            Detail = statusAttempt.Succeeded
                ? "WSL 命令可用，但未检测到已安装发行版。"
                : BuildFailureDetail("WSL 状态检测未通过", statusAttempt),
            Recommendation = "需要 Linux 子系统时安装或更新 WSL。",
            RepairActionId = statusAttempt.Succeeded
                ? LocalDependencyRepairActionIds.InstallWsl
                : LocalDependencyRepairActionIds.UpdateWsl,
        };
    }

    private async Task<LocalDependencyItem> CheckWingetAsync(
        ToolState toolState,
        CancellationToken cancellationToken
    )
    {
        var wingetPath = PickExecutablePath(await FindExecutablePathsAsync("winget", cancellationToken));
        toolState.WingetPath = wingetPath;
        if (string.IsNullOrWhiteSpace(wingetPath))
        {
            return new LocalDependencyItem
            {
                Id = "winget",
                Name = "winget",
                Status = LocalDependencyStatus.Warning,
                Severity = LocalDependencySeverity.RepairTool,
                Detail = "未找到 winget；安装类修复能力不可用。",
                Recommendation = "如需一键安装 Node.js 或 PowerShell，请先安装 App Installer/winget。",
            };
        }

        var attempt = await TryRunExecutableAsync(
            wingetPath,
            "--version",
            ShortCommandTimeout,
            cancellationToken
        );
        var version = FirstOutputLine(attempt.Output);
        if (attempt.Succeeded && !string.IsNullOrWhiteSpace(version))
        {
            return new LocalDependencyItem
            {
                Id = "winget",
                Name = "winget",
                Status = LocalDependencyStatus.Ready,
                Severity = LocalDependencySeverity.RepairTool,
                Version = version,
                Path = wingetPath,
                Detail = "winget 修复能力检测通过。",
                Recommendation = "无需处理。",
            };
        }

        return new LocalDependencyItem
        {
            Id = "winget",
            Name = "winget",
            Status = LocalDependencyStatus.Warning,
            Severity = LocalDependencySeverity.RepairTool,
            Path = wingetPath,
            Detail = BuildFailureDetail("winget 版本检测失败", attempt),
            Recommendation = "修复 winget 后可使用内置安装动作。",
        };
    }

    private async Task<List<string>> FindExecutablePathsAsync(
        string commandName,
        CancellationToken cancellationToken
    )
    {
        var attempt = await TryRunAsync(
            "where.exe",
            commandName,
            ShortCommandTimeout,
            cancellationToken
        );
        if (!attempt.Succeeded)
        {
            return [];
        }

        return SplitLines(attempt.Output)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<CommandAttempt> TryRunExecutableAsync(
        string executablePath,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        if (IsCommandScript(executablePath))
        {
            return await TryRunAsync(
                "cmd.exe",
                $"/d /c \"\"{executablePath}\" {arguments}\"",
                timeout,
                cancellationToken
            );
        }

        return await TryRunAsync(executablePath, arguments, timeout, cancellationToken);
    }

    private async Task<CommandAttempt> TryRunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var result = await _processRunner.RunAsync(
                fileName,
                arguments,
                timeout,
                cancellationToken
            );
            return new CommandAttempt(
                result.ExitCode,
                CombineOutput(result.StandardOutput, result.StandardError),
                null,
                TimedOut: false
            );
        }
        catch (TimeoutException exception)
        {
            return new CommandAttempt(null, string.Empty, exception.Message, TimedOut: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new CommandAttempt(null, string.Empty, exception.Message, TimedOut: false);
        }
    }

    private CodexAuthProbe ProbeCodexAuthFiles()
    {
        var homes = ResolveCodexHomeCandidates();
        var hasConfig = false;
        var hasAuth = false;

        foreach (var home in homes)
        {
            hasConfig |= _fileExists(System.IO.Path.Combine(home, "config.toml"));
            hasAuth |= _fileExists(System.IO.Path.Combine(home, "auth.json"));
        }

        return new CodexAuthProbe(hasConfig, hasAuth);
    }

    private List<string> ResolveCodexHomeCandidates()
    {
        var candidates = new List<string>();
        var codexHome = _environmentVariableProvider("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
        {
            AddCandidate(candidates, codexHome);
            AddCandidate(candidates, System.IO.Path.Combine(codexHome, ".codex"));
        }

        var userProfile = _environmentVariableProvider("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            AddCandidate(candidates, System.IO.Path.Combine(userProfile, ".codex"));
        }

        return candidates;
    }

    private string? GetAppDataNpmPath()
    {
        var appData = _environmentVariableProvider("APPDATA");
        return string.IsNullOrWhiteSpace(appData)
            ? null
            : System.IO.Path.Combine(appData, "npm");
    }

    private bool IsDirectoryOnAnyPath(string directory)
    {
        var allEntries = SplitPath(_environmentVariableProvider("PATH") ?? string.Empty)
            .Concat(SplitPath(_pathProvider(EnvironmentVariableTarget.User) ?? string.Empty))
            .Concat(SplitPath(_pathProvider(EnvironmentVariableTarget.Machine) ?? string.Empty));
        return allEntries.Any(entry => IsSameDirectory(entry, directory));
    }

    private List<PathRepairDirectory> FindMissingSafePathDirectories(
        ToolState toolState,
        IReadOnlyCollection<string> pathEntries
    )
    {
        var expected = new List<PathRepairDirectory>();
        var appDataNpmPath = GetAppDataNpmPath();
        if (!string.IsNullOrWhiteSpace(appDataNpmPath))
        {
            expected.Add(new PathRepairDirectory("%APPDATA%\\npm", appDataNpmPath));
        }

        AddExecutableDirectory(expected, "Node.js", toolState.NodePath);
        AddExecutableDirectory(expected, "npm", toolState.NpmPath);
        AddExecutableDirectory(expected, "Codex CLI", toolState.CodexPath);
        AddExecutableDirectory(expected, "PowerShell", toolState.PowerShellPath);

        return expected
            .Where(item => _directoryExists(item.Path) && !pathEntries.Any(entry => IsSameDirectory(entry, item.Path)))
            .DistinctBy(item => NormalizePathForComparison(item.Path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CountDuplicatePathEntries(IEnumerable<string> entries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = 0;
        foreach (var normalized in entries.Select(NormalizePathForComparison).Where(item => item.Length > 0))
        {
            if (!seen.Add(normalized))
            {
                duplicates++;
            }
        }

        return duplicates;
    }

    private int CountUnreachablePathEntries(IEnumerable<string> entries)
    {
        var count = 0;
        foreach (var entry in entries)
        {
            var expanded = Environment.ExpandEnvironmentVariables(entry.Trim());
            if (
                expanded.Length > 0
                && !expanded.Contains('%', StringComparison.Ordinal)
                && !_directoryExists(expanded)
            )
            {
                count++;
            }
        }

        return count;
    }

    private static int CalculateReadinessScore(IEnumerable<LocalDependencyItem> items)
    {
        var required = items
            .Where(item => item.Severity == LocalDependencySeverity.Required)
            .ToArray();
        if (required.Length == 0)
        {
            return 100;
        }

        var points = required.Sum(item =>
            item.Status switch
            {
                LocalDependencyStatus.Ready => 1.0,
                LocalDependencyStatus.Warning => 0.75,
                LocalDependencyStatus.Repairing => 0.5,
                _ => 0.0,
            }
        );
        return Math.Clamp(
            (int)Math.Round(points / required.Length * 100, MidpointRounding.AwayFromZero),
            0,
            100
        );
    }

    private static string BuildSummary(
        IReadOnlyCollection<LocalDependencyItem> items,
        int readinessScore
    )
    {
        var required = items
            .Where(item => item.Severity == LocalDependencySeverity.Required)
            .ToArray();
        var ready = required.Count(item => item.Status == LocalDependencyStatus.Ready);
        var missing = required.Count(item =>
            item.Status is LocalDependencyStatus.Missing or LocalDependencyStatus.Error
        );
        var optionalUnavailable = items.Count(item =>
            item.Status == LocalDependencyStatus.OptionalUnavailable
        );

        if (missing == 0 && readinessScore >= 95)
        {
            return optionalUnavailable == 0
                ? "本地 Codex 环境已就绪。"
                : "本地 Codex 必备环境已就绪，部分可选能力未启用。";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"必备环境 {ready}/{required.Length} 项就绪，评分 {readinessScore}%。"
        );
    }

    private static IReadOnlyList<LocalDependencyRepairCapability> BuildRepairCapabilities(
        IReadOnlyCollection<LocalDependencyItem> items,
        ToolState toolState
    )
    {
        var wingetReady = items.Any(item =>
            item.Id == "winget" && item.Status == LocalDependencyStatus.Ready
        );
        var npmReady = toolState.NpmUsable;
        var wslPresent = !string.IsNullOrWhiteSpace(toolState.WslPath);
        var pathRepairAvailable = toolState.MissingSafePathDirectories.Length > 0;

        return
        [
            new LocalDependencyRepairCapability
            {
                ActionId = LocalDependencyRepairActionIds.InstallNodeNpm,
                Name = "安装 Node.js LTS 和 npm",
                IsAvailable = wingetReady,
                Detail = wingetReady ? "将通过 winget 安装 OpenJS.NodeJS.LTS。" : "需要 winget 可用。",
            },
            new LocalDependencyRepairCapability
            {
                ActionId = LocalDependencyRepairActionIds.InstallPowerShell,
                Name = "安装 PowerShell 7",
                IsAvailable = wingetReady,
                Detail = wingetReady ? "将通过 winget 安装 Microsoft.PowerShell。" : "需要 winget 可用。",
            },
            new LocalDependencyRepairCapability
            {
                ActionId = LocalDependencyRepairActionIds.InstallCodexCli,
                Name = "安装 Codex CLI",
                IsAvailable = npmReady,
                Detail = npmReady ? "将通过 npm 全局安装 @openai/codex。" : "需要 npm 可用。",
            },
            new LocalDependencyRepairCapability
            {
                ActionId = LocalDependencyRepairActionIds.RepairUserPath,
                Name = "修复用户 PATH",
                IsAvailable = pathRepairAvailable,
                Detail = pathRepairAvailable ? "只补齐已确认缺失的安全目录。" : "没有需要补齐的安全目录。",
            },
            new LocalDependencyRepairCapability
            {
                ActionId = LocalDependencyRepairActionIds.InstallWsl,
                Name = "安装 WSL",
                IsAvailable = wslPresent,
                IsOptional = true,
                Detail = wslPresent ? "将调用 wsl.exe --install。" : "需要系统提供 wsl.exe。",
            },
            new LocalDependencyRepairCapability
            {
                ActionId = LocalDependencyRepairActionIds.UpdateWsl,
                Name = "更新 WSL",
                IsAvailable = wslPresent,
                IsOptional = true,
                Detail = wslPresent ? "将调用 wsl.exe --update。" : "需要系统提供 wsl.exe。",
            },
        ];
    }

    private static string BuildNpmDetail(string? prefix)
    {
        return string.IsNullOrWhiteSpace(prefix)
            ? "npm 命令检测通过。"
            : $"npm 命令检测通过，全局 prefix 已解析。";
    }

    private static string BuildFailureDetail(string prefix, CommandAttempt attempt)
    {
        if (attempt.TimedOut)
        {
            return $"{prefix}：命令超时。";
        }

        if (!string.IsNullOrWhiteSpace(attempt.Error))
        {
            return $"{prefix}：{attempt.Error}";
        }

        if (!string.IsNullOrWhiteSpace(attempt.Output))
        {
            return $"{prefix}：{FirstOutputLine(attempt.Output)}";
        }

        return attempt.ExitCode.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"{prefix}：退出码 {attempt.ExitCode.Value}。")
            : $"{prefix}。";
    }

    private static string PickExecutablePath(IReadOnlyList<string> candidates)
    {
        return candidates
            .Select(candidate => candidate.Trim())
            .FirstOrDefault(candidate => candidate.Length > 0) ?? string.Empty;
    }

    private static void AddCandidate(List<string> candidates, string path)
    {
        if (
            !string.IsNullOrWhiteSpace(path)
            && !candidates.Contains(path, StringComparer.OrdinalIgnoreCase)
        )
        {
            candidates.Add(path);
        }
    }

    private static void AddExecutableDirectory(
        List<PathRepairDirectory> expected,
        string displayName,
        string? executablePath
    )
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        var directory = System.IO.Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            expected.Add(new PathRepairDirectory(displayName, directory));
        }
    }

    private static bool IsCommandScript(string path)
    {
        return path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] SplitPath(string value)
    {
        return value
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim().Trim('"'))
            .Where(item => item.Length > 0)
            .ToArray();
    }

    private static string[] SplitLines(string value)
    {
        return value.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
    }

    private static string? FirstOutputLine(string value)
    {
        return SplitLines(value).FirstOrDefault();
    }

    private static int CountNonEmptyLines(string value)
    {
        return SplitLines(value).Length;
    }

    private static string CombineOutput(string standardOutput, string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return standardOutput.Trim();
        }

        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            return standardError.Trim();
        }

        return $"{standardOutput.Trim()}{Environment.NewLine}{standardError.Trim()}";
    }

    private static bool IsSameDirectory(string left, string right)
    {
        var normalizedLeft = NormalizePathForComparison(left);
        var normalizedRight = NormalizePathForComparison(right);
        return normalizedLeft.Length > 0
            && normalizedRight.Length > 0
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathForComparison(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path))
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
    }

    private sealed record CommandAttempt(
        int? ExitCode,
        string Output,
        string? Error,
        bool TimedOut
    )
    {
        public bool Succeeded => ExitCode == 0 && !TimedOut;
    }

    private sealed record CodexAuthProbe(bool HasConfigFile, bool HasAuthFile);

    private sealed record PathRepairDirectory(string DisplayName, string Path);

    private sealed class ToolState
    {
        public string? CodexPath { get; set; }

        public string? NodePath { get; set; }

        public string? NpmPath { get; set; }

        public string? NpmPrefix { get; set; }

        public bool NpmUsable { get; set; }

        public string? PowerShellPath { get; set; }

        public string? WslPath { get; set; }

        public string? WingetPath { get; set; }

        public string[] MissingSafePathDirectories { get; set; } = [];
    }
}
