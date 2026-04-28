using System.Reflection;
using System.Text;

using CodexCliPlus.Core.Constants;
using CodexCliPlus.Infrastructure.Backend;

namespace CodexCliPlus.Tests.Backend;

public sealed class BackendReleaseMetadataTests
{
    [Fact]
    public void BackendReleaseMetadataPinsCliProxyApi6940WindowsAsset()
    {
        Assert.Equal("6.9.40", BackendReleaseMetadata.Version);
        Assert.Equal("v6.9.40", BackendReleaseMetadata.ReleaseTag);
        Assert.Equal("CLIProxyAPI_6.9.40_windows_amd64.zip", BackendReleaseMetadata.AssetName);
        Assert.Equal(
            "https://github.com/router-for-me/CLIProxyAPI/releases/tag/v6.9.40",
            BackendReleaseMetadata.ReleaseUrl);
        Assert.Equal(
            "https://github.com/router-for-me/CLIProxyAPI/releases/download/v6.9.40/CLIProxyAPI_6.9.40_windows_amd64.zip",
            BackendReleaseMetadata.ArchiveUrl);
        Assert.Equal(
            "b21eb12a819f49fc43affc29a3996810be85c090ba0c523b7704ce90070ae760",
            BackendReleaseMetadata.ArchiveSha256);
    }

    [Fact]
    public void RuntimeAndBuildToolUseSharedBackendReleaseMetadata()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtimeSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.Infrastructure", "Backend", "BackendAssetService.cs"),
            Encoding.UTF8);
        var buildToolSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.BuildTool", "Program.cs"),
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
            "6.9.40",
            InvokeParseBackendVersion("CLIProxyAPI Version: 6.9.40"));
        Assert.Equal(
            "6.9.40",
            InvokeParseBackendVersion("CLIProxyAPI Version: v6.9.40\r\nBuild Date: 2026-04-01"));
        Assert.Equal(
            "6.9.36",
            InvokeParseBackendVersion("CLIProxyAPI Version: 6.9.36"));
        Assert.Equal(
            "6.9.34",
            InvokeParseBackendVersion("CLIProxyAPI Version: 6.9.34"));

        Assert.True(InvokeIsExpectedBackendVersion("6.9.40"));
        Assert.True(InvokeIsExpectedBackendVersion("v6.9.40"));
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
