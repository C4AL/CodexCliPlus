using System.Diagnostics;

namespace CodexCliPlus.OfflineBuilder;

internal sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

internal interface IOfflineBuilderProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default
    );
}

internal sealed class OfflineBuilderProcessRunner : IOfflineBuilderProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                if (value is null)
                {
                    startInfo.Environment.Remove(name);
                }
                else
                {
                    startInfo.Environment[name] = value;
                }
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var shouldEchoOutput = ShouldEchoOutput(arguments);
        if (shouldEchoOutput && !string.IsNullOrWhiteSpace(stdout))
        {
            Console.Out.Write(stdout);
        }

        if (shouldEchoOutput && !string.IsNullOrWhiteSpace(stderr))
        {
            if (process.ExitCode == 0)
            {
                Console.Out.Write(stderr);
            }
            else
            {
                Console.Error.Write(stderr);
            }
        }

        return new ProcessRunResult(process.ExitCode, stdout, stderr);
    }

    private static bool ShouldEchoOutput(IReadOnlyList<string> arguments)
    {
        if (arguments.SequenceEqual(["--version"], StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (arguments.SequenceEqual(["version"], StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (arguments.SequenceEqual(["env", "GOROOT"], StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
