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
            var outputTask = ReadToEndAsync(process.StandardOutput.BaseStream, timeoutCts.Token);
            var errorTask = ReadToEndAsync(process.StandardError.BaseStream, timeoutCts.Token);
            await Task.WhenAll(process.WaitForExitAsync(timeoutCts.Token), outputTask, errorTask);
            return new LocalEnvironmentProcessResult(
                process.ExitCode,
                DecodeProcessOutput(outputTask.Result).Trim(),
                DecodeProcessOutput(errorTask.Result).Trim()
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

    internal static string DecodeProcessOutput(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        if (HasPrefix(bytes, [0xef, 0xbb, 0xbf]))
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (HasPrefix(bytes, [0xff, 0xfe]))
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (HasPrefix(bytes, [0xfe, 0xff]))
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (LooksLikeUtf16LittleEndian(bytes))
        {
            return Encoding.Unicode.GetString(bytes);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static async Task<byte[]> ReadToEndAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static bool HasPrefix(byte[] bytes, ReadOnlySpan<byte> prefix)
    {
        return bytes.AsSpan().StartsWith(prefix);
    }

    private static bool LooksLikeUtf16LittleEndian(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes.Length % 2 != 0)
        {
            return false;
        }

        var pairCount = bytes.Length / 2;
        var oddZeroCount = 0;
        var evenZeroCount = 0;
        for (var index = 0; index < bytes.Length; index += 2)
        {
            if (bytes[index] == 0)
            {
                evenZeroCount++;
            }

            if (bytes[index + 1] == 0)
            {
                oddZeroCount++;
            }
        }

        return oddZeroCount >= Math.Max(2, pairCount / 8)
            && oddZeroCount > evenZeroCount * 2;
    }
}
