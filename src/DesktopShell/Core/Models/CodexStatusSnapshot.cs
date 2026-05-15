namespace CodexCliPlus.Core.Models;

public sealed class CodexStatusSnapshot
{
    public bool IsInstalled { get; init; }

    public string? ExecutablePath { get; init; }

    public string? Version { get; init; }

    public string DefaultProfile { get; init; } = "official";

    public bool HasUserConfig { get; init; }

    public bool HasProjectConfig { get; init; }

    public string AuthenticationState { get; init; } = "Unknown";

    public string EffectiveSource { get; init; } = "official";

    public string? ErrorMessage { get; init; }
}
