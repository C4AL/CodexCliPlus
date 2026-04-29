using System.Diagnostics;
using CodexCliPlus.Core.Abstractions.Processes;

namespace CodexCliPlus.Infrastructure.Processes;

public sealed class SystemManagedProcess : IManagedProcess
{
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
        cancellationToken.ThrowIfCancellationRequested();

        if (_process.HasExited)
        {
            return;
        }

        try
        {
            if (_process.CloseMainWindow())
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
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
            await _process.WaitForExitAsync(cancellationToken);
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        return _process.WaitForExitAsync(cancellationToken);
    }
}
