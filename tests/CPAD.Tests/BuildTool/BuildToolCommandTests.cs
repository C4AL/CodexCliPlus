using System.IO.Compression;

using CPAD.BuildTool;

namespace CPAD.Tests.BuildTool;

public sealed class BuildToolCommandTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"cpad-buildtool-{Guid.NewGuid():N}");

    [Fact]
    public async Task HelpListsBuildToolCommands()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(["--help"], output, error, new RecordingProcessRunner());

        Assert.Equal(0, exitCode);
        Assert.Contains("fetch-assets", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("verify-package", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task UnknownCommandReturnsNonZeroExitCode()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(["unknown"], output, error, new RecordingProcessRunner());

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown command", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAndVerifyAssetsWriteManifestFromRepositoryResources()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(_rootDirectory, "out");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var fetchCode = await BuildToolApp.ExecuteAsync(
            ["fetch-assets", "--repo-root", repositoryRoot, "--output", outputRoot, "--version", "9.9.9"],
            output,
            error,
            new RecordingProcessRunner());
        var verifyCode = await BuildToolApp.ExecuteAsync(
            ["verify-assets", "--repo-root", repositoryRoot, "--output", outputRoot, "--version", "9.9.9"],
            output,
            error,
            new RecordingProcessRunner());

        Assert.Equal(0, fetchCode);
        Assert.Equal(0, verifyCode);
        Assert.True(File.Exists(Path.Combine(outputRoot, "assets", "asset-manifest.json")));
        Assert.Contains("asset verification passed", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task VerifyPackageFailsWhenExpectedArtifactsAreMissing()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(_rootDirectory, "out");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            ["verify-package", "--repo-root", repositoryRoot, "--output", outputRoot, "--version", "9.9.9"],
            output,
            error,
            new RecordingProcessRunner());

        Assert.Equal(1, exitCode);
        Assert.Contains("Package missing", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyPackageAcceptsPortableDevAndInstallerArchives()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(_rootDirectory, "out");
        var packageRoot = Path.Combine(outputRoot, "packages");
        Directory.CreateDirectory(packageRoot);
        CreateZipWithEntry(Path.Combine(packageRoot, "CPAD.Portable.9.9.9.win-x64.zip"), "CPAD.exe");
        CreateZipWithEntry(Path.Combine(packageRoot, "CPAD.Dev.9.9.9.win-x64.zip"), "app/CPAD.exe");
        CreateZipWithEntries(
            Path.Combine(packageRoot, "CPAD.Setup.9.9.9.win-x64.zip"),
            "payload/app/CPAD.exe",
            "mica-setup.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            ["verify-package", "--repo-root", repositoryRoot, "--output", outputRoot, "--version", "9.9.9"],
            output,
            error,
            new RecordingProcessRunner());

        Assert.Equal(0, exitCode);
        Assert.Contains("package verification passed", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private string CreateRepositoryWithBackendAssets()
    {
        var repositoryRoot = Path.Combine(_rootDirectory, "repo");
        var assetRoot = Path.Combine(repositoryRoot, "resources", "backend", "windows-x64");
        Directory.CreateDirectory(assetRoot);
        foreach (var fileName in AssetCommands.RequiredFiles)
        {
            File.WriteAllText(Path.Combine(assetRoot, fileName), $"{fileName} content");
        }

        return repositoryRoot;
    }

    private static void CreateZipWithEntry(string packagePath, string entryName)
    {
        CreateZipWithEntries(packagePath, entryName);
    }

    private static void CreateZipWithEntries(string packagePath, params string[] entryNames)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        foreach (var entryName in entryNames)
        {
            var entry = archive.CreateEntry(entryName);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream);
            writer.Write("test");
        }
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public Task<int> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            BuildLogger logger,
            CancellationToken cancellationToken = default)
        {
            logger.Info($"{fileName} {string.Join(" ", arguments)}");
            return Task.FromResult(0);
        }
    }
}
