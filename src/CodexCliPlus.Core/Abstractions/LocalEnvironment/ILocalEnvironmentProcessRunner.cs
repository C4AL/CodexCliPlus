using CodexCliPlus.Core.Models.LocalEnvironment;

namespace CodexCliPlus.Core.Abstractions.LocalEnvironment;

public interface ILocalEnvironmentProcessRunner
{
    Task<LocalEnvironmentProcessResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    );
}

public sealed record LocalEnvironmentProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError
);
