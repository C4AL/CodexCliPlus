namespace DesktopHost.Core.Abstractions.Processes;

public interface IManagedProcess : IDisposable
{
    int ProcessId { get; }

    bool HasExited { get; }

    int? ExitCode { get; }

    event EventHandler? Exited;

    Task StopAsync(CancellationToken cancellationToken = default);

    Task WaitForExitAsync(CancellationToken cancellationToken = default);
}
