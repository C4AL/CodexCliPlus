using System.Diagnostics;

using DesktopHost.Core.Abstractions.Processes;

namespace DesktopHost.Infrastructure.Processes;

public sealed class SystemProcessService : IProcessService
{
    public Task<IManagedProcess> StartAsync(
        ManagedProcessStartInfo startInfo,
        Action<string>? standardOutput = null,
        Action<string>? standardError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startInfo.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(startInfo.WorkingDirectory);

        cancellationToken.ThrowIfCancellationRequested();

        var processStartInfo = new ProcessStartInfo
        {
            FileName = startInfo.FileName,
            Arguments = startInfo.Arguments,
            WorkingDirectory = startInfo.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = startInfo.CaptureOutput,
            RedirectStandardError = startInfo.CaptureOutput,
            CreateNoWindow = startInfo.CreateNoWindow
        };

        if (startInfo.EnvironmentVariables is not null)
        {
            foreach (var pair in startInfo.EnvironmentVariables)
            {
                processStartInfo.Environment[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        var process = new Process
        {
            StartInfo = processStartInfo,
            EnableRaisingEvents = true
        };

        if (startInfo.CaptureOutput)
        {
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    standardOutput?.Invoke(eventArgs.Data);
                }
            };

            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    standardError?.Invoke(eventArgs.Data);
                }
            };
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{startInfo.FileName}'.");
        }

        if (startInfo.CaptureOutput)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        IManagedProcess managedProcess = new SystemManagedProcess(process);
        return Task.FromResult(managedProcess);
    }
}
