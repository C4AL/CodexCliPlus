using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    private static readonly JsonSerializerOptions BundleJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private const string NodeReleaseIndexJson =
        """
        [
          { "version": "v23.0.0", "lts": false, "files": ["win-x64-msi"] },
          { "version": "v22.12.0", "lts": "Jod", "files": ["win-x64-msi", "win-x86-msi"] }
        ]
        """;

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
        Assert.True(
            LocalDependencyRepairActionIds.IsKnown(
                LocalDependencyRepairActionIds.RepairRequiredEnvInstallBundledCodex
            )
        );
        Assert.True(
            LocalDependencyRepairActionIds.IsKnown(
                LocalDependencyRepairActionIds.UpgradeBundledEnvInstallLatestCodex
            )
        );
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncRunsBuiltInNodeInstallerAndWritesStatus()
    {
        var processRunner = new RecordingProcessRunner();
        var service = CreateService(processRunner);
        var statusPath = Path.Combine(_rootDirectory, "runtime", "status.json");

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.InstallNodeNpm,
            statusPath
        );

        Assert.True(result.Succeeded);
        Assert.Equal("msiexec.exe", processRunner.Commands[0].FileName);
        Assert.Contains("node-v22.12.0-x64.msi", processRunner.Commands[0].Arguments);
        Assert.DoesNotContain(
            processRunner.Commands,
            command => command.FileName.Equals("winget", StringComparison.OrdinalIgnoreCase)
        );
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
    public async Task ExecuteRepairModeAsyncKeepsExistingStatusWhenInitialStatusRewriteFails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var processRunner = new RecordingProcessRunner();
        var service = CreateService(processRunner);
        var statusPath = Path.Combine(_rootDirectory, "runtime", "status.json");
        Directory.CreateDirectory(Path.GetDirectoryName(statusPath)!);
        var originalJson =
            """
            {
              "actionId": "installNodeNpm",
              "phase": "previous"
            }
            """;
        await File.WriteAllTextAsync(statusPath, originalJson);
        await using var lockedStatus = new FileStream(
            statusPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );

        var exception = await Record.ExceptionAsync(() =>
            service.ExecuteRepairModeAsync(
                LocalDependencyRepairActionIds.InstallNodeNpm,
                statusPath
            )
        );

        Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
        Assert.Equal(originalJson, await File.ReadAllTextAsync(statusPath));
        Assert.Empty(
            Directory.GetFiles(Path.GetDirectoryName(statusPath)!, "status.json.*.tmp")
        );
        Assert.Empty(processRunner.Commands);
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
            [
                $"msiexec.exe /i {Path.Combine(_rootDirectory, "cache", "local-environment", "nodejs", "v22.12.0", "node-v22.12.0-x64.msi")} /qn /norestart",
                "node --version",
                "cmd.exe /d /c npm --version",
                "cmd.exe /d /c npm install -g @openai/codex@latest",
            ],
            processRunner.Commands.Select(command => $"{command.FileName} {command.Arguments}")
        );
        Assert.Contains("npm", writtenPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nodejs", writtenPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(refreshCount >= 2);
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
    public async Task UpgradeBundledEnvironmentInstallsLatestNodeLtsBeforeLatestCodex()
    {
        var processRunner = new ScriptedProcessRunner(
            new LocalEnvironmentProcessResult(0, "node installed", ""),
            new LocalEnvironmentProcessResult(0, "v22.12.0", ""),
            new LocalEnvironmentProcessResult(0, "10.9.0", ""),
            new LocalEnvironmentProcessResult(0, "updated latest codex", "")
        );
        var service = CreateService(
            processRunner,
            directoryExists: path => path.Contains("nodejs", StringComparison.OrdinalIgnoreCase),
            createDirectory: _ => { },
            userPathReader: _ => string.Empty,
            userPathWriter: _ => { },
            processPathRefresher: () => { }
        );

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.UpgradeBundledEnvInstallLatestCodex,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.True(result.Succeeded);
        Assert.Equal(
            [
                $"msiexec.exe /i {Path.Combine(_rootDirectory, "cache", "local-environment", "nodejs", "v22.12.0", "node-v22.12.0-x64.msi")} /qn /norestart",
                "node --version",
                "cmd.exe /d /c npm --version",
                "cmd.exe /d /c npm install -g @openai/codex@latest",
            ],
            processRunner.Commands.Select(command => $"{command.FileName} {command.Arguments}")
        );
    }

    [Fact]
    public async Task RequiredEnvironmentRepairReturnsNetworkFallbackWhenNodeIndexFails()
    {
        var processRunner = new RecordingProcessRunner();
        var service = CreateService(
            processRunner,
            downloadStringAsync: (_, _) =>
                throw new HttpRequestException("DNS failed for nodejs.org")
        );
        var statusPath = Path.Combine(_rootDirectory, "runtime", "status.json");

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallLatestCodex,
            statusPath
        );

        Assert.False(result.Succeeded);
        Assert.Equal("network", result.FailureKind);
        Assert.Equal(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallBundledCodex,
            result.RecommendedFallbackActionId
        );
        var progress = JsonSerializer.Deserialize<LocalDependencyRepairProgress>(
            File.ReadAllText(statusPath),
            JsonOptions
        );
        Assert.Equal("network", progress?.FailureKind);
        Assert.Equal(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallBundledCodex,
            progress?.RecommendedFallbackActionId
        );
        Assert.Empty(processRunner.Commands);
    }

    [Fact]
    public async Task RequiredEnvironmentRepairReturnsNetworkFallbackWhenNpmRegistryFails()
    {
        var processRunner = new ScriptedProcessRunner(
            new LocalEnvironmentProcessResult(0, "node installed", ""),
            new LocalEnvironmentProcessResult(0, "v22.12.0", ""),
            new LocalEnvironmentProcessResult(0, "10.9.0", ""),
            new LocalEnvironmentProcessResult(
                1,
                "",
                "npm ERR! code ENOTFOUND\nnpm ERR! request to https://registry.npmjs.org/@openai%2fcodex failed"
            )
        );
        var service = CreateService(processRunner);

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallLatestCodex,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.False(result.Succeeded);
        Assert.Equal("network", result.FailureKind);
        Assert.Equal(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallBundledCodex,
            result.RecommendedFallbackActionId
        );
    }

    [Fact]
    public async Task RequiredEnvironmentRepairDoesNotOfferOfflineFallbackForMsiFailure()
    {
        var processRunner = new ScriptedProcessRunner(
            new LocalEnvironmentProcessResult(1603, "", "msiexec failed")
        );
        var service = CreateService(processRunner);

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallLatestCodex,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.False(result.Succeeded);
        Assert.Null(result.FailureKind);
        Assert.Null(result.RecommendedFallbackActionId);
    }

    [Fact]
    public async Task BundledEnvironmentRepairFailsWithoutManifestAndDoesNotWriteUpgradeMarker()
    {
        var assetRoot = Path.Combine(_rootDirectory, "missing-local-environment-assets");
        var offlinePackageService = CreateOfflinePackageService(assetRoot);
        var service = CreateService(
            new RecordingProcessRunner(),
            offlinePackageService: offlinePackageService
        );

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallBundledCodex,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(offlinePackageService.PendingUpgradePath));
    }

    [Fact]
    public async Task BundledEnvironmentRepairFailsOnNodeShaMismatchAndDoesNotWriteUpgradeMarker()
    {
        var assetRoot = CreateBundledLocalEnvironmentAssets(nodeSha256Override: new string('0', 64));
        var offlinePackageService = CreateOfflinePackageService(assetRoot);
        var service = CreateService(
            new RecordingProcessRunner(),
            offlinePackageService: offlinePackageService
        );

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallBundledCodex,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.False(result.Succeeded);
        Assert.Contains("SHA256", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(offlinePackageService.PendingUpgradePath));
    }

    [Fact]
    public async Task BundledEnvironmentRepairInstallsOfflineCodexAndWritesSingleUpgradeMarker()
    {
        var assetRoot = CreateBundledLocalEnvironmentAssets();
        var offlinePackageService = CreateOfflinePackageService(assetRoot);
        var processRunner = new ScriptedProcessRunner(
            new LocalEnvironmentProcessResult(1, "", "node not found"),
            new LocalEnvironmentProcessResult(1, "", "npm not found"),
            new LocalEnvironmentProcessResult(0, "node installed", ""),
            new LocalEnvironmentProcessResult(0, "v22.12.0", ""),
            new LocalEnvironmentProcessResult(0, "10.9.0", ""),
            new LocalEnvironmentProcessResult(0, "codex installed from cache", ""),
            new LocalEnvironmentProcessResult(0, "codex 0.1.2", "")
        );
        var service = CreateService(
            processRunner,
            directoryExists: path => path.Contains("nodejs", StringComparison.OrdinalIgnoreCase),
            createDirectory: _ => { },
            userPathReader: _ => string.Empty,
            userPathWriter: _ => { },
            processPathRefresher: () => { },
            offlinePackageService: offlinePackageService
        );

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallBundledCodex,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.True(result.Succeeded);
        Assert.Contains(
            processRunner.Commands,
            command =>
                command.FileName == "cmd.exe"
                && command.Arguments.Contains("--offline", StringComparison.Ordinal)
                && command.Arguments.Contains("@openai/codex@0.1.2", StringComparison.Ordinal)
        );
        Assert.True(File.Exists(offlinePackageService.PendingUpgradePath));
        var marker = JsonSerializer.Deserialize<LocalEnvironmentOfflineUpgradeState>(
            await File.ReadAllTextAsync(offlinePackageService.PendingUpgradePath),
            JsonOptions
        );
        Assert.Equal("v22.12.0", marker?.OfflineNodeVersion);
        Assert.Equal("0.1.2", marker?.OfflineCodexVersion);
        Assert.Single(
            Directory.GetFiles(
                Path.GetDirectoryName(offlinePackageService.PendingUpgradePath)!,
                LocalEnvironmentOfflinePackageService.PendingUpgradeFileName
            )
        );
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncUsesBuiltInNodeInstallerDuringRequiredEnvironmentRepair()
    {
        var processRunner = new ScriptedProcessRunner(
            new LocalEnvironmentProcessResult(0, "node installed", ""),
            new LocalEnvironmentProcessResult(0, "v22.12.0", ""),
            new LocalEnvironmentProcessResult(0, "10.9.0", ""),
            new LocalEnvironmentProcessResult(0, "updated latest codex", "")
        );
        var service = CreateService(
            processRunner,
            directoryExists: path => path.Contains("nodejs", StringComparison.OrdinalIgnoreCase),
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
        Assert.Equal("一键修复并安装最新 Codex 已完成。", result.Summary);
        Assert.Contains(
            processRunner.Commands,
            command =>
                command.FileName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
                && command.Arguments.Contains("node-v22.12.0-x64.msi", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            processRunner.Commands,
            command => command.FileName.Equals("winget", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncDoesNotRepairWingetBeforeRequiredEnvironmentInstall()
    {
        var processRunner = new ScriptedProcessRunner(
            new LocalEnvironmentProcessResult(0, "node installed", ""),
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
        Assert.DoesNotContain(
            processRunner.Commands,
            command =>
                command.FileName.Equals("winget", StringComparison.OrdinalIgnoreCase)
                || command.Arguments.Contains("Repair-WinGetPackageManager", StringComparison.Ordinal)
        );
        Assert.Contains(
            processRunner.Commands,
            command => command.FileName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncStopsRequiredEnvironmentInstallWhenNodeInstallerFails()
    {
        var processRunner = new ScriptedProcessRunner(
            new LocalEnvironmentProcessResult(9, "", "msiexec 安装失败")
        );
        var service = CreateService(processRunner, processPathRefresher: () => { });

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.RepairRequiredEnvInstallLatestCodex,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.False(result.Succeeded);
        Assert.Equal(9, result.ExitCode);
        Assert.Contains("msiexec 安装失败", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(
            processRunner.Commands,
            command => command.Arguments.Contains("@openai/codex@latest", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncStopsRequiredEnvironmentInstallWhenNpmStillUnavailable()
    {
        var processRunner = new ScriptedProcessRunner(
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
    public async Task ExecuteRepairModeAsyncDownloadsOfficialNodeInstallerFromLatestLtsIndex()
    {
        var downloadedUris = new List<Uri>();
        var processRunner = new RecordingProcessRunner();
        var service = CreateService(
            processRunner,
            downloadFileAsync: (uri, path, _) =>
            {
                downloadedUris.Add(uri);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "fake msi");
                return Task.CompletedTask;
            }
        );
        var statusPath = Path.Combine(_rootDirectory, "runtime", "status.json");

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.InstallNodeNpm,
            statusPath
        );

        Assert.True(result.Succeeded);
        var downloadedUri = Assert.Single(downloadedUris);
        Assert.Equal(
            "https://nodejs.org/dist/v22.12.0/node-v22.12.0-x64.msi",
            downloadedUri.ToString()
        );
        Assert.Equal("msiexec.exe", processRunner.Commands[0].FileName);
        var progress = JsonSerializer.Deserialize<LocalDependencyRepairProgress>(
            File.ReadAllText(statusPath),
            JsonOptions
        );
        Assert.NotNull(progress);
        Assert.True(progress!.IsCompleted);
        Assert.Equal("completed", progress.Phase);
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncFailsWhenNodeReleaseIndexHasNoSupportedLtsInstaller()
    {
        var processRunner = new RecordingProcessRunner();
        var service = CreateService(
            processRunner,
            downloadStringAsync: (_, _) =>
                Task.FromResult(
                    """
                    [
                      { "version": "v23.0.0", "lts": false, "files": ["win-x64-msi"] }
                    ]
                    """
                )
        );

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.InstallNodeNpm,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.False(result.Succeeded);
        Assert.Equal("解析 Node.js LTS 安装包失败。", result.Summary);
        Assert.Contains("LTS 安装包", result.Detail, StringComparison.Ordinal);
        Assert.Empty(processRunner.Commands);
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncFailsWhenNodeInstallerDownloadFails()
    {
        var processRunner = new RecordingProcessRunner();
        var service = CreateService(
            processRunner,
            downloadFileAsync: (_, _, _) => throw new InvalidOperationException("download failed")
        );

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.InstallNodeNpm,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.False(result.Succeeded);
        Assert.Equal("下载 Node.js LTS 安装包失败。", result.Summary);
        Assert.Contains("download failed", result.Detail, StringComparison.Ordinal);
        Assert.Empty(processRunner.Commands);
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncReportsKnownWingetExitReasonWhenPowerShellSourceResetFails()
    {
        var wingetSourceDataMissing = unchecked((int)0x8A15000F);
        var processRunner = new ScriptedProcessRunner(
            new LocalEnvironmentProcessResult(wingetSourceDataMissing, "/\r\n-\r\n\\\r\n|", ""),
            new LocalEnvironmentProcessResult(wingetSourceDataMissing, "", ""),
            new LocalEnvironmentProcessResult(wingetSourceDataMissing, "", "")
        );
        var service = CreateService(processRunner);

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.InstallPowerShell,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.False(result.Succeeded);
        Assert.Equal(wingetSourceDataMissing, result.ExitCode);
        Assert.Contains("0x8A15000F", result.Detail, StringComparison.Ordinal);
        Assert.Contains("winget 源数据", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("：/；", result.Detail, StringComparison.Ordinal);
        Assert.Contains("自动重置源失败", result.Detail, StringComparison.Ordinal);
        Assert.Equal(3, processRunner.Commands.Count);
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncReportsWingetSourceNameMissingAfterSourceReset()
    {
        var wingetSourceDataMissing = unchecked((int)0x8A15000F);
        var wingetSourceNameMissing = unchecked((int)0x8A150012);
        var processRunner = new ScriptedProcessRunner(
            new LocalEnvironmentProcessResult(wingetSourceDataMissing, "", "Failed when opening source(s)"),
            new LocalEnvironmentProcessResult(wingetSourceDataMissing, "", "Failed when opening source(s)"),
            new LocalEnvironmentProcessResult(0, "源已重置", ""),
            new LocalEnvironmentProcessResult(
                wingetSourceNameMissing,
                "",
                "Did not find a source named: winget"
            )
        );
        var service = CreateService(processRunner);

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.InstallPowerShell,
            Path.Combine(_rootDirectory, "runtime", "status.json")
        );

        Assert.False(result.Succeeded);
        Assert.Equal(wingetSourceNameMissing, result.ExitCode);
        Assert.Contains("0x8A150012", result.Detail, StringComparison.Ordinal);
        Assert.Contains("未找到指定源名称", result.Detail, StringComparison.Ordinal);
        Assert.Contains("自动更新源失败", result.Detail, StringComparison.Ordinal);
        Assert.Equal(4, processRunner.Commands.Count);
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
    public async Task RunElevatedRepairAsyncExecutesInCurrentProcessWhenAlreadyAdministrator()
    {
        var processRunner = new BlockingProcessRunner();
        var progressEvents = new List<LocalDependencyRepairProgress>();
        var processStarterCalled = false;
        var refreshCount = 0;
        var service = CreateService(
            processRunner,
            processStarter: _ =>
            {
                processStarterCalled = true;
                return null;
            },
            processPathRefresher: () => refreshCount++,
            currentProcessAdministratorChecker: () => true
        );

        var repairTask = service.RunElevatedRepairAsync(
            LocalDependencyRepairActionIds.InstallCodexCli,
            progress =>
            {
                lock (progressEvents)
                {
                    progressEvents.Add(progress);
                }
            }
        );
        await processRunner.CommandStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForProgressAsync(
            progressEvents,
            progress =>
                progress.Phase == "commandRunning"
                && progress.CommandLine?.Contains(
                    "npm install -g @openai/codex@latest",
                    StringComparison.Ordinal
                ) == true
        );

        processRunner.Complete(new LocalEnvironmentProcessResult(0, "installed", ""));
        var result = await repairTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded);
        Assert.False(processStarterCalled);
        var command = Assert.Single(processRunner.Commands);
        Assert.Equal("cmd.exe", command.FileName);
        Assert.Contains("npm install -g @openai/codex@latest", command.Arguments);
        Assert.True(refreshCount >= 1);

        Assert.Empty(
            Directory.Exists(Path.Combine(_rootDirectory, "runtime"))
                ? Directory.GetFiles(
                    Path.Combine(_rootDirectory, "runtime"),
                    "local-environment-repair-*.json"
                )
                : []
        );

        LocalDependencyRepairProgress[] recordedProgress;
        lock (progressEvents)
        {
            recordedProgress = progressEvents.ToArray();
        }

        Assert.Contains(recordedProgress, progress => progress.Message == "正在准备修复。");
        Assert.Contains(recordedProgress, progress => progress.Phase == "commandRunning");
        Assert.Contains(recordedProgress, progress => progress.Phase == "completed");
    }

    [Fact]
    public async Task RunElevatedRepairAsyncRefreshesCommandProgressWhileCurrentProcessCommandRuns()
    {
        var processRunner = new BlockingProcessRunner();
        var progressEvents = new List<LocalDependencyRepairProgress>();
        var service = CreateService(
            processRunner,
            currentProcessAdministratorChecker: () => true,
            repairProgressHeartbeatInterval: TimeSpan.FromMilliseconds(100)
        );

        var repairTask = service.RunElevatedRepairAsync(
            LocalDependencyRepairActionIds.InstallCodexCli,
            progress =>
            {
                lock (progressEvents)
                {
                    progressEvents.Add(progress);
                }
            }
        );
        await processRunner.CommandStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForProgressAsync(
            progressEvents,
            progress =>
            {
                lock (progressEvents)
                {
                    return progressEvents.Count(candidate =>
                            candidate.Phase == "commandRunning"
                            && candidate.Message == "正在执行：安装 Codex CLI。"
                        )
                        >= 2;
                }
            }
        );

        processRunner.Complete(new LocalEnvironmentProcessResult(0, "installed", ""));
        var result = await repairTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded);
        LocalDependencyRepairProgress[] commandProgress;
        lock (progressEvents)
        {
            commandProgress = progressEvents
                .Where(progress =>
                    progress.Phase == "commandRunning"
                    && progress.Message == "正在执行：安装 Codex CLI。"
                )
                .ToArray();
        }

        Assert.True(commandProgress.Length >= 2);
        Assert.True(commandProgress.Select(progress => progress.UpdatedAt).Distinct().Count() >= 2);
    }

    [Fact]
    public async Task RunElevatedRepairAsyncReturnsCommandExceptionWhenCurrentProcessCommandFailsToStart()
    {
        var progressEvents = new List<LocalDependencyRepairProgress>();
        var service = CreateService(
            new ThrowingProcessRunner(new InvalidOperationException("bootstrap failed")),
            currentProcessAdministratorChecker: () => true
        );

        var result = await service.RunElevatedRepairAsync(
            LocalDependencyRepairActionIds.InstallCodexCli,
            progressEvents.Add
        );

        Assert.False(result.Succeeded);
        Assert.Contains("bootstrap failed", result.Detail, StringComparison.Ordinal);
        Assert.Contains(progressEvents, progress => progress.IsCompleted && progress.Phase == "failed");
        Assert.Empty(
            Directory.Exists(Path.Combine(_rootDirectory, "runtime"))
                ? Directory.GetFiles(
                    Path.Combine(_rootDirectory, "runtime"),
                    "local-environment-repair-*.json"
                )
                : []
        );
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
        Assert.Equal(ProcessWindowStyle.Hidden, captured.WindowStyle);
        Assert.Contains("--repair", captured.Arguments, StringComparison.Ordinal);
        Assert.Contains(
            LocalDependencyRepairActionIds.InstallCodexCli,
            captured.Arguments,
            StringComparison.Ordinal
        );
        Assert.Contains("--status", captured.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunElevatedRepairAsyncFailsQuicklyWhenStartedProcessDoesNotReportProgress()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var processRunner = new RecordingProcessRunner();
        Process? startedProcess = null;
        var service = CreateService(
            processRunner,
            currentProcessPathResolver: () => "C:\\Program Files\\CodexCliPlus\\CodexCliPlus.exe",
            processStarter: _ =>
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add("Start-Sleep -Seconds 30");
                startedProcess = Process.Start(startInfo);
                return startedProcess;
            },
            repairProcessFirstProgressTimeout: TimeSpan.FromMilliseconds(150)
        );

        try
        {
            var result = await service.RunElevatedRepairAsync(
                LocalDependencyRepairActionIds.InstallCodexCli
            );

            Assert.False(result.Succeeded);
            Assert.Equal("修复进程未回传进度。", result.Summary);
            Assert.Contains("local-environment-repair.log", result.Detail, StringComparison.Ordinal);
            Assert.Contains("local-environment-repair-", result.Detail, StringComparison.Ordinal);
            Assert.True(startedProcess is null || startedProcess.HasExited);
        }
        finally
        {
            if (startedProcess is not null)
            {
                StopProcessIfRunning(startedProcess.Id);
                startedProcess.Dispose();
            }
        }
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

    [Fact]
    public async Task RunElevatedRepairAsyncWritesFailedStatusWhenPreparationDirectoryCreationFails()
    {
        var progressEvents = new List<LocalDependencyRepairProgress>();
        var service = CreateService(
            new RecordingProcessRunner(),
            pathService: new TestPathService(
                _rootDirectory,
                ensureException: new IOException("runtime prepare failed")
            )
        );

        var result = await service.RunElevatedRepairAsync(
            LocalDependencyRepairActionIds.InstallCodexCli,
            progressEvents.Add
        );

        Assert.False(result.Succeeded);
        Assert.Equal("准备修复失败。", result.Summary);
        Assert.Contains("runtime prepare failed", result.Detail, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.LogPath));

        var statusPath = Assert.Single(
            Directory.GetFiles(
                Path.Combine(_rootDirectory, "runtime"),
                "local-environment-repair-*.json"
            )
        );
        var status = JsonSerializer.Deserialize<LocalDependencyRepairProgress>(
            File.ReadAllText(statusPath),
            JsonOptions
        );
        Assert.NotNull(status);
        Assert.True(status!.IsCompleted);
        Assert.Equal("failed", status.Phase);
        Assert.Equal(result.Summary, status.Summary);
        Assert.Contains(progressEvents, progress => progress.IsCompleted && progress.Phase == "failed");
    }

    [Fact]
    public async Task RunElevatedRepairAsyncWritesFailedStatusWhenRepairLogPathCannotBePrepared()
    {
        Directory.CreateDirectory(_rootDirectory);
        var logsFile = Path.Combine(_rootDirectory, "logs-file");
        File.WriteAllText(logsFile, "occupied");
        var progressEvents = new List<LocalDependencyRepairProgress>();
        var service = CreateService(
            new RecordingProcessRunner(),
            pathService: new TestPathService(
                _rootDirectory,
                logsDirectory: logsFile,
                createLogsDirectory: false
            )
        );

        var result = await service.RunElevatedRepairAsync(
            LocalDependencyRepairActionIds.InstallCodexCli,
            progressEvents.Add
        );

        Assert.False(result.Succeeded);
        Assert.Equal("准备修复失败。", result.Summary);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Equal(Path.Combine(logsFile, "local-environment-repair.log"), result.LogPath);

        var statusPath = Assert.Single(
            Directory.GetFiles(
                Path.Combine(_rootDirectory, "runtime"),
                "local-environment-repair-*.json"
            )
        );
        var status = JsonSerializer.Deserialize<LocalDependencyRepairProgress>(
            File.ReadAllText(statusPath),
            JsonOptions
        );
        Assert.NotNull(status);
        Assert.True(status!.IsCompleted);
        Assert.Equal("failed", status.Phase);
        Assert.Equal(result.LogPath, status.LogPath);
        Assert.Contains(progressEvents, progress => progress.IsCompleted && progress.Phase == "failed");
    }

    [Fact]
    public async Task RunElevatedRepairAsyncReportsFailedProgressWhenInitialStatusWriteFails()
    {
        Directory.CreateDirectory(_rootDirectory);
        var runtimeFile = Path.Combine(_rootDirectory, "runtime-file");
        File.WriteAllText(runtimeFile, "occupied");
        var progressEvents = new List<LocalDependencyRepairProgress>();
        var service = CreateService(
            new RecordingProcessRunner(),
            pathService: new TestPathService(
                _rootDirectory,
                runtimeDirectory: runtimeFile,
                createRuntimeDirectory: false
            )
        );

        var result = await service.RunElevatedRepairAsync(
            LocalDependencyRepairActionIds.InstallCodexCli,
            progressEvents.Add
        );

        Assert.False(result.Succeeded);
        Assert.Equal("准备修复失败。", result.Summary);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.False(string.IsNullOrWhiteSpace(result.LogPath));
        Assert.Contains(progressEvents, progress => progress.IsCompleted && progress.Phase == "failed");
    }

    [Fact]
    public async Task RunElevatedRepairAsyncReportsAuthorizationWaitAndTimesOutWhenLaunchBlocks()
    {
        var processRunner = new RecordingProcessRunner();
        var launchStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var releaseLaunch = new ManualResetEventSlim(false);
        var progressEvents = new List<LocalDependencyRepairProgress>();
        var service = CreateService(
            processRunner,
            currentProcessPathResolver: () => "C:\\Program Files\\CodexCliPlus\\CodexCliPlus.exe",
            processStarter: _ =>
            {
                launchStarted.TrySetResult(true);
                releaseLaunch.Wait();
                return null;
            },
            elevationAuthorizationTimeout: TimeSpan.FromMilliseconds(900)
        );

        try
        {
            var resultTask = service.RunElevatedRepairAsync(
                LocalDependencyRepairActionIds.InstallCodexCli,
                progressEvents.Add
            );
            await launchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var result = await resultTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(result.Succeeded);
            Assert.Equal("等待管理员授权超时。", result.Summary);
            Assert.Contains("管理员授权", result.Detail, StringComparison.Ordinal);
            Assert.True(
                progressEvents.Count(progress => progress.Message == "等待管理员授权。") >= 2
            );
        }
        finally
        {
            releaseLaunch.Set();
        }
    }

    [Fact]
    public async Task ExecuteRepairModeAsyncWritesFailureStatusWhenRepairExecutionThrows()
    {
        var service = CreateService(
            new ThrowingProcessRunner(new InvalidOperationException("bootstrap failed"))
        );
        var statusPath = Path.Combine(_rootDirectory, "runtime", "status.json");

        var result = await service.ExecuteRepairModeAsync(
            LocalDependencyRepairActionIds.InstallCodexCli,
            statusPath
        );

        Assert.False(result.Succeeded);
        Assert.Contains("bootstrap failed", result.Detail, StringComparison.Ordinal);
        var progress = JsonSerializer.Deserialize<LocalDependencyRepairProgress>(
            File.ReadAllText(statusPath),
            JsonOptions
        );
        Assert.NotNull(progress);
        Assert.True(progress!.IsCompleted);
        Assert.Equal("failed", progress.Phase);
        Assert.Contains("bootstrap failed", progress.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunElevatedRepairAsyncKillsStartedProcessWhenCancelled()
    {
        Directory.CreateDirectory(_rootDirectory);
        var pidPath = Path.Combine(
            _rootDirectory,
            $"local-repair-process-{Guid.NewGuid():N}.pid"
        );
        var escapedPidPath = pidPath.Replace("'", "''", StringComparison.Ordinal);
        int? processId = null;
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(
            new RecordingProcessRunner(),
            currentProcessPathResolver: () => "C:\\Program Files\\CodexCliPlus\\CodexCliPlus.exe",
            processStarter: _ =>
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add(
                    $"Set-Content -LiteralPath '{escapedPidPath}' -Value $PID -Encoding ascii; Start-Sleep -Seconds 60"
                );
                return Process.Start(startInfo);
            }
        );

        try
        {
            var repairTask = service.RunElevatedRepairAsync(
                LocalDependencyRepairActionIds.InstallCodexCli,
                cancellation.Token
            );
            processId = await WaitForPidAsync(pidPath);

            cancellation.Cancel();

            var exception = await Record.ExceptionAsync(async () => await repairTask);

            Assert.IsAssignableFrom<OperationCanceledException>(exception);
            Assert.False(await ProcessExistsAsync(processId.Value));
        }
        finally
        {
            if (processId is { } pid)
            {
                StopProcessIfRunning(pid);
            }
        }
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
        Action? processPathRefresher = null,
        TimeSpan? elevationAuthorizationTimeout = null,
        Func<bool>? currentProcessAdministratorChecker = null,
        IPathService? pathService = null,
        TimeSpan? repairProcessFirstProgressTimeout = null,
        TimeSpan? repairProgressHeartbeatInterval = null,
        Func<Uri, CancellationToken, Task<string>>? downloadStringAsync = null,
        Func<Uri, string, CancellationToken, Task>? downloadFileAsync = null,
        Func<Architecture>? osArchitectureProvider = null,
        LocalEnvironmentOfflinePackageService? offlinePackageService = null
    )
    {
        return new LocalDependencyRepairService(
            pathService ?? new TestPathService(_rootDirectory),
            new TestLogger(_rootDirectory),
            processRunner,
            currentProcessPathResolver,
            processStarter,
            directoryExists,
            createDirectory,
            userPathReader,
            userPathWriter,
            environmentChangeBroadcaster: () => { },
            processPathRefresher: processPathRefresher ?? (() => { }),
            elevationAuthorizationTimeout: elevationAuthorizationTimeout,
            currentProcessAdministratorChecker: currentProcessAdministratorChecker ?? (() => false),
            repairProcessFirstProgressTimeout: repairProcessFirstProgressTimeout,
            repairProgressHeartbeatInterval: repairProgressHeartbeatInterval,
            downloadStringAsync: downloadStringAsync ?? ((_, _) => Task.FromResult(NodeReleaseIndexJson)),
            downloadFileAsync: downloadFileAsync ?? WriteFakeNodeInstallerAsync,
            osArchitectureProvider: osArchitectureProvider ?? (() => Architecture.X64),
            offlinePackageService: offlinePackageService
        );
    }

    private static Task WriteFakeNodeInstallerAsync(
        Uri _,
        string path,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fake msi");
        return Task.CompletedTask;
    }

    private static string FormatCommand((string FileName, string Arguments) command) =>
        $"{command.FileName} {command.Arguments}";

    private static async Task WaitForProgressAsync(
        List<LocalDependencyRepairProgress> progressEvents,
        Func<LocalDependencyRepairProgress, bool> predicate
    )
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (progressEvents)
            {
                if (progressEvents.Any(predicate))
                {
                    return;
                }
            }

            await Task.Delay(50);
        }

        string phases;
        lock (progressEvents)
        {
            phases = string.Join(", ", progressEvents.Select(progress => progress.Phase));
        }

        throw new TimeoutException($"Expected repair progress was not reported. Phases: {phases}");
    }

    private static async Task<int> WaitForPidAsync(string path)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                var text = await TryReadSharedTextAsync(path);
                if (int.TryParse(text?.Trim(), out var processId))
                {
                    return processId;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Process PID file was not created.");
    }

    private static async Task<string?> TryReadSharedTextAsync(string path)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task<bool> ProcessExistsAsync(int processId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return false;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }

            await Task.Delay(100);
        }

        return true;
    }

    private static void StopProcessIfRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch { }
    }

    private LocalEnvironmentOfflinePackageService CreateOfflinePackageService(string assetRoot)
    {
        return new LocalEnvironmentOfflinePackageService(
            new TestPathService(_rootDirectory),
            new TestLogger(_rootDirectory),
            assetRootResolver: () => assetRoot
        );
    }

    private string CreateBundledLocalEnvironmentAssets(string? nodeSha256Override = null)
    {
        var assetRoot = Path.Combine(_rootDirectory, "bundled-local-environment");
        var nodeRoot = Path.Combine(assetRoot, "node");
        var cacheRoot = Path.Combine(assetRoot, "npm-cache", "_cacache", "content-v2", "sha512");
        Directory.CreateDirectory(nodeRoot);
        Directory.CreateDirectory(cacheRoot);

        var nodePath = Path.Combine(nodeRoot, "node-v22.12.0-x64.msi");
        File.WriteAllText(nodePath, "fake bundled node msi");
        File.WriteAllText(Path.Combine(cacheRoot, "cache-entry"), "fake codex tarball");

        var manifest = new LocalEnvironmentBundleManifest
        {
            Schema = 1,
            Runtime = "win-x64",
            GeneratedAt = DateTimeOffset.UtcNow,
            Node = new LocalEnvironmentBundleNodeManifest
            {
                Version = "v22.12.0",
                Architecture = "x64",
                FileName = "node/node-v22.12.0-x64.msi",
                Sha256 = nodeSha256Override ?? ComputeSha256(nodePath),
            },
            Codex = new LocalEnvironmentBundleCodexManifest
            {
                Version = "0.1.2",
                NpmCachePath = "npm-cache",
            },
        };
        File.WriteAllText(
            Path.Combine(assetRoot, "manifest.json"),
            JsonSerializer.Serialize(manifest, BundleJsonOptions)
        );
        return assetRoot;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

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
            CommandStarted.TrySetResult(true);
            return _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete(LocalEnvironmentProcessResult result)
        {
            _completion.TrySetResult(result);
        }
    }

    private sealed class ThrowingProcessRunner(Exception exception) : ILocalEnvironmentProcessRunner
    {
        public Task<LocalEnvironmentProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw exception;
        }
    }

    private sealed class TestPathService : IPathService
    {
        private readonly Exception? _ensureException;
        private readonly bool _createLogsDirectory;
        private readonly bool _createRuntimeDirectory;

        public TestPathService(
            string rootDirectory,
            string? logsDirectory = null,
            string? runtimeDirectory = null,
            Exception? ensureException = null,
            bool createLogsDirectory = true,
            bool createRuntimeDirectory = true
        )
        {
            Directories = new AppDirectories(
                CodexCliPlus.Core.Enums.AppDataMode.Installed,
                rootDirectory,
                logsDirectory ?? Path.Combine(rootDirectory, "logs"),
                Path.Combine(rootDirectory, "config"),
                Path.Combine(rootDirectory, "backend"),
                Path.Combine(rootDirectory, "cache"),
                Path.Combine(rootDirectory, "diagnostics"),
                runtimeDirectory ?? Path.Combine(rootDirectory, "runtime"),
                Path.Combine(rootDirectory, "config", "appsettings.json"),
                Path.Combine(rootDirectory, "config", "backend.yaml"),
                Path.Combine(rootDirectory, "persistence")
            );
            _ensureException = ensureException;
            _createLogsDirectory = createLogsDirectory;
            _createRuntimeDirectory = createRuntimeDirectory;
        }

        public AppDirectories Directories { get; }

        public Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_ensureException is not null)
            {
                throw _ensureException;
            }

            Directory.CreateDirectory(Directories.RootDirectory);
            if (_createLogsDirectory)
            {
                Directory.CreateDirectory(Directories.LogsDirectory);
            }

            Directory.CreateDirectory(Directories.ConfigDirectory);
            Directory.CreateDirectory(Directories.BackendDirectory);
            Directory.CreateDirectory(Directories.CacheDirectory);
            Directory.CreateDirectory(Directories.DiagnosticsDirectory);
            if (_createRuntimeDirectory)
            {
                Directory.CreateDirectory(Directories.RuntimeDirectory);
            }

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
