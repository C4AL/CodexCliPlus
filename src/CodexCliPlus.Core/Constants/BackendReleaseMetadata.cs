namespace CodexCliPlus.Core.Constants;

public static class BackendReleaseMetadata
{
    public const string Version = "6.10.1";

    public const string ReleaseTag = "v6.10.1";

    public const string AssetName = "CLIProxyAPI_6.10.1_windows_amd64.zip";

    public const string ReleaseUrl =
        "https://github.com/router-for-me/CLIProxyAPI/releases/tag/v6.10.1";

    public const string ArchiveUrl =
        "https://github.com/router-for-me/CLIProxyAPI/releases/download/v6.10.1/CLIProxyAPI_6.10.1_windows_amd64.zip";

    public const string ArchiveSha256 =
        "6879be857337adbf2ce5ba03b7f62bccedb14c266efd2cb6c02a4719a653ac13";

    public const string SourceCommit = "56df36895a0ed21720a3aa315f5b394f8b20b1b3";

    public const string BundledExecutableSha256 =
        "1f21a568661d20006a9e8998d5051bba3d03241957a2187b8dc0cda66990d985";

    public static bool RemoteArchiveFallbackEnabled => false;
}
