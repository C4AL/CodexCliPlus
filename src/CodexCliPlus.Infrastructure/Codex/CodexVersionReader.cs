using System.Diagnostics.CodeAnalysis;

using CodexCliPlus.Infrastructure.Utilities;

namespace CodexCliPlus.Infrastructure.Codex;

public sealed class CodexVersionReader
{
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance member is resolved through dependency injection.")]
    public async Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var result = await ProcessCapture.RunAsync("cmd.exe", "/c codex --version", cancellationToken: cancellationToken);
        return result.ExitCode == 0 ? result.StandardOutput : null;
    }
}
