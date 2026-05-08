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
            return result
                .StandardOutput.Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .FirstOrDefault();
        }

        var appDataNpm = _npmFallbackPathProvider();

        return File.Exists(appDataNpm) ? appDataNpm : null;
    }
}
