using System.Diagnostics;
using CodexCliPlus.Core.Abstractions.LocalEnvironment;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models.LocalEnvironment;
using CodexCliPlus.Infrastructure.LocalEnvironment;

namespace CodexCliPlus.Tests.LocalEnvironment;

[Trait("Category", "LocalIntegration")]
public sealed class LocalDependencyHealthServiceTests
{
    [Fact]
    public async Task CheckAsyncMarksWslOptionalAndDoesNotLowerRequiredScore()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ProcessRunner.Respond("where.exe", "wsl", 1, "", "not found");

        var snapshot = await fixture.CreateService().CheckAsync();

        Assert.Equal(100, snapshot.ReadinessScore);
        Assert.Contains("必备环境已就绪", snapshot.Summary, StringComparison.Ordinal);
        var wsl = Assert.Single(snapshot.Items, item => item.Id == "wsl");
        Assert.Equal(LocalDependencyStatus.OptionalUnavailable, wsl.Status);
        Assert.Equal(LocalDependencySeverity.Optional, wsl.Severity);
    }

    [Fact]
    public async Task CheckAsyncUsesCodexInstallActionWhenNpmIsReadyButCodexIsMissing()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ProcessRunner.Respond("where.exe", "codex", 1, "", "not found");
        fixture.ExistingFiles.Clear();

        var snapshot = await fixture.CreateService().CheckAsync();

        var codex = Assert.Single(snapshot.Items, item => item.Id == "codex-cli");
        Assert.Equal(LocalDependencyStatus.Missing, codex.Status);
        Assert.Equal(LocalDependencyRepairActionIds.InstallCodexCli, codex.RepairActionId);
    }

    [Fact]
    public async Task CheckAsyncUsesNodeNpmInstallActionWhenNpmIsMissing()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ProcessRunner.Respond("where.exe", "node", 1, "", "not found");
        fixture.ProcessRunner.Respond("where.exe", "npm", 1, "", "not found");
        fixture.ProcessRunner.Respond("where.exe", "codex", 1, "", "not found");
        fixture.ExistingFiles.Clear();

        var snapshot = await fixture.CreateService().CheckAsync();

        var npm = Assert.Single(snapshot.Items, item => item.Id == "npm");
        var codex = Assert.Single(snapshot.Items, item => item.Id == "codex-cli");
        Assert.Equal(LocalDependencyRepairActionIds.InstallNodeNpm, npm.RepairActionId);
        Assert.Equal(LocalDependencyRepairActionIds.InstallNodeNpm, codex.RepairActionId);
    }

    [Fact]
    public async Task CheckAsyncOffersWingetRepairWhenWingetIsMissing()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ProcessRunner.Respond("where.exe", "winget", 1, "", "not found");

        var snapshot = await fixture.CreateService().CheckAsync();

        var winget = Assert.Single(snapshot.Items, item => item.Id == "winget");
        Assert.Equal(LocalDependencyStatus.Warning, winget.Status);
        Assert.Equal(LocalDependencyRepairActionIds.RepairWinget, winget.RepairActionId);
        var capability = Assert.Single(
            snapshot.RepairCapabilities,
            item => item.ActionId == LocalDependencyRepairActionIds.RepairWinget
        );
        Assert.True(capability.IsAvailable);
    }

    [Fact]
    public async Task CheckAsyncOffersWingetRepairWhenWingetVersionProbeFails()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ProcessRunner.Respond(
            "C:\\Users\\Tester\\AppData\\Local\\Microsoft\\WindowsApps\\winget.exe",
            "--version",
            1,
            "",
            "winget failed"
        );

        var snapshot = await fixture.CreateService().CheckAsync();

        var winget = Assert.Single(snapshot.Items, item => item.Id == "winget");
        Assert.Equal(LocalDependencyStatus.Warning, winget.Status);
        Assert.Equal(LocalDependencyRepairActionIds.RepairWinget, winget.RepairActionId);
        Assert.Contains("winget 版本检测失败", winget.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsyncDisablesWingetRepairCapabilityWhenPowerShellIsUnavailable()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ProcessRunner.Respond("where.exe", "pwsh", 1, "", "not found");
        fixture.ProcessRunner.Respond("where.exe", "powershell", 1, "", "not found");

        var snapshot = await fixture.CreateService().CheckAsync();

        var capability = Assert.Single(
            snapshot.RepairCapabilities,
            item => item.ActionId == LocalDependencyRepairActionIds.RepairWinget
        );
        Assert.False(capability.IsAvailable);
        Assert.Contains("需要 PowerShell 可用", capability.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsyncRecordsTimeoutAsWarning()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ProcessRunner.ThrowOn(
            "C:\\Program Files\\nodejs\\node.exe",
            "--version",
            new TimeoutException("node timed out")
        );

        var snapshot = await fixture.CreateService().CheckAsync();

        var node = Assert.Single(snapshot.Items, item => item.Id == "node");
        Assert.Equal(LocalDependencyStatus.Warning, node.Status);
        Assert.Contains("超时", node.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsyncPrefersWindowsExecutableExtensionsOverExtensionlessShims()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ProcessRunner.Respond(
            "where.exe",
            "npm",
            0,
            "C:\\Users\\Tester\\AppData\\Roaming\\npm\\npm"
                + Environment.NewLine
                + "C:\\Program Files\\nodejs\\npm.cmd"
        );
        fixture.ProcessRunner.Respond(
            "where.exe",
            "codex",
            0,
            "C:\\Users\\Tester\\AppData\\Roaming\\npm\\codex"
                + Environment.NewLine
                + "C:\\Users\\Tester\\AppData\\Roaming\\npm\\codex.cmd"
        );

        var snapshot = await fixture.CreateService().CheckAsync();

        var npm = Assert.Single(snapshot.Items, item => item.Id == "npm");
        var codex = Assert.Single(snapshot.Items, item => item.Id == "codex-cli");
        Assert.Equal("C:\\Program Files\\nodejs\\npm.cmd", npm.Path);
        Assert.Equal("C:\\Users\\Tester\\AppData\\Roaming\\npm\\codex.cmd", codex.Path);
        Assert.Equal(LocalDependencyStatus.Ready, npm.Status);
        Assert.Equal(LocalDependencyStatus.Ready, codex.Status);
    }

    [Fact]
    public async Task CheckAsyncDoesNotSelectExtensionlessCodexShim()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ProcessRunner.Respond(
            "where.exe",
            "codex",
            0,
            "C:\\Users\\Tester\\AppData\\Roaming\\npm\\codex"
        );
        fixture.ExistingFiles.Clear();

        var snapshot = await fixture.CreateService().CheckAsync();

        var codex = Assert.Single(snapshot.Items, item => item.Id == "codex-cli");
        Assert.Equal(LocalDependencyStatus.Missing, codex.Status);
        Assert.Equal(LocalDependencyRepairActionIds.InstallCodexCli, codex.RepairActionId);
    }

    [Fact]
    public async Task CheckAsyncTurnsSlowProbeIntoWarningWithinSnapshotBudget()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ProcessRunner.DelayOn(
            "C:\\Program Files\\nodejs\\node.exe",
            "--version",
            TimeSpan.FromSeconds(5)
        );
        var stopwatch = Stopwatch.StartNew();

        var snapshot = await fixture.CreateService().CheckAsync();

        stopwatch.Stop();
        var node = Assert.Single(snapshot.Items, item => item.Id == "node");
        Assert.Equal(LocalDependencyStatus.Warning, node.Status);
        Assert.Contains("超时", node.Detail, StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task CheckAsyncRunsIndependentProbesConcurrently()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        foreach (
            var command in new[] { "node", "npm", "codex", "pwsh", "powershell", "wsl", "winget" }
        )
        {
            fixture.ProcessRunner.DelayOn("where.exe", command, TimeSpan.FromMilliseconds(180));
        }
        var stopwatch = Stopwatch.StartNew();

        await fixture.CreateService().CheckAsync();

        stopwatch.Stop();
        Assert.True(fixture.ProcessRunner.MaxConcurrentRuns > 1);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CheckAsyncReportsRepairableUserPathWhenAppDataNpmIsMissingFromPath()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ProcessPath = "C:\\Program Files\\nodejs;C:\\Program Files\\PowerShell\\7";

        var snapshot = await fixture.CreateService().CheckAsync();

        var path = Assert.Single(snapshot.Items, item => item.Id == "path");
        Assert.Equal(LocalDependencyStatus.Warning, path.Status);
        Assert.Equal(LocalDependencyRepairActionIds.RepairUserPath, path.RepairActionId);
        Assert.Contains("%APPDATA%\\npm", path.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsyncReportsRepairableUserPathWhenUserPathHasDuplicateOrUnreachableEntries()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ExistingDirectories.Add("C:\\Tools");
        fixture.UserPath = "C:\\Tools;C:\\Tools;C:\\Missing;%NOT_EXPANDED%\\bin";

        var snapshot = await fixture.CreateService().CheckAsync();

        var path = Assert.Single(snapshot.Items, item => item.Id == "path");
        Assert.Equal(LocalDependencyStatus.Warning, path.Status);
        Assert.Equal(LocalDependencyRepairActionIds.RepairUserPath, path.RepairActionId);
        Assert.Contains("用户 PATH 发现 1 个重复目录", path.Detail, StringComparison.Ordinal);
        Assert.Contains("用户 PATH 发现 1 个不可达目录", path.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsyncDoesNotOfferUserPathRepairForOnlyMachinePathIssues()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ExistingDirectories.Add("C:\\MachineTools");
        fixture.MachinePath = "C:\\MachineTools;C:\\MachineTools;C:\\MissingMachine";

        var snapshot = await fixture.CreateService().CheckAsync();

        var path = Assert.Single(snapshot.Items, item => item.Id == "path");
        Assert.Equal(LocalDependencyStatus.Warning, path.Status);
        Assert.Null(path.RepairActionId);
        Assert.Contains("系统 PATH 发现 1 个重复目录", path.Detail, StringComparison.Ordinal);
        Assert.Contains("系统 PATH 发现 1 个不可达目录", path.Detail, StringComparison.Ordinal);
        var capability = Assert.Single(
            snapshot.RepairCapabilities,
            item => item.ActionId == LocalDependencyRepairActionIds.RepairUserPath
        );
        Assert.False(capability.IsAvailable);
    }

    [Fact]
    public async Task CheckAsyncUsesInstallWslWhenWslStatusReportsSubsystemNotInstalled()
    {
        var fixture = LocalEnvironmentFixture.CreateHealthy();
        fixture.ProcessRunner.Respond(
            "C:\\Windows\\System32\\wsl.exe",
            "--status",
            50,
            "未安装适用于 Linux 的 Windows 子系统。运行 wsl.exe --install。"
        );

        var snapshot = await fixture.CreateService().CheckAsync();

        Assert.Equal(100, snapshot.ReadinessScore);
        var wsl = Assert.Single(snapshot.Items, item => item.Id == "wsl");
        Assert.Equal(LocalDependencyStatus.OptionalUnavailable, wsl.Status);
        Assert.Equal(LocalDependencySeverity.Optional, wsl.Severity);
        Assert.Equal(LocalDependencyRepairActionIds.InstallWsl, wsl.RepairActionId);
        Assert.Contains("未安装适用于 Linux 的 Windows 子系统", wsl.Detail, StringComparison.Ordinal);
    }

    private sealed class LocalEnvironmentFixture
    {
        private const string AppData = "C:\\Users\\Tester\\AppData\\Roaming";
        private const string UserProfile = "C:\\Users\\Tester";
        public HashSet<string> ExistingDirectories { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "C:\\Users\\Tester\\AppData\\Roaming\\npm",
            "C:\\Program Files\\nodejs",
            "C:\\Program Files\\PowerShell\\7",
            "C:\\Users\\Tester\\AppData\\Local\\Microsoft\\WindowsApps",
        };

        public FakeProcessRunner ProcessRunner { get; } = new();

        public HashSet<string> ExistingFiles { get; } =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "C:\\Users\\Tester\\AppData\\Roaming\\npm\\codex.cmd",
                "C:\\Users\\Tester\\.codex\\auth.json",
            };

        public string ProcessPath { get; set; } =
            "C:\\Users\\Tester\\AppData\\Roaming\\npm;C:\\Program Files\\nodejs;C:\\Program Files\\PowerShell\\7;C:\\Users\\Tester\\AppData\\Local\\Microsoft\\WindowsApps";

        public string UserPath { get; set; } = string.Empty;

        public string MachinePath { get; set; } = string.Empty;

        public static LocalEnvironmentFixture CreateHealthy()
        {
            var fixture = new LocalEnvironmentFixture();
            fixture.ProcessRunner.Respond(
                "where.exe",
                "node",
                0,
                "C:\\Program Files\\nodejs\\node.exe"
            );
            fixture.ProcessRunner.Respond(
                "C:\\Program Files\\nodejs\\node.exe",
                "--version",
                0,
                "v22.12.0"
            );
            fixture.ProcessRunner.Respond(
                "where.exe",
                "npm",
                0,
                "C:\\Program Files\\nodejs\\npm.cmd"
            );
            fixture.ProcessRunner.Respond("cmd.exe", "npm.cmd --version", 0, "10.9.0");
            fixture.ProcessRunner.Respond(
                "cmd.exe",
                "npm.cmd config get prefix",
                0,
                "C:\\Users\\Tester\\AppData\\Roaming\\npm"
            );
            fixture.ProcessRunner.Respond(
                "where.exe",
                "codex",
                0,
                "C:\\Users\\Tester\\AppData\\Roaming\\npm\\codex.cmd"
            );
            fixture.ProcessRunner.Respond("cmd.exe", "codex.cmd --version", 0, "codex 0.9.0");
            fixture.ProcessRunner.Respond("cmd.exe", "codex.cmd login status", 0, "logged in");
            fixture.ProcessRunner.Respond(
                "where.exe",
                "pwsh",
                0,
                "C:\\Program Files\\PowerShell\\7\\pwsh.exe"
            );
            fixture.ProcessRunner.Respond(
                "where.exe",
                "powershell",
                0,
                "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe"
            );
            fixture.ProcessRunner.Respond(
                "C:\\Program Files\\PowerShell\\7\\pwsh.exe",
                "$PSVersionTable",
                0,
                "7.5.0"
            );
            fixture.ProcessRunner.Respond("where.exe", "wsl", 0, "C:\\Windows\\System32\\wsl.exe");
            fixture.ProcessRunner.Respond(
                "C:\\Windows\\System32\\wsl.exe",
                "--status",
                0,
                "默认版本: 2"
            );
            fixture.ProcessRunner.Respond("C:\\Windows\\System32\\wsl.exe", "-l -q", 0, "Ubuntu");
            fixture.ProcessRunner.Respond(
                "where.exe",
                "winget",
                0,
                "C:\\Users\\Tester\\AppData\\Local\\Microsoft\\WindowsApps\\winget.exe"
            );
            fixture.ProcessRunner.Respond(
                "C:\\Users\\Tester\\AppData\\Local\\Microsoft\\WindowsApps\\winget.exe",
                "--version",
                0,
                "v1.10.0"
            );
            return fixture;
        }

        public LocalDependencyHealthService CreateService()
        {
            return new LocalDependencyHealthService(
                ProcessRunner,
                name =>
                    name.ToUpperInvariant() switch
                    {
                        "APPDATA" => AppData,
                        "USERPROFILE" => UserProfile,
                        "PATH" => ProcessPath,
                        _ => null,
                    },
                target =>
                    target switch
                    {
                        EnvironmentVariableTarget.User => UserPath,
                        EnvironmentVariableTarget.Machine => MachinePath,
                        _ => string.Empty,
                    },
                path => ExistingFiles.Contains(path),
                path => ExistingDirectories.Contains(path),
                () => new DateTimeOffset(2026, 4, 30, 12, 0, 0, TimeSpan.Zero)
            );
        }
    }

    private sealed class FakeProcessRunner : ILocalEnvironmentProcessRunner
    {
        private readonly List<Response> _responses = [];
        private readonly List<Failure> _failures = [];
        private readonly List<Delay> _delays = [];
        private int _activeRuns;
        private int _maxConcurrentRuns;

        public int MaxConcurrentRuns => _maxConcurrentRuns;

        public void Respond(
            string fileName,
            string argumentsContains,
            int exitCode,
            string standardOutput,
            string standardError = ""
        )
        {
            _responses.RemoveAll(response =>
                string.Equals(response.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    response.ArgumentsContains,
                    argumentsContains,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            _responses.Add(
                new Response(fileName, argumentsContains, exitCode, standardOutput, standardError)
            );
        }

        public void ThrowOn(string fileName, string argumentsContains, Exception exception)
        {
            _failures.Add(new Failure(fileName, argumentsContains, exception));
        }

        public void DelayOn(string fileName, string argumentsContains, TimeSpan delay)
        {
            _delays.Add(new Delay(fileName, argumentsContains, delay));
        }

        public async Task<LocalEnvironmentProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default
        )
        {
            var active = Interlocked.Increment(ref _activeRuns);
            UpdateMaxConcurrentRuns(active);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var joinedArguments = string.Join(" ", arguments);
                var delay = _delays.LastOrDefault(candidate =>
                    Matches(candidate, fileName, joinedArguments)
                );
                if (delay is not null)
                {
                    await Task.Delay(delay.Duration, cancellationToken);
                }

                var failure = _failures.LastOrDefault(candidate =>
                    Matches(candidate, fileName, joinedArguments)
                );
                if (failure is not null)
                {
                    throw failure.Exception;
                }

                var response = _responses.LastOrDefault(candidate =>
                    Matches(candidate, fileName, joinedArguments)
                );
                if (response is null)
                {
                    return new LocalEnvironmentProcessResult(1, "", "not found");
                }

                return new LocalEnvironmentProcessResult(
                    response.ExitCode,
                    response.StandardOutput,
                    response.StandardError
                );
            }
            finally
            {
                Interlocked.Decrement(ref _activeRuns);
            }
        }

        private void UpdateMaxConcurrentRuns(int active)
        {
            while (true)
            {
                var current = _maxConcurrentRuns;
                if (active <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrentRuns, active, current) == current)
                {
                    return;
                }
            }
        }

        private static bool Matches(Response response, string fileName, string arguments)
        {
            return string.Equals(response.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                && arguments.Contains(
                    response.ArgumentsContains,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static bool Matches(Failure failure, string fileName, string arguments)
        {
            return string.Equals(failure.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                && arguments.Contains(
                    failure.ArgumentsContains,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static bool Matches(Delay delay, string fileName, string arguments)
        {
            return string.Equals(delay.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                && arguments.Contains(delay.ArgumentsContains, StringComparison.OrdinalIgnoreCase);
        }

        private sealed record Response(
            string FileName,
            string ArgumentsContains,
            int ExitCode,
            string StandardOutput,
            string StandardError
        );

        private sealed record Failure(
            string FileName,
            string ArgumentsContains,
            Exception Exception
        );

        private sealed record Delay(string FileName, string ArgumentsContains, TimeSpan Duration);
    }
}
