using System.Diagnostics;
using CodexCliPlus.Infrastructure.Utilities;

namespace CodexCliPlus.Tests.Utilities;

[Trait("Category", "LocalIntegration")]
public sealed class ProcessCaptureTests
{
    [Fact]
    public async Task RunAsyncPropagatesPreCanceledTokenBeforeStartingProcess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Record.ExceptionAsync(() =>
            ProcessCapture.RunAsync(
                "codexcliplus-missing-process.exe",
                "--version",
                cancellationToken: cancellation.Token
            )
        );

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task RunAsyncCancellationWaitsForProcessExit()
    {
        var pidPath = Path.Combine(
            Path.GetTempPath(),
            $"codexcliplus-process-capture-{Guid.NewGuid():N}.pid"
        );
        var escapedPidPath = pidPath.Replace("'", "''", StringComparison.Ordinal);
        int? processId = null;
        using var cancellation = new CancellationTokenSource();

        try
        {
            var runTask = ProcessCapture.RunAsync(
                "powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command "
                    + $"\"Set-Content -LiteralPath '{escapedPidPath}' -Value $PID -Encoding ascii; Start-Sleep -Seconds 60\"",
                cancellationToken: cancellation.Token
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
