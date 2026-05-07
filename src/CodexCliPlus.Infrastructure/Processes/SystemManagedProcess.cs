using System.Diagnostics;
using CodexCliPlus.Core.Abstractions.Processes;

namespace CodexCliPlus.Infrastructure.Processes;

public sealed class SystemManagedProcess : IManagedProcess
{
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FastExitWaitTimeout = TimeSpan.FromMilliseconds(2500);

    private readonly Process _process;

    public SystemManagedProcess(Process process)
    {
        _process = process;
    }

    public int ProcessId => _process.Id;

    public bool HasExited => _process.HasExited;

    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    public event EventHandler? Exited
    {
        add => _process.Exited += value;
        remove => _process.Exited -= value;
    }

    public void Dispose()
    {
        _process.Dispose();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(ManagedProcessStopOptions.Default, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(
        ManagedProcessStopOptions stopOptions,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_process.HasExited)
        {
            return;
        }

        if (stopOptions == ManagedProcessStopOptions.FastExit)
        {
            await KillProcessTreeAsync(FastExitWaitTimeout, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            if (_process.CloseMainWindow())
            {
                using var timeout = new CancellationTokenSource(GracefulStopTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeout.Token
                );
                await _process.WaitForExitAsync(linked.Token);
                return;
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Fall back to a hard kill after the graceful timeout.
        }

        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        return _process.WaitForExitAsync(cancellationToken);
    }

    private async Task KillProcessTreeAsync(
        TimeSpan waitTimeout,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(waitTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token
        );
        try
        {
            await _process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Fast application shutdown should not wait indefinitely after a hard kill request.
        }
    }
}
