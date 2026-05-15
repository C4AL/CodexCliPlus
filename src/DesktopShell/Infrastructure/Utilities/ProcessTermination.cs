using System.ComponentModel;
using System.Diagnostics;

namespace CodexCliPlus.Infrastructure.Utilities;

internal static class ProcessTermination
{
    private static readonly TimeSpan TerminationWaitTimeout = TimeSpan.FromSeconds(5);

    public static async Task KillProcessTreeAndWaitAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (Win32Exception)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TerminationWaitTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
    }
}
