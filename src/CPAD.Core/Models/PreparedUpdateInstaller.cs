using CPAD.Core.Enums;

namespace CPAD.Core.Models;

public sealed class PreparedUpdateInstaller
{
    public string InstallerPath { get; init; } = string.Empty;

    public string CacheDirectory { get; init; } = string.Empty;

    public string? Version { get; init; }

    public bool UsedCachedFile { get; init; }

    public bool DigestValidated { get; init; }

    public AppDataMode DataMode { get; init; } = AppDataMode.Installed;

    public UpdateReleaseAsset Asset { get; init; } = new();
}
