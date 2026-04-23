namespace DesktopHost.Core.Abstractions.Processes;

public interface IProcessService
{
    Task<IManagedProcess> StartAsync(
        ManagedProcessStartInfo startInfo,
        Action<string>? standardOutput = null,
        Action<string>? standardError = null,
        CancellationToken cancellationToken = default);
}
