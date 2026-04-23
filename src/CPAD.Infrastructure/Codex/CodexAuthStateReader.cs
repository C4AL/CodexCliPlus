using CPAD.Infrastructure.Utilities;

namespace CPAD.Infrastructure.Codex;

public sealed class CodexAuthStateReader
{
    public async Task<string> ReadAsync(CancellationToken cancellationToken = default)
    {
        var result = await ProcessCapture.RunAsync(
            "cmd.exe",
            "/c codex login status",
            cancellationToken: cancellationToken);

        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return result.StandardOutput;
        }

        return string.IsNullOrWhiteSpace(result.StandardError)
            ? "Authentication status is unavailable."
            : result.StandardError;
    }
}
