using System.Reflection;
using System.Text;
using System.Text.Json;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Infrastructure.Backend;

namespace CodexCliPlus.Tests.Backend;

[Trait("Category", "Packaging")]
public sealed class BackendReleaseMetadataTests
{
    [Fact]
    public void BackendReleaseMetadataPinsCliProxyApi6109WindowsAsset()
    {
        Assert.Equal("6.10.9", BackendReleaseMetadata.Version);
        Assert.Equal("v6.10.9", BackendReleaseMetadata.ReleaseTag);
        Assert.Equal("CLIProxyAPI_6.10.9_windows_amd64.zip", BackendReleaseMetadata.AssetName);
        Assert.Equal(
            "https://github.com/router-for-me/CLIProxyAPI/releases/tag/v6.10.9",
            BackendReleaseMetadata.ReleaseUrl
        );
        Assert.Equal(
            "https://github.com/router-for-me/CLIProxyAPI/releases/download/v6.10.9/CLIProxyAPI_6.10.9_windows_amd64.zip",
            BackendReleaseMetadata.ArchiveUrl
        );
        Assert.Equal(
            "1da9061b7620aafd07b05fb95461065a935c5948378dc14dd5090797e16fd50c",
            BackendReleaseMetadata.ArchiveSha256
        );
        Assert.Equal(
            "785b00c3127eea6aa207f1207ead8a2aa93690a3",
            BackendReleaseMetadata.SourceCommit
        );
        Assert.Equal(
            "f17d22d4fb7df11c84b06b1fe2123e45561bdc6f14226a3f3cffa344491d4023",
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
    public void BackendWindowsManifestMatchesPinnedRuntimeMetadata()
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
    public void BackendSourceManifestDescribesRepositoryOwnedSourceBuild()
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifestPath = Path.Combine(
            repositoryRoot,
            "resources",
            "backend",
            "backend-source-manifest.json"
        );
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
        var root = document.RootElement;
        var artifact = root.GetProperty("generatedArtifact");

        Assert.Equal("CLIProxyAPI", root.GetProperty("component").GetString());
        Assert.Equal(BackendReleaseMetadata.Version, root.GetProperty("version").GetString());
        Assert.Equal(BackendReleaseMetadata.ReleaseTag, root.GetProperty("releaseTag").GetString());
        Assert.Equal(
            BackendReleaseMetadata.SourceCommit,
            root.GetProperty("sourceCommit").GetString()
        );
        Assert.Equal("resources/backend/source", root.GetProperty("sourceRoot").GetString());
        Assert.Equal("repo-source", root.GetProperty("buildKind").GetString());
        Assert.False(
            string.IsNullOrWhiteSpace(root.GetProperty("compatibilityPatchVersion").GetString())
        );
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("goVersion").GetString()));
        Assert.Equal(
            BackendExecutableNames.ManagedExecutableFileName,
            artifact.GetProperty("fileName").GetString()
        );
        Assert.Equal(
            BackendReleaseMetadata.BundledExecutableSha256,
            artifact.GetProperty("sha256").GetString()
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
        var buildToolSource = ReadBuildToolSources(repositoryRoot);

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
            "BackendReleaseMetadata.SourceCommit",
            buildToolSource,
            StringComparison.Ordinal
        );
        Assert.Contains("BackendSourceRoot", buildToolSource, StringComparison.Ordinal);
        Assert.Contains("go\",", buildToolSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"git\", [\"clone\"", buildToolSource, StringComparison.Ordinal);
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
        Assert.DoesNotContain(
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
        Assert.Equal("6.10.9", InvokeParseBackendVersion("CLIProxyAPI Version: 6.10.9"));
        Assert.Equal(
            "6.10.9",
            InvokeParseBackendVersion("CLIProxyAPI Version: v6.10.9\r\nBuild Date: 2026-05-07")
        );
        Assert.Equal("6.9.36", InvokeParseBackendVersion("CLIProxyAPI Version: 6.9.36"));
        Assert.Equal("6.9.34", InvokeParseBackendVersion("CLIProxyAPI Version: 6.9.34"));

        Assert.True(InvokeIsExpectedBackendVersion("6.10.9"));
        Assert.True(InvokeIsExpectedBackendVersion("v6.10.9"));
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

    private static string ReadBuildToolSources(string repositoryRoot)
    {
        var buildToolDirectory = Path.Combine(repositoryRoot, "src", "CodexCliPlus.BuildTool");
        var sourceFiles = Directory
            .GetFiles(buildToolDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Split(Path.DirectorySeparatorChar).Contains("bin")
                && !path.Split(Path.DirectorySeparatorChar).Contains("obj")
            )
            .OrderBy(path => path, StringComparer.Ordinal);

        return string.Join(
            Environment.NewLine,
            sourceFiles.Select(path => File.ReadAllText(path, Encoding.UTF8))
        );
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
