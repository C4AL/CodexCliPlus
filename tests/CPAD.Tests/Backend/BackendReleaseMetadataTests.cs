using System.Reflection;
using System.Text;

using CPAD.Core.Constants;
using CPAD.Infrastructure.Backend;

namespace CPAD.Tests.Backend;

public sealed class BackendReleaseMetadataTests
{
    [Fact]
    public void BackendReleaseMetadataPinsCliProxyApi6936WindowsAsset()
    {
        Assert.Equal("6.9.36", BackendReleaseMetadata.Version);
        Assert.Equal("v6.9.36", BackendReleaseMetadata.ReleaseTag);
        Assert.Equal("CLIProxyAPI_6.9.36_windows_amd64.zip", BackendReleaseMetadata.AssetName);
        Assert.Equal(
            "https://github.com/router-for-me/CLIProxyAPI/releases/tag/v6.9.36",
            BackendReleaseMetadata.ReleaseUrl);
        Assert.Equal(
            "https://github.com/router-for-me/CLIProxyAPI/releases/download/v6.9.36/CLIProxyAPI_6.9.36_windows_amd64.zip",
            BackendReleaseMetadata.ArchiveUrl);
        Assert.Equal(
            "97f3ccc20e2b6fe35faa02732ca6497ccf13c57eac08ed94263682545515a2c4",
            BackendReleaseMetadata.ArchiveSha256);
    }

    [Fact]
    public void RuntimeAndBuildToolUseSharedBackendReleaseMetadata()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtimeSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CPAD.Infrastructure", "Backend", "BackendAssetService.cs"),
            Encoding.UTF8);
        var buildToolSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CPAD.BuildTool", "Program.cs"),
            Encoding.UTF8);

        Assert.Contains("BackendReleaseMetadata.Version", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("BackendReleaseMetadata.ArchiveUrl", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("BackendReleaseMetadata.ArchiveSha256", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("BackendReleaseMetadata.ArchiveUrl", buildToolSource, StringComparison.Ordinal);
        Assert.Contains("BackendReleaseMetadata.ArchiveSha256", buildToolSource, StringComparison.Ordinal);
        Assert.DoesNotContain("6.9.34", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("6.9.34", buildToolSource, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendAssetServiceParsesAndRejectsCliProxyApiVersions()
    {
        Assert.Equal(
            "6.9.36",
            InvokeParseBackendVersion("CLIProxyAPI Version: 6.9.36"));
        Assert.Equal(
            "6.9.36",
            InvokeParseBackendVersion("CLIProxyAPI Version: v6.9.36\r\nBuild Date: 2026-04-01"));
        Assert.Equal(
            "6.9.34",
            InvokeParseBackendVersion("CLIProxyAPI Version: 6.9.34"));

        Assert.True(InvokeIsExpectedBackendVersion("6.9.36"));
        Assert.True(InvokeIsExpectedBackendVersion("v6.9.36"));
        Assert.False(InvokeIsExpectedBackendVersion("6.9.34"));
        Assert.False(InvokeIsExpectedBackendVersion(null));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CliProxyApiDesktop.sln")))
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
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (string?)method!.Invoke(null, [output]);
    }

    private static bool InvokeIsExpectedBackendVersion(string? version)
    {
        var method = typeof(BackendAssetService).GetMethod(
            "IsExpectedBackendVersion",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(null, [version]));
    }
}
