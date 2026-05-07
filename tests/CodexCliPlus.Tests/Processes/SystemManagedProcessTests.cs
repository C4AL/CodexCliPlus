using System.Diagnostics;
using CodexCliPlus.Core.Abstractions.Processes;
using CodexCliPlus.Infrastructure.Processes;

namespace CodexCliPlus.Tests.Processes;

[Trait("Category", "LocalIntegration")]
public sealed class SystemManagedProcessTests
{
    [Fact]
    public async Task StopAsyncFastExitKillsOwnedProcessTree()
    {
        var childPidPath = Path.Combine(
            Path.GetTempPath(),
            $"codexcliplus-child-pid-{Guid.NewGuid():N}.txt"
        );
        Process? parent = null;
        int? childPid = null;

        try
        {
            var escapedPath = childPidPath.Replace("'", "''", StringComparison.Ordinal);
            parent = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        "-NoProfile -ExecutionPolicy Bypass -Command "
                        + "\"$child = Start-Process -FilePath powershell.exe "
                        + "-ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 60' "
                        + "-PassThru; "
                        + $"Set-Content -LiteralPath '{escapedPath}' -Value $child.Id -Encoding ascii; "
                        + "Start-Sleep -Seconds 60\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            );
            Assert.NotNull(parent);

            childPid = await WaitForChildPidAsync(childPidPath);
            using var managedProcess = new SystemManagedProcess(parent);

            await managedProcess.StopAsync(ManagedProcessStopOptions.FastExit);

            Assert.True(parent.HasExited);
            Assert.False(await ProcessExistsAsync(childPid.Value));
        }
        finally
        {
            StopProcessIfRunning(parent);
            if (childPid is { } pid)
            {
                StopProcessIfRunning(pid);
            }

            if (File.Exists(childPidPath))
            {
                File.Delete(childPidPath);
            }
        }
    }

    [Fact]
    public async Task StopAsyncFastExitDoesNotWaitForCloseMainWindowGracePeriod()
    {
        Process? process = null;

        try
        {
            process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "
                        + "\"Add-Type -AssemblyName System.Windows.Forms; "
                        + "$form = New-Object System.Windows.Forms.Form; "
                        + "$form.Text = 'CodexCliPlusStopTest'; "
                        + "$form.Width = 160; $form.Height = 90; "
                        + "$form.StartPosition = 'Manual'; $form.Left = -32000; $form.Top = -32000; "
                        + "$form.Add_FormClosing({ param($sender, $eventArgs) $eventArgs.Cancel = $true }); "
                        + "[System.Windows.Forms.Application]::Run($form)\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            );
            Assert.NotNull(process);

            await WaitForMainWindowAsync(process);
            using var managedProcess = new SystemManagedProcess(process);

            var stopwatch = Stopwatch.StartNew();
            await managedProcess.StopAsync(ManagedProcessStopOptions.FastExit);
            stopwatch.Stop();

            Assert.True(process.HasExited);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(2500),
                $"Fast exit took {stopwatch.Elapsed.TotalMilliseconds:F0}ms."
            );
        }
        finally
        {
            StopProcessIfRunning(process);
        }
    }

    private static async Task WaitForMainWindowAsync(Process process)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            process.Refresh();
            if (process.HasExited)
            {
                break;
            }

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Process main window was not created.");
    }

    private static async Task<int> WaitForChildPidAsync(string path)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                var text = await TryReadSharedTextAsync(path);
                if (int.TryParse(text?.Trim(), out var pid))
                {
                    return pid;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Child process PID file was not created.");
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

    private static void StopProcessIfRunning(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch { }
        finally
        {
            process.Dispose();
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
