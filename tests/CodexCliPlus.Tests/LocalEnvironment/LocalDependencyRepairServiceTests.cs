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

public sealed class LocalDependencyRepairServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-local-repair-{Guid.NewGuid():N}"
    );

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

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private LocalDependencyRepairService CreateService(
        RecordingProcessRunner processRunner,
        Func<string?>? currentProcessPathResolver = null,
        Func<ProcessStartInfo, Process?>? processStarter = null,
        Func<string, bool>? directoryExists = null,
        Action<string>? createDirectory = null,
        Func<EnvironmentVariableTarget, string?>? userPathReader = null,
        Action<string>? userPathWriter = null
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
            environmentChangeBroadcaster: () => { }
        );
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
            return Task.FromResult(new LocalEnvironmentProcessResult(0, "ok", ""));
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
