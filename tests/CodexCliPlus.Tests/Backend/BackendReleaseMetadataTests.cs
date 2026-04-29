using System.Reflection;
using System.Text;
using System.Text.Json;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Infrastructure.Backend;

namespace CodexCliPlus.Tests.Backend;

public sealed class BackendReleaseMetadataTests
{
    [Fact]
    public void BackendReleaseMetadataPinsCliProxyApi6941WindowsAsset()
    {
        Assert.Equal("6.9.41", BackendReleaseMetadata.Version);
        Assert.Equal("v6.9.41", BackendReleaseMetadata.ReleaseTag);
        Assert.Equal("CLIProxyAPI_6.9.41_windows_amd64.zip", BackendReleaseMetadata.AssetName);
        Assert.Equal(
            "https://github.com/router-for-me/CLIProxyAPI/releases/tag/v6.9.41",
            BackendReleaseMetadata.ReleaseUrl
        );
        Assert.Equal(
            "https://github.com/router-for-me/CLIProxyAPI/releases/download/v6.9.41/CLIProxyAPI_6.9.41_windows_amd64.zip",
            BackendReleaseMetadata.ArchiveUrl
        );
        Assert.Equal(
            "0cc1f2a7c314d739e844eca64ab6866a1c7246a3b72b15891cc94fedfb38d7d0",
            BackendReleaseMetadata.ArchiveSha256
        );
        Assert.Equal(
            "c4965befe726c2d3ccf6223fa024526d8ff803f6",
            BackendReleaseMetadata.SourceCommit
        );
        Assert.Equal(
            "e5d1a6f094ece822afb98d73c147c52d7901249f7b1e102a035edac24a9a6cea",
            BackendReleaseMetadata.BundledExecutableSha256
        );
        Assert.False(BackendReleaseMetadata.RemoteArchiveFallbackEnabled);
    }

    [Fact]
    public void BackendExecutableNamesSeparateUpstreamArchiveEntryFromManagedRuntimeName()
    {
        Assert.Equal("cli-proxy-api.exe", BackendExecutableNames.UpstreamExecutableFileName);
        Assert.Equal("ccp-core.exe", BackendExecutableNames.ManagedExecutableFileName);
    }

    [Fact]
    public void BackendSourceManifestMatchesPinnedRuntimeMetadata()
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifestPath = Path.Combine(
            repositoryRoot,
            "resources",
            "backend",
            "windows-x64",
            "source-manifest.json"
        );
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
        var root = document.RootElement;

        Assert.Equal(BackendReleaseMetadata.Version, root.GetProperty("version").GetString());
        Assert.Equal(BackendReleaseMetadata.ReleaseTag, root.GetProperty("releaseTag").GetString());
        Assert.Equal(BackendReleaseMetadata.ArchiveUrl, root.GetProperty("archiveUrl").GetString());
        Assert.Equal(
            BackendReleaseMetadata.ArchiveSha256,
            root.GetProperty("archiveSha256").GetString()
        );
        Assert.Equal(
            BackendReleaseMetadata.SourceCommit,
            root.GetProperty("sourceCommit").GetString()
        );
        Assert.Equal(
            BackendReleaseMetadata.BundledExecutableSha256,
            root.GetProperty("managedExecutableSha256").GetString()
        );
        Assert.Equal(
            BackendReleaseMetadata.RemoteArchiveFallbackEnabled,
            root.GetProperty("remoteArchiveFallbackEnabled").GetBoolean()
        );
        Assert.Equal(
            BackendExecutableNames.UpstreamExecutableFileName,
            root.GetProperty("upstreamExecutableName").GetString()
        );
        Assert.Equal(
            BackendExecutableNames.ManagedExecutableFileName,
            root.GetProperty("managedExecutableName").GetString()
        );
    }

    [Fact]
    public void RuntimeAndBuildToolUseSharedBackendReleaseMetadata()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtimeSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.Infrastructure",
                "Backend",
                "BackendAssetService.cs"
            ),
            Encoding.UTF8
        );
        var buildToolSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.BuildTool", "Program.cs"),
            Encoding.UTF8
        );

        Assert.Contains("BackendReleaseMetadata.Version", runtimeSource, StringComparison.Ordinal);
        Assert.Contains(
            "BackendReleaseMetadata.ArchiveUrl",
            runtimeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "BackendReleaseMetadata.ArchiveSha256",
            runtimeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "BackendReleaseMetadata.RemoteArchiveFallbackEnabled",
            runtimeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "BackendReleaseMetadata.ArchiveUrl",
            buildToolSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "BackendReleaseMetadata.ArchiveSha256",
            buildToolSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "BackendReleaseMetadata.RemoteArchiveFallbackEnabled",
            buildToolSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "BackendExecutableNames.ManagedExecutableFileName",
            runtimeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "BackendExecutableNames.UpstreamExecutableFileName",
            runtimeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "BackendExecutableNames.ManagedExecutableFileName",
            buildToolSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "BackendExecutableNames.UpstreamExecutableFileName",
            buildToolSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("6.9.34", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("6.9.34", buildToolSource, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendAssetServiceParsesAndRejectsCliProxyApiVersions()
    {
        Assert.Equal("6.9.41", InvokeParseBackendVersion("CLIProxyAPI Version: 6.9.41"));
        Assert.Equal(
            "6.9.41",
            InvokeParseBackendVersion("CLIProxyAPI Version: v6.9.41\r\nBuild Date: 2026-04-29")
        );
        Assert.Equal("6.9.36", InvokeParseBackendVersion("CLIProxyAPI Version: 6.9.36"));
        Assert.Equal("6.9.34", InvokeParseBackendVersion("CLIProxyAPI Version: 6.9.34"));

        Assert.True(InvokeIsExpectedBackendVersion("6.9.41"));
        Assert.True(InvokeIsExpectedBackendVersion("v6.9.41"));
        Assert.False(InvokeIsExpectedBackendVersion("6.9.36"));
        Assert.False(InvokeIsExpectedBackendVersion("6.9.34"));
        Assert.False(InvokeIsExpectedBackendVersion(null));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodexCliPlus.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string? InvokeParseBackendVersion(string output)
    {
        var method = typeof(BackendAssetService).GetMethod(
            "TryParseCliProxyApiVersion",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.NotNull(method);
        return (string?)method!.Invoke(null, [output]);
    }

    private static bool InvokeIsExpectedBackendVersion(string? version)
    {
        var method = typeof(BackendAssetService).GetMethod(
            "IsExpectedBackendVersion",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(null, [version]));
    }
}
