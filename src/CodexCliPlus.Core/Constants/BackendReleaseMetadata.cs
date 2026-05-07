namespace CodexCliPlus.Core.Constants;

public static class BackendReleaseMetadata
{
    public const string Version = "6.10.9";

    public const string ReleaseTag = "v6.10.9";

    public const string AssetName = "CLIProxyAPI_6.10.9_windows_amd64.zip";

    public const string ReleaseUrl =
        "https://github.com/router-for-me/CLIProxyAPI/releases/tag/v6.10.9";

    public const string ArchiveUrl =
        "https://github.com/router-for-me/CLIProxyAPI/releases/download/v6.10.9/CLIProxyAPI_6.10.9_windows_amd64.zip";

    public const string ArchiveSha256 =
        "1da9061b7620aafd07b05fb95461065a935c5948378dc14dd5090797e16fd50c";

    public const string SourceCommit = "785b00c3127eea6aa207f1207ead8a2aa93690a3";

    public const string BundledExecutableSha256 =
        "f17d22d4fb7df11c84b06b1fe2123e45561bdc6f14226a3f3cffa344491d4023";

    public static bool RemoteArchiveFallbackEnabled => false;
}
