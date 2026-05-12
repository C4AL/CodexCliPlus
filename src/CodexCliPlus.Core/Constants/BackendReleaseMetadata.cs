namespace CodexCliPlus.Core.Constants;

public static class BackendReleaseMetadata
{
    public const string Version = "7.0.3";

    public const string ReleaseTag = "v7.0.3";

    public const string AssetName = "CLIProxyAPI_7.0.3_windows_amd64.zip";

    public const string ReleaseUrl =
        "https://github.com/router-for-me/CLIProxyAPI/releases/tag/v7.0.3";

    public const string ArchiveUrl =
        "https://github.com/router-for-me/CLIProxyAPI/releases/download/v7.0.3/CLIProxyAPI_7.0.3_windows_amd64.zip";

    public const string ArchiveSha256 =
        "65f1657a900b50cbcbe14526b4a59753141b18e3b8f09a691648fc50ca601660";

    public const string SourceCommit = "bd8c05a830a36b2e5181bb5c8596a2c8d85c3dbc";

    public const string BundledExecutableSha256 =
        "71a56ba6a6e397f0c75c0c17e6ec818e9ccde42a9c5a695c4c2237179ab68d39";

    public static bool RemoteArchiveFallbackEnabled => false;
}
