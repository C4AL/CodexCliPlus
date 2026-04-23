using CPAD.Infrastructure.Utilities;

namespace CPAD.Infrastructure.Codex;

public sealed class CodexVersionReader
{
    public async Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var result = await ProcessCapture.RunAsync("cmd.exe", "/c codex --version", cancellationToken: cancellationToken);
        return result.ExitCode == 0 ? result.StandardOutput : null;
    }
}
