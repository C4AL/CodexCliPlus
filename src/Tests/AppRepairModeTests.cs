using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using CodexCliPlus.Core.Abstractions.LocalEnvironment;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.LocalEnvironment;
using CodexCliPlus.Infrastructure.LocalEnvironment;

namespace CodexCliPlus.Tests;

[Trait("Category", "Fast")]
public sealed class AppRepairModeTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-app-repair-{Guid.NewGuid():N}"
    );

    [Fact]
    public void ExecuteRepairModeCompletesWhenCurrentSynchronizationContextIsBlocked()
    {
        var syncContext = new QueuedSynchronizationContext();
        var pathService = new YieldingPathService(_rootDirectory);
        var processRunner = new RecordingProcessRunner();
        var service = new LocalDependencyRepairService(
            pathService,
            new TestLogger(_rootDirectory),
            processRunner
        );
        var statusPath = Path.Combine(_rootDirectory, "runtime", "status.json");
        LocalDependencyRepairResult? result = null;
        Exception? exception = null;
        using var completed = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(syncContext);
            try
            {
                result = App.ExecuteRepairMode(
                    service,
                    LocalDependencyRepairActionIds.InstallCodexCli,
                    statusPath
                );
            }
            catch (Exception caught)
            {
                exception = caught;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var completedWithoutPumping = completed.Wait(TimeSpan.FromSeconds(5));
        if (!completedWithoutPumping)
        {
            DrainUntilCompleted(syncContext, completed, TimeSpan.FromSeconds(5));
        }

        Assert.True(
            completedWithoutPumping,
            "修复模式入口不应依赖当前 SynchronizationContext 泵消息才能完成。"
        );
        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.True(result!.Succeeded);
        Assert.Single(processRunner.Commands);
        Assert.DoesNotContain(
            pathService.ObservedContexts,
            context => ReferenceEquals(context, syncContext)
        );
        Assert.Equal(0, syncContext.PostCount);
    }

    [Fact]
    public void DesktopRepairRequestCatchesRepairFlowFailuresAndPostsResult()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src", "DesktopShell", "App",
                "MainWindow.WebViewHost.cs"
            ),
            Encoding.UTF8
        );
        var method = SliceBetween(
            source,
            "private async Task RunLocalDependencyRepairAsync",
            "private Task PostWebUiCommandOnDispatcherAsync"
        );
        var outerCatchStart = method.LastIndexOf(
            "catch (Exception exception)",
            StringComparison.Ordinal
        );
        Assert.True(outerCatchStart >= 0, "Expected repair flow catch block.");
        var notificationStart = method.IndexOf("if (result.Succeeded)", outerCatchStart, StringComparison.Ordinal);
        Assert.True(notificationStart > outerCatchStart, "Expected notification branch after repair catch.");
        var catchBlock = method[outerCatchStart..notificationStart];

        Assert.Contains(
            "_localDependencyRepairService.RunElevatedRepairAsync",
            method,
            StringComparison.Ordinal
        );
        Assert.Contains("_localDependencyHealthService.CheckAsync", method, StringComparison.Ordinal);
        Assert.Contains("CreateLocalDependencyRepairFailure", catchBlock, StringComparison.Ordinal);
        Assert.Contains("AttachLocalDependencyRepairDebugReportIfNeeded", method, StringComparison.Ordinal);
        Assert.Contains(
            "LocalDependencyRepairDebugReportWriter.WriteToDesktop",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains("_settings.EnableLocalRepairDebugReport", source, StringComparison.Ordinal);
        Assert.Contains("DebugReportPath", source, StringComparison.Ordinal);
        Assert.Contains(
            "_changeBroadcastService.Broadcast(\"local-environment\")",
            method,
            StringComparison.Ordinal
        );
        Assert.Contains("type = \"localDependencyRepairResult\"", catchBlock, StringComparison.Ordinal);
        Assert.Contains("Succeeded = false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalDependencyRepairDebugReportWriterCreatesCodexReadyDesktopReport()
    {
        var desktopDirectory = Path.Combine(_rootDirectory, "Desktop");
        var generatedAt = new DateTimeOffset(2026, 5, 2, 3, 4, 5, 678, TimeSpan.Zero);
        var result = new LocalDependencyRepairResult
        {
            ActionId = LocalDependencyRepairActionIds.InstallNodeNpm,
            Succeeded = false,
            ExitCode = 1,
            Summary = "安装 Node.js LTS 和 npm 失败。",
            Detail = "命令执行失败。",
            LogPath = @"C:\logs\local-environment-repair.log",
        };
        var snapshot = new LocalDependencySnapshot
        {
            CheckedAt = generatedAt,
            ReadinessScore = 40,
            Summary = "本地环境仍有缺失。",
            Items =
            [
                new LocalDependencyItem
                {
                    Id = "node",
                    Name = "Node.js",
                    Status = LocalDependencyStatus.Missing,
                    Severity = LocalDependencySeverity.Required,
                    Detail = "未找到 node 命令。",
                    Recommendation = "安装 Node.js LTS，安装后可直接重新检测。",
                    RepairActionId = LocalDependencyRepairActionIds.InstallNodeNpm,
                },
            ],
            RepairCapabilities =
            [
                new LocalDependencyRepairCapability
                {
                    ActionId = LocalDependencyRepairActionIds.InstallNodeNpm,
                    Name = "安装 Node.js LTS 和 npm",
                    IsAvailable = true,
                    RequiresElevation = true,
                    Detail = "可使用内置修复。",
                },
            ],
        };
        var progress = new LocalDependencyRepairProgress
        {
            ActionId = LocalDependencyRepairActionIds.InstallNodeNpm,
            Phase = "commandRunning",
            Message = "正在执行安装命令。",
            CommandLine = "winget install OpenJS.NodeJS.LTS",
            RecentOutput = ["准备安装", "安装失败"],
            LogPath = result.LogPath,
            UpdatedAt = generatedAt,
            ExitCode = 1,
        };

        var reportPath = CodexCliPlus.LocalDependencyRepairDebugReportWriter.WriteToDesktop(
            desktopDirectory,
            "1.2.3",
            "1.2.3+test",
            "local-env-repair-test",
            result,
            snapshot,
            progress,
            @"C:\logs\app.log",
            generatedAt
        );

        Assert.StartsWith(
            Path.GetFullPath(desktopDirectory),
            Path.GetFullPath(reportPath),
            StringComparison.OrdinalIgnoreCase
        );
        var expectedFileName =
            "CodexCliPlus-本地环境修复报告-"
            + generatedAt.LocalDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
            + ".txt";
        Assert.Equal(expectedFileName, Path.GetFileName(reportPath));
        var report = File.ReadAllText(reportPath, Encoding.UTF8);
        Assert.Contains("请将本文件完整提供给 Codex", report, StringComparison.Ordinal);
        Assert.Contains(LocalDependencyRepairActionIds.InstallNodeNpm, report, StringComparison.Ordinal);
        Assert.Contains("命令执行失败。", report, StringComparison.Ordinal);
        Assert.Contains("winget install OpenJS.NodeJS.LTS", report, StringComparison.Ordinal);
        Assert.Contains("Node.js", report, StringComparison.Ordinal);
        Assert.Contains("[修复指南]", report, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopRepairBridgeStartsRepairFlowOutsideUiDispatcher()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src", "DesktopShell", "App",
                "MainWindow.WebViewHost.cs"
            ),
            Encoding.UTF8
        );
        var repairCase = SliceBetween(
            source,
            "case \"runLocalDependencyRepair\":",
            "case \"managementRequest\":"
        );

        Assert.Contains("Task.Run", repairCase, StringComparison.Ordinal);
        Assert.Contains("RunLocalDependencyRepairAsync", repairCase, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatcher.InvokeAsync(async () =>", repairCase, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodexCliPlus.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string SliceBetween(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Expected to find '{start}'.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Expected to find '{end}' after '{start}'.");
        return source[startIndex..endIndex];
    }

    private static void DrainUntilCompleted(
        QueuedSynchronizationContext syncContext,
        ManualResetEventSlim completed,
        TimeSpan timeout
    )
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!completed.IsSet && DateTimeOffset.UtcNow < deadline)
        {
            syncContext.Drain();
            completed.Wait(TimeSpan.FromMilliseconds(10));
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _workItems =
            new();
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            _workItems.Enqueue((d, state));
        }

        public void Drain()
        {
            while (_workItems.TryDequeue(out var workItem))
            {
                workItem.Callback(workItem.State);
            }
        }
    }

    private sealed class YieldingPathService : IPathService
    {
        public YieldingPathService(string rootDirectory)
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

        public List<SynchronizationContext?> ObservedContexts { get; } = [];

        public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObservedContexts.Add(SynchronizationContext.Current);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            ObservedContexts.Add(SynchronizationContext.Current);
            Directory.CreateDirectory(Directories.RootDirectory);
            Directory.CreateDirectory(Directories.LogsDirectory);
            Directory.CreateDirectory(Directories.ConfigDirectory);
            Directory.CreateDirectory(Directories.BackendDirectory);
            Directory.CreateDirectory(Directories.CacheDirectory);
            Directory.CreateDirectory(Directories.DiagnosticsDirectory);
            Directory.CreateDirectory(Directories.RuntimeDirectory);
        }
    }

    private sealed class RecordingProcessRunner : ILocalEnvironmentProcessRunner
    {
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
            return Task.FromResult(new LocalEnvironmentProcessResult(0, "installed", ""));
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
