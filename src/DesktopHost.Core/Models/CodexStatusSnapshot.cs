namespace DesktopHost.Core.Models;

public sealed class CodexStatusSnapshot
{
    public bool IsInstalled { get; init; }

    public string? ExecutablePath { get; init; }

    public string? Version { get; init; }

    public string DefaultProfile { get; init; } = "official";

    public bool HasUserConfig { get; init; }

    public bool HasProjectConfig { get; init; }

    public string AuthenticationState { get; init; } = "未检测";

    public string EffectiveSource { get; init; } = "official";

    public string? ErrorMessage { get; init; }
}
