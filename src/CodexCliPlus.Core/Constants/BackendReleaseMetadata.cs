namespace CodexCliPlus.Core.Constants;

public static class BackendReleaseMetadata
{
    public const string Version = "6.9.41";

    public const string ReleaseTag = "v6.9.41";

    public const string AssetName = "CLIProxyAPI_6.9.41_windows_amd64.zip";

    public const string ReleaseUrl =
        "https://github.com/router-for-me/CLIProxyAPI/releases/tag/v6.9.41";

    public const string ArchiveUrl =
        "https://github.com/router-for-me/CLIProxyAPI/releases/download/v6.9.41/CLIProxyAPI_6.9.41_windows_amd64.zip";

    public const string ArchiveSha256 =
        "0cc1f2a7c314d739e844eca64ab6866a1c7246a3b72b15891cc94fedfb38d7d0";

    public const string SourceCommit = "c4965befe726c2d3ccf6223fa024526d8ff803f6";

    public const string BundledExecutableSha256 =
        "e5d1a6f094ece822afb98d73c147c52d7901249f7b1e102a035edac24a9a6cea";

    public static bool RemoteArchiveFallbackEnabled => false;
}
