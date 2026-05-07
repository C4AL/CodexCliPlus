using System.Diagnostics;
using System.Text.Json;
using CodexCliPlus.Core.Abstractions.LocalEnvironment;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.LocalEnvironment;
using CodexCliPlus.Infrastructure.LocalEnvironment;

namespace CodexCliPlus.Tests.LocalEnvironment;

[Trait("Category", "LocalIntegration")]
public sealed class LocalDependencyRepairServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string[] RequiredEnvironmentLatestCodexCommandOrder =
    [
        "winget --version",
        "node --version",
        "cmd.exe /d /c npm --version",
        "winget install --id OpenJS.NodeJS.LTS -e --source winget --accept-package-agreements --accept-source-agreements",
        "node --version",
        "cmd.exe /d /c npm --version",
        "cmd.exe /d /c npm install -g @openai/codex@latest",
    ];

    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-local-repair-{Guid.NewGuid():N}"
    );

    [Fact]
    public void RepairRequiredEnvironmentActionIsWhitelisted()
    {
        Assert.True(
            LocalDependencyRepairActionIds.IsKnown(
                LocalDependencyRepairActionIds.RepairRequiredEnvInstallLatestCodex
            )
        );
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncRunsWhitelistedWingetInstallAndWritesStatus()
    {
        var processRunner = new RecordingProcessRunner();
        var service = CreateService(processRunner);
        var statusPath = Path.Combine(_rootDirectory, "runtime", "status.json");

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.InstallNodeNpm,
            statusPath
        );

        Assert.True(result.Succeeded);
        var command = Assert.Single(processRunner.Commands);
        Assert.Equal("winget", command.FileName);
        Assert.Contains("OpenJS.NodeJS.LTS", command.Arguments, StringComparison.Ordinal);
        Assert.True(File.Exists(statusPath));
        var status = JsonSerializer.Deserialize<LocalDependencyRepairResult>(
            File.ReadAllText(statusPath),
            JsonOptions
        );
        Assert.NotNull(status);
        Assert.True(status!.Succeeded);
        Assert.Equal(LocalDependencyRepairActionIds.InstallNodeNpm, status.ActionId);
        var progress = JsonSerializer.Deserialize<LocalDependencyRepairProgress>(
            File.ReadAllText(statusPath),
            JsonOptions
        );
        Assert.NotNull(progress);
        Assert.True(progress!.IsCompleted);
        Assert.Equal("completed", progress.Phase);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(statusPath)!, "*.tmp"));
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncRunsWingetRepairBootstrap()
    {
        var processRunner = new RecordingProcessRunner();
        var service = CreateService(processRunner);
        var statusPath = Path.Combine(_rootDirectory, "runtime", "status.json");

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairWinget,
            statusPath
        );

        Assert.True(result.Succeeded);
        var command = Assert.Single(processRunner.Commands);
        Assert.Equal("powershell.exe", command.FileName);
        Assert.Contains("-NoLogo", command.Arguments, StringComparison.Ordinal);
        Assert.Contains("-NoProfile", command.Arguments, StringComparison.Ordinal);
        Assert.Contains("-NonInteractive", command.Arguments, StringComparison.Ordinal);
        Assert.Contains("-ExecutionPolicy Bypass", command.Arguments, StringComparison.Ordinal);
        Assert.Contains("Microsoft.WinGet.Client", command.Arguments, StringComparison.Ordinal);
        Assert.Contains("Repair-WinGetPackageManager", command.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncWritesVisibleProgressBeforeCommandFinishes()
    {
        var processRunner = new BlockingProcessRunner();
        var service = CreateService(processRunner);
        var statusPath = Path.Combine(_rootDirectory, "runtime", "status.json");

        var repairTask = service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.InstallCodexCli,
            statusPath
        );
        await processRunner.CommandStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var progress = JsonSerializer.Deserialize<LocalDependencyRepairProgress>(
            File.ReadAllText(statusPath),
            JsonOptions
        );
        Assert.NotNull(progress);
        Assert.False(progress!.IsCompleted);
        Assert.Equal("commandRunning", progress.Phase);
        Assert.NotNull(progress.CommandLine);
        Assert.Contains("npm install -g @openai/codex", progress.CommandLine, StringComparison.Ordinal);
        Assert.Contains("@openai/codex@latest", progress.CommandLine, StringComparison.Ordinal);
        Assert.NotNull(progress.LogPath);

        processRunner.Complete(new LocalEnvironmentProcessResult(0, "installed", ""));
        var result = await repairTask;

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncRunsRequiredEnvironmentAndLatestCodexInstallInOrder()
    {
        var processRunner = new ScriptedProcessRunner(
            new LocalEnvironmentProcessResult(0, "v1.10.0", ""),
            new LocalEnvironmentProcessResult(1, "", "node not found"),
            new LocalEnvironmentProcessResult(1, "", "npm not found"),
            new LocalEnvironmentProcessResult(0, "node installed", ""),
            new LocalEnvironmentProcessResult(0, "v22.12.0", ""),
            new LocalEnvironmentProcessResult(0, "10.9.0", ""),
            new LocalEnvironmentProcessResult(0, "updated latest codex", "")
        );
        var writtenPath = string.Empty;
        var refreshCount = 0;
        var service = CreateService(
            processRunner,
            directoryExists: path => path.Contains("nodejs", StringComparison.OrdinalIgnoreCase),
            createDirectory: _ => { },
            userPathReader: _ => string.Empty,
            userPathWriter: value => writtenPath = value,
            processPathRefresher: () => refreshCount++
        );
        var statusPath = Path.Combine(_rootDirectory, "runtime", "status.json");

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallLatestCodex,
            statusPath
        );

        Assert.True(result.Succeeded);
        Assert.Equal("一键修复并安装最新 Codex 已完成。", result.Summary);
        Assert.Equal(
            RequiredEnvironmentLatestCodexCommandOrder,
            processRunner.Commands.Select(command => $"{command.FileName} {command.Arguments}")
        );
        Assert.Contains("npm", writtenPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nodejs", writtenPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(refreshCount >= 3);
        var progress = JsonSerializer.Deserialize<LocalDependencyRepairProgress>(
            File.ReadAllText(statusPath),
            JsonOptions
        );
        Assert.NotNull(progress);
        Assert.True(progress!.IsCompleted);
        Assert.Equal("completed", progress.Phase);
        Assert.Equal(LocalDependencyRepairActionIds.RepairRequiredEnvInstallLatestCodex, progress.ActionId);
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncRepairsWingetBeforeRequiredEnvironmentInstall()
    {
        var processRunner = new ScriptedProcessRunner(
            new LocalEnvironmentProcessResult(1, "", "winget not found"),
            new LocalEnvironmentProcessResult(0, "winget repaired", ""),
            new LocalEnvironmentProcessResult(0, "v1.10.0", ""),
            new LocalEnvironmentProcessResult(0, "v22.12.0", ""),
            new LocalEnvironmentProcessResult(0, "10.9.0", ""),
            new LocalEnvironmentProcessResult(0, "updated latest codex", "")
        );
        var service = CreateService(
            processRunner,
            createDirectory: _ => { },
            userPathReader: _ => string.Empty,
            userPathWriter: _ => { },
            processPathRefresher: () => { }
        );

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallLatestCodex,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.True(result.Succeeded);
        Assert.Equal("winget --version", FormatCommand(processRunner.Commands[0]));
        Assert.Equal("powershell.exe", processRunner.Commands[1].FileName);
        Assert.Contains("Repair-WinGetPackageManager", processRunner.Commands[1].Arguments);
        Assert.Equal("winget --version", FormatCommand(processRunner.Commands[2]));
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncStopsRequiredEnvironmentInstallWhenWingetRepairFails()
    {
        var processRunner = new ScriptedProcessRunner(
            new LocalEnvironmentProcessResult(1, "", "winget not found"),
            new LocalEnvironmentProcessResult(9, "", "Repair-WinGetPackageManager 失败")
        );
        var service = CreateService(processRunner, processPathRefresher: () => { });

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallLatestCodex,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.False(result.Succeeded);
        Assert.Equal(9, result.ExitCode);
        Assert.Contains("Repair-WinGetPackageManager 失败", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(
            processRunner.Commands,
            command => command.Arguments.Contains("OpenJS.NodeJS.LTS", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncStopsRequiredEnvironmentInstallWhenNpmStillUnavailable()
    {
        var processRunner = new ScriptedProcessRunner(
            new LocalEnvironmentProcessResult(0, "v1.10.0", ""),
            new LocalEnvironmentProcessResult(1, "", "node not found"),
            new LocalEnvironmentProcessResult(1, "", "npm not found"),
            new LocalEnvironmentProcessResult(0, "node installed", ""),
            new LocalEnvironmentProcessResult(0, "v22.12.0", ""),
            new LocalEnvironmentProcessResult(1, "", "npm still not found")
        );
        var service = CreateService(processRunner, processPathRefresher: () => { });

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallLatestCodex,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.False(result.Succeeded);
        Assert.Contains("Node.js/npm 安装后仍不可用", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(
            processRunner.Commands,
            command => command.Arguments.Contains("@openai/codex@latest", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncReturnsExitCodeDetailAndLogPathForCommandFailure()
    {
        var processRunner = new RecordingProcessRunner(
            new LocalEnvironmentProcessResult(7, "", "winget 安装失败")
        );
        var service = CreateService(processRunner);
        var statusPath = Path.Combine(_rootDirectory, "runtime", "status.json");

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.InstallNodeNpm,
            statusPath
        );

        Assert.False(result.Succeeded);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("退出码 7", result.Detail, StringComparison.Ordinal);
        Assert.Contains("winget 安装失败", result.Detail, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.LogPath));
        var progress = JsonSerializer.Deserialize<LocalDependencyRepairProgress>(
            File.ReadAllText(statusPath),
            JsonOptions
        );
        Assert.NotNull(progress);
        Assert.True(progress!.IsCompleted);
        Assert.Equal("failed", progress.Phase);
        Assert.Equal(7, progress.ExitCode);
        Assert.Contains("winget 安装失败", progress.RecentOutput);
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncKeepsWingetRepairFailureDetails()
    {
        var processRunner = new RecordingProcessRunner(
            new LocalEnvironmentProcessResult(9, "准备修复 winget", "Repair-WinGetPackageManager 失败")
        );
        var service = CreateService(processRunner);
        var statusPath = Path.Combine(_rootDirectory, "runtime", "status.json");

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairWinget,
            statusPath
        );

        Assert.False(result.Succeeded);
        Assert.Equal(9, result.ExitCode);
        Assert.Contains("退出码 9", result.Detail, StringComparison.Ordinal);
        Assert.Contains("Repair-WinGetPackageManager 失败", result.Detail, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.LogPath));
        var progress = JsonSerializer.Deserialize<LocalDependencyRepairProgress>(
            File.ReadAllText(statusPath),
            JsonOptions
        );
        Assert.NotNull(progress);
        Assert.True(progress!.IsCompleted);
        Assert.Equal("failed", progress.Phase);
        Assert.Equal(9, progress.ExitCode);
        Assert.Contains("准备修复 winget", progress.RecentOutput);
        Assert.Contains("Repair-WinGetPackageManager 失败", progress.RecentOutput);
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncRejectsUnknownAction()
    {
        var processRunner = new RecordingProcessRunner();
        var service = CreateService(processRunner);

        var result = await service.ExecuteRepairModeAsync(
            "run-anything",
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.False(result.Succeeded);
        Assert.Empty(processRunner.Commands);
        Assert.Contains("白名单", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepairUserPathAddsOnlyAllowedSafeDirectories()
    {
        var processRunner = new RecordingProcessRunner();
        var writtenPath = string.Empty;
        var createdDirectories = new List<string>();
        var service = CreateService(
            processRunner,
            directoryExists: path => path.Contains("nodejs", StringComparison.OrdinalIgnoreCase),
            createDirectory: createdDirectories.Add,
            userPathReader: _ => "C:\\Existing",
            userPathWriter: value => writtenPath = value
        );

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairUserPath,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.True(result.Succeeded);
        Assert.Contains("npm", writtenPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nodejs", writtenPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("run-anything", writtenPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            createdDirectories,
            path => path.EndsWith("\\npm", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task RepairUserPathCleansDuplicateAndUnreachableUserEntries()
    {
        var processRunner = new RecordingProcessRunner();
        var writtenPath = string.Empty;
        var service = CreateService(
            processRunner,
            directoryExists: path =>
                path.Equals("C:\\Keep", StringComparison.OrdinalIgnoreCase)
                || path.Contains("nodejs", StringComparison.OrdinalIgnoreCase),
            createDirectory: _ => { },
            userPathReader: _ => "C:\\Keep;C:\\Keep;C:\\Missing;%NOT_EXPANDED%\\bin",
            userPathWriter: value => writtenPath = value
        );

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairUserPath,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.True(result.Succeeded);
        var entries = writtenPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(entries, entry => entry.Equals("C:\\Keep", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            entries,
            entry => entry.Equals("C:\\Missing", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            entries,
            entry => entry.Equals("%NOT_EXPANDED%\\bin", StringComparison.Ordinal)
        );
        Assert.Contains("重复目录", result.Detail, StringComparison.Ordinal);
        Assert.Contains("失效目录", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunElevatedRepairAsyncUsesRepairModeArgumentsAndRunAsVerb()
    {
        var processRunner = new RecordingProcessRunner();
        ProcessStartInfo? captured = null;
        var service = CreateService(
            processRunner,
            currentProcessPathResolver: () => "C:\\Program Files\\CodexCliPlus\\CodexCliPlus.exe",
            processStarter: startInfo =>
            {
                captured = startInfo;
                return null;
            }
        );

        var result = await service.RunElevatedRepairAsync(
            LocalDependencyRepairActionIds.InstallCodexCli
        );

        Assert.False(result.Succeeded);
        Assert.NotNull(captured);
        Assert.True(captured!.UseShellExecute);
        Assert.Equal("runas", captured.Verb);
        Assert.Contains("--repair", captured.Arguments, StringComparison.Ordinal);
        Assert.Contains(
            LocalDependencyRepairActionIds.InstallCodexCli,
            captured.Arguments,
            StringComparison.Ordinal
        );
        Assert.Contains("--status", captured.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunElevatedRepairAsyncReportsStartingProgressBeforeLaunchingProcess()
    {
        var processRunner = new RecordingProcessRunner();
        var progressEvents = new List<LocalDependencyRepairProgress>();
        var service = CreateService(
            processRunner,
            currentProcessPathResolver: () => "C:\\Program Files\\CodexCliPlus\\CodexCliPlus.exe",
            processStarter: _ => null
        );

        var result = await service.RunElevatedRepairAsync(
            LocalDependencyRepairActionIds.InstallCodexCli,
            progressEvents.Add
        );

        Assert.False(result.Succeeded);
        var progress = Assert.Single(progressEvents);
        Assert.Equal(LocalDependencyRepairActionIds.InstallCodexCli, progress.ActionId);
        Assert.Equal("starting", progress.Phase);
        Assert.False(progress.IsCompleted);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private LocalDependencyRepairService CreateService(
        ILocalEnvironmentProcessRunner processRunner,
        Func<string?>? currentProcessPathResolver = null,
        Func<ProcessStartInfo, Process?>? processStarter = null,
        Func<string, bool>? directoryExists = null,
        Action<string>? createDirectory = null,
        Func<EnvironmentVariableTarget, string?>? userPathReader = null,
        Action<string>? userPathWriter = null,
        Action? processPathRefresher = null
    )
    {
        return new LocalDependencyRepairService(
            new TestPathService(_rootDirectory),
            new TestLogger(_rootDirectory),
            processRunner,
            currentProcessPathResolver,
            processStarter,
            directoryExists,
            createDirectory,
            userPathReader,
            userPathWriter,
            environmentChangeBroadcaster: () => { },
            processPathRefresher: processPathRefresher ?? (() => { })
        );
    }

    private static string FormatCommand((string FileName, string Arguments) command) =>
        $"{command.FileName} {command.Arguments}";

    private sealed class RecordingProcessRunner : ILocalEnvironmentProcessRunner
    {
        private readonly LocalEnvironmentProcessResult _result;

        public RecordingProcessRunner()
            : this(new LocalEnvironmentProcessResult(0, "ok", "")) { }

        public RecordingProcessRunner(LocalEnvironmentProcessResult result)
        {
            _result = result;
        }

        public List<(string FileName, string Arguments)> Commands { get; } = [];

        public Task<LocalEnvironmentProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add((fileName, string.Join(" ", arguments)));
            return Task.FromResult(_result);
        }
    }

    private sealed class ScriptedProcessRunner : ILocalEnvironmentProcessRunner
    {
        private readonly Queue<LocalEnvironmentProcessResult> _results;

        public ScriptedProcessRunner(params LocalEnvironmentProcessResult[] results)
        {
            _results = new Queue<LocalEnvironmentProcessResult>(results);
        }

        public List<(string FileName, string Arguments)> Commands { get; } = [];

        public Task<LocalEnvironmentProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add((fileName, string.Join(" ", arguments)));
            return Task.FromResult(
                _results.Count == 0
                    ? new LocalEnvironmentProcessResult(0, "ok", "")
                    : _results.Dequeue()
            );
        }
    }

    private sealed class BlockingProcessRunner : ILocalEnvironmentProcessRunner
    {
        private readonly TaskCompletionSource<LocalEnvironmentProcessResult> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public TaskCompletionSource<bool> CommandStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task<LocalEnvironmentProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommandStarted.TrySetResult(true);
            return _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete(LocalEnvironmentProcessResult result)
        {
            _completion.TrySetResult(result);
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

    private sealed class TestLogger(string rootDirectory) : IAppLogger
    {
        public string LogFilePath { get; } = Path.Combine(rootDirectory, "logs", "desktop.log");

        public void Info(string message) { }

        public void Warn(string message) { }

        public void LogError(string message, Exception? exception = null) { }
    }
}
