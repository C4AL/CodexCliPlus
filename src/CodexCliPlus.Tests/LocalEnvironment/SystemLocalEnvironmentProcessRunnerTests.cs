using System.Diagnostics;
using System.Text;
using CodexCliPlus.Infrastructure.LocalEnvironment;

namespace CodexCliPlus.Tests.LocalEnvironment;

[Trait("Category", "LocalIntegration")]
public sealed class SystemLocalEnvironmentProcessRunnerTests
{
    [Fact]
    public async Task RunAsyncPropagatesPreCanceledTokenBeforeStartingProcess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new SystemLocalEnvironmentProcessRunner();

        var exception = await Record.ExceptionAsync(() =>
            runner.RunAsync(
                "codexcliplus-missing-process.exe",
                [],
                TimeSpan.FromMinutes(1),
                cancellation.Token
            )
        );

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task RunAsyncCancellationWaitsForProcessExit()
    {
        var pidPath = Path.Combine(
            Path.GetTempPath(),
            $"codexcliplus-local-runner-{Guid.NewGuid():N}.pid"
        );
        var escapedPidPath = pidPath.Replace("'", "''", StringComparison.Ordinal);
        int? processId = null;
        using var cancellation = new CancellationTokenSource();
        var runner = new SystemLocalEnvironmentProcessRunner();

        try
        {
            var runTask = runner.RunAsync(
                "powershell.exe",
                [
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    $"Set-Content -LiteralPath '{escapedPidPath}' -Value $PID -Encoding ascii; Start-Sleep -Seconds 60",
                ],
                TimeSpan.FromMinutes(1),
                cancellation.Token
            );
            processId = await WaitForPidAsync(pidPath);

            cancellation.Cancel();

            var exception = await Record.ExceptionAsync(async () => await runTask);

            Assert.IsAssignableFrom<OperationCanceledException>(exception);
            Assert.False(ProcessExistsNow(processId.Value));
        }
        finally
        {
            if (processId is { } pid)
            {
                StopProcessIfRunning(pid);
            }

            if (File.Exists(pidPath))
            {
                File.Delete(pidPath);
            }
        }
    }

    [Fact]
    public void DecodeProcessOutputDetectsUtf16LittleEndianWithoutBom()
    {
        const string output = "未安装适用于 Linux 的 Windows 子系统。运行 wsl.exe --install。";
        var bytes = Encoding.Unicode.GetBytes(output);

        var decoded = SystemLocalEnvironmentProcessRunner.DecodeProcessOutput(bytes);

        Assert.Equal(output, decoded);
    }

    [Fact]
    public void DecodeProcessOutputKeepsUtf8Output()
    {
        const string output = "node v22.12.0";
        var bytes = Encoding.UTF8.GetBytes(output);

        var decoded = SystemLocalEnvironmentProcessRunner.DecodeProcessOutput(bytes);

        Assert.Equal(output, decoded);
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

    private static bool ProcessExistsNow(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
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
}
