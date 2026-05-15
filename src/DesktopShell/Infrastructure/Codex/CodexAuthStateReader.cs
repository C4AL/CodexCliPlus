using System.Diagnostics.CodeAnalysis;
using CodexCliPlus.Infrastructure.Utilities;

namespace CodexCliPlus.Infrastructure.Codex;

public sealed class CodexAuthStateReader
{
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Instance member is resolved through dependency injection."
    )]
    public async Task<string> ReadAsync(CancellationToken cancellationToken = default)
    {
        var result = await ProcessCapture.RunAsync(
            "cmd.exe",
            "/c codex login status",
            cancellationToken: cancellationToken
        );

        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return result.StandardOutput;
        }

        return string.IsNullOrWhiteSpace(result.StandardError)
            ? "Authentication status is unavailable."
            : result.StandardError;
    }
}
