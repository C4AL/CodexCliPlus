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
        var result = await _whereRunner(cancellationToken);
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
