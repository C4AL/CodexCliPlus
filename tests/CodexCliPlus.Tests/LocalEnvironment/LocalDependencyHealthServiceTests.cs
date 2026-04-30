using CodexCliPlus.Core.Abstractions.LocalEnvironment;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models.LocalEnvironment;
using CodexCliPlus.Infrastructure.LocalEnvironment;

namespace CodexCliPlus.Tests.LocalEnvironment;

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

    private sealed class LocalEnvironmentFixture
    {
        private const string AppData = "C:\\Users\\Tester\\AppData\\Roaming";
        private const string UserProfile = "C:\\Users\\Tester";
        private readonly HashSet<string> _existingDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "C:\\Users\\Tester\\AppData\\Roaming\\npm",
            "C:\\Program Files\\nodejs",
            "C:\\Program Files\\PowerShell\\7",
            "C:\\Users\\Tester\\AppData\\Local\\Microsoft\\WindowsApps",
        };

        public FakeProcessRunner ProcessRunner { get; } = new();

        public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "C:\\Users\\Tester\\AppData\\Roaming\\npm\\codex.cmd",
            "C:\\Users\\Tester\\.codex\\auth.json",
        };

        public string ProcessPath { get; set; } =
            "C:\\Users\\Tester\\AppData\\Roaming\\npm;C:\\Program Files\\nodejs;C:\\Program Files\\PowerShell\\7;C:\\Users\\Tester\\AppData\\Local\\Microsoft\\WindowsApps";

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
            fixture.ProcessRunner.Respond(
                "where.exe",
                "wsl",
                0,
                "C:\\Windows\\System32\\wsl.exe"
            );
            fixture.ProcessRunner.Respond("C:\\Windows\\System32\\wsl.exe", "--status", 0, "默认版本: 2");
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
                _ => string.Empty,
                path => ExistingFiles.Contains(path),
                path => _existingDirectories.Contains(path),
                () => new DateTimeOffset(2026, 4, 30, 12, 0, 0, TimeSpan.Zero)
            );
        }
    }

    private sealed class FakeProcessRunner : ILocalEnvironmentProcessRunner
    {
        private readonly List<Response> _responses = [];
        private readonly List<Failure> _failures = [];

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

        public Task<LocalEnvironmentProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var joinedArguments = string.Join(" ", arguments);
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
                return Task.FromResult(new LocalEnvironmentProcessResult(1, "", "not found"));
            }

            return Task.FromResult(
                new LocalEnvironmentProcessResult(
                    response.ExitCode,
                    response.StandardOutput,
                    response.StandardError
                )
            );
        }

        private static bool Matches(Response response, string fileName, string arguments)
        {
            return string.Equals(response.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                && arguments.Contains(response.ArgumentsContains, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Matches(Failure failure, string fileName, string arguments)
        {
            return string.Equals(failure.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                && arguments.Contains(failure.ArgumentsContains, StringComparison.OrdinalIgnoreCase);
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
    }
}
