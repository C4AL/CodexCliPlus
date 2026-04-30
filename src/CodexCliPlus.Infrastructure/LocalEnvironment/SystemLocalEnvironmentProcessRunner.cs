using System.Diagnostics;
using System.Text;
using CodexCliPlus.Core.Abstractions.LocalEnvironment;

namespace CodexCliPlus.Infrastructure.LocalEnvironment;

public sealed class SystemLocalEnvironmentProcessRunner : ILocalEnvironmentProcessRunner
{
    public async Task<LocalEnvironmentProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        }

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await Task.WhenAll(process.WaitForExitAsync(timeoutCts.Token), outputTask, errorTask);
            return new LocalEnvironmentProcessResult(
                process.ExitCode,
                outputTask.Result.Trim(),
                errorTask.Result.Trim()
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw new TimeoutException(
                $"Process '{fileName} {string.Join(" ", arguments)}' exceeded {timeout.TotalSeconds:0.#} seconds."
            );
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }
}
