using System.Diagnostics;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Infrastructure.Codex;

namespace CodexCliPlus.Tests.Codex;

[Trait("Category", "Fast")]
public sealed class CodexLaunchServiceTests
{
    [Fact]
    public void LaunchInTerminalUsesPowerShellLiteralRepositoryPath()
    {
        var repositoryPath = @"C:\repo with spaces\$(Write-Output unsafe)'s";
        ProcessStartInfo? capturedStartInfo = null;
        var service = new CodexLaunchService(
            new CodexConfigService(),
            new NoopLogger(),
            startInfo => capturedStartInfo = startInfo
        );

        var result = service.LaunchInTerminal(CodexSourceKind.Official, repositoryPath);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(
            @"codex --profile official -C 'C:\repo with spaces\$(Write-Output unsafe)''s'",
            result.Command
        );
        Assert.NotNull(capturedStartInfo);
        var startInfo = capturedStartInfo!;
        var quotedRepositoryPath = @"'C:\repo with spaces\$(Write-Output unsafe)''s'";
        Assert.Equal("powershell.exe", startInfo.FileName);
        Assert.Equal(repositoryPath, startInfo.WorkingDirectory);
        Assert.True(startInfo.UseShellExecute);
        Assert.Contains(
            $"Set-Location -LiteralPath {quotedRepositoryPath}",
            startInfo.Arguments,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"codex --profile official -C {quotedRepositoryPath}",
            startInfo.Arguments,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("\"C:\\repo", startInfo.Arguments, StringComparison.Ordinal);
    }

    private sealed class NoopLogger : IAppLogger
    {
        public string LogFilePath => string.Empty;

        public void Info(string message) { }

        public void Warn(string message) { }

        public void LogError(string message, Exception? exception = null) { }
    }
}
