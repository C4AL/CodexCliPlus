namespace CodexCliPlus.Core.Abstractions.Processes;

public interface IManagedProcess : IDisposable
{
    int ProcessId { get; }

    bool HasExited { get; }

    int? ExitCode { get; }

    event EventHandler? Exited;

    Task StopAsync(CancellationToken cancellationToken = default);

    Task StopAsync(
        ManagedProcessStopOptions stopOptions,
        CancellationToken cancellationToken = default
    );

    Task WaitForExitAsync(CancellationToken cancellationToken = default);
}
