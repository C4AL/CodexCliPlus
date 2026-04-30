namespace CodexCliPlus.Core.Constants;

public static class BackendReleaseMetadata
{
    public const string Version = "6.9.45";

    public const string ReleaseTag = "v6.9.45";

    public const string AssetName = "CLIProxyAPI_6.9.45_windows_amd64.zip";

    public const string ReleaseUrl =
        "https://github.com/router-for-me/CLIProxyAPI/releases/tag/v6.9.45";

    public const string ArchiveUrl =
        "https://github.com/router-for-me/CLIProxyAPI/releases/download/v6.9.45/CLIProxyAPI_6.9.45_windows_amd64.zip";

    public const string ArchiveSha256 =
        "0fdf0122a98a54cdeaa8d368d364fa824d4fe4d6084d1d3d982dca1a34133cd0";

    public const string SourceCommit = "6ba7c810a78c9afa88550a80b90c48b24e8b4852";

    public const string BundledExecutableSha256 =
        "56c7f125e8304065eb03df444a56cbedeb408182efa4decc2d435dadeae18265";

    public static bool RemoteArchiveFallbackEnabled => false;
}
