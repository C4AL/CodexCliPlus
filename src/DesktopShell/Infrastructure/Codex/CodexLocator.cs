using CodexCliPlus.Infrastructure.Utilities;

namespace CodexCliPlus.Infrastructure.Codex;

public sealed class CodexLocator
{
    private readonly Func<
        CancellationToken,
        Task<(int ExitCode, string StandardOutput, string StandardError)>
    > _whereRunner;
    private readonly Func<string> _npmFallbackPathProvider;

    public CodexLocator()
        : this(
            cancellationToken =>
                ProcessCapture.RunAsync("where.exe", "codex", cancellationToken: cancellationToken),
            () =>
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "npm",
                    "codex.cmd"
                )
        ) { }

    public CodexLocator(
        Func<
            CancellationToken,
            Task<(int ExitCode, string StandardOutput, string StandardError)>
        > whereRunner,
        Func<string> npmFallbackPathProvider
    )
    {
        _whereRunner = whereRunner;
        _npmFallbackPathProvider = npmFallbackPathProvider;
    }

    public async Task<string?> LocateAsync(CancellationToken cancellationToken = default)
    {
        (int ExitCode, string StandardOutput, string StandardError) result;
        try
        {
            result = await _whereRunner(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            result = (1, string.Empty, string.Empty);
        }

        if (result.ExitCode == 0)
        {
            var resolvedPath = PickExecutablePath(SplitLines(result.StandardOutput));
            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                return resolvedPath;
            }
        }

        var appDataNpm = _npmFallbackPathProvider();

        return File.Exists(appDataNpm) ? appDataNpm : null;
    }

    private static string? PickExecutablePath(IEnumerable<string> candidates)
    {
        return candidates
            .OrderBy(GetExecutablePreference)
            .FirstOrDefault(candidate => GetExecutablePreference(candidate) < int.MaxValue);
    }

    private static string[] SplitLines(string value)
    {
        return value.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
    }

    private static int GetExecutablePreference(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return int.MaxValue;
    }
}
