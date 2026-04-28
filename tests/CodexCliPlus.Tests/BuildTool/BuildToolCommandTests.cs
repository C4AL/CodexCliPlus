using System.IO.Compression;
using System.Reflection;
using System.Text;

using CodexCliPlus.BuildTool;
using CodexCliPlus.Core.Constants;

namespace CodexCliPlus.Tests.BuildTool;

public sealed class BuildToolCommandTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"codexcliplus-buildtool-{Guid.NewGuid():N}");

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
        var manifestPath = Path.Combine(outputRoot, "assets", "asset-manifest.json");
        Assert.True(File.Exists(manifestPath));
        var manifestText = await File.ReadAllTextAsync(manifestPath);
        Assert.Contains(BackendExecutableNames.ManagedExecutableFileName, manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain(BackendExecutableNames.UpstreamExecutableFileName, manifestText, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            outputRoot,
            "assets",
            "backend",
            "windows-x64",
            BackendExecutableNames.ManagedExecutableFileName)));
        Assert.False(File.Exists(Path.Combine(
            outputRoot,
            "assets",
            "backend",
            "windows-x64",
            BackendExecutableNames.UpstreamExecutableFileName)));
        Assert.Contains("asset verification passed", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task VerifyAssetsFetchesManifestWhenCleanOutputHasNone()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(_rootDirectory, "out");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var verifyCode = await BuildToolApp.ExecuteAsync(
            ["verify-assets", "--repo-root", repositoryRoot, "--output", outputRoot, "--version", "9.9.9"],
            output,
            error,
            new RecordingProcessRunner());

        Assert.Equal(0, verifyCode);
        Assert.True(File.Exists(Path.Combine(outputRoot, "assets", "asset-manifest.json")));
        Assert.Contains("asset manifest not found; fetching assets", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("asset verification passed", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void RequiredBackendFilesUseManagedExecutableName()
    {
        Assert.Contains(BackendExecutableNames.ManagedExecutableFileName, AssetCommands.RequiredFiles);
        Assert.DoesNotContain(BackendExecutableNames.UpstreamExecutableFileName, AssetCommands.RequiredFiles);
    }

    [Fact]
    public void BackendArchiveExtractionMapsUpstreamExecutableToManagedName()
    {
        var backendTarget = Path.Combine(_rootDirectory, "archive-assets");
        Directory.CreateDirectory(backendTarget);
        using var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, BackendExecutableNames.UpstreamExecutableFileName, "upstream binary");
            WriteZipEntry(archive, "LICENSE", "license");
            WriteZipEntry(archive, "README.md", "readme");
            WriteZipEntry(archive, "README_CN.md", "readme cn");
            WriteZipEntry(archive, "config.example.yaml", "config");
        }

        archiveStream.Position = 0;
        using var output = new StringWriter();
        using var error = new StringWriter();

        InvokeExtractBackendArchive(archiveStream, backendTarget, new BuildLogger(output, error));

        Assert.True(File.Exists(Path.Combine(backendTarget, BackendExecutableNames.ManagedExecutableFileName)));
        Assert.False(File.Exists(Path.Combine(backendTarget, BackendExecutableNames.UpstreamExecutableFileName)));
        Assert.Equal(
            "upstream binary",
            File.ReadAllText(Path.Combine(backendTarget, BackendExecutableNames.ManagedExecutableFileName)));
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
        CreateZipWithEntries(
            Path.Combine(packageRoot, "CodexCliPlus.Portable.9.9.9.win-x64.zip"),
            "CodexCliPlus.exe",
            "portable-mode.json",
            "assets/webui/upstream/dist/index.html",
            "assets/webui/upstream/sync.json");
        CreateZipWithEntries(
            Path.Combine(packageRoot, "CodexCliPlus.Dev.9.9.9.win-x64.zip"),
            "app/CodexCliPlus.exe",
            "app/dev-mode.json",
            "app/artifacts/dev-data/.gitkeep",
            "app/assets/webui/upstream/dist/index.html",
            "app/assets/webui/upstream/sync.json");
        CreateStubExecutable(Path.Combine(packageRoot, "CodexCliPlus.Setup.9.9.9.exe"));
        CreateInstallerStagingZip(
            Path.Combine(packageRoot, "CodexCliPlus.Setup.9.9.9.win-x64.zip"),
            CreateStubExecutableBytes());
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

    [Fact]
    public async Task VerifyPackageRejectsInstallerExecutableWithoutPeHeader()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(_rootDirectory, "out");
        var packageRoot = Path.Combine(outputRoot, "packages");
        Directory.CreateDirectory(packageRoot);
        CreateZipWithEntries(
            Path.Combine(packageRoot, "CodexCliPlus.Portable.9.9.9.win-x64.zip"),
            "CodexCliPlus.exe",
            "portable-mode.json",
            "assets/webui/upstream/dist/index.html",
            "assets/webui/upstream/sync.json");
        CreateZipWithEntries(
            Path.Combine(packageRoot, "CodexCliPlus.Dev.9.9.9.win-x64.zip"),
            "app/CodexCliPlus.exe",
            "app/dev-mode.json",
            "app/artifacts/dev-data/.gitkeep",
            "app/assets/webui/upstream/dist/index.html",
            "app/assets/webui/upstream/sync.json");
        File.WriteAllBytes(Path.Combine(packageRoot, "CodexCliPlus.Setup.9.9.9.exe"), new byte[80]);
        CreateInstallerStagingZip(
            Path.Combine(packageRoot, "CodexCliPlus.Setup.9.9.9.win-x64.zip"),
            CreateStubExecutableBytes());
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            ["verify-package", "--repo-root", repositoryRoot, "--output", outputRoot, "--version", "9.9.9"],
            output,
            error,
            new RecordingProcessRunner());

        Assert.Equal(1, exitCode);
        Assert.Contains("Windows PE header", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyPackageRejectsInvalidExecutableInsideInstallerStagingArchive()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(_rootDirectory, "out");
        var packageRoot = Path.Combine(outputRoot, "packages");
        Directory.CreateDirectory(packageRoot);
        CreateZipWithEntries(
            Path.Combine(packageRoot, "CodexCliPlus.Portable.9.9.9.win-x64.zip"),
            "CodexCliPlus.exe",
            "portable-mode.json",
            "assets/webui/upstream/dist/index.html",
            "assets/webui/upstream/sync.json");
        CreateZipWithEntries(
            Path.Combine(packageRoot, "CodexCliPlus.Dev.9.9.9.win-x64.zip"),
            "app/CodexCliPlus.exe",
            "app/dev-mode.json",
            "app/artifacts/dev-data/.gitkeep",
            "app/assets/webui/upstream/dist/index.html",
            "app/assets/webui/upstream/sync.json");
        CreateStubExecutable(Path.Combine(packageRoot, "CodexCliPlus.Setup.9.9.9.exe"));
        CreateZipWithExecutableEntries(
            Path.Combine(packageRoot, "CodexCliPlus.Setup.9.9.9.win-x64.zip"),
            new Dictionary<string, byte[]>
            {
                ["app-package/CodexCliPlus.exe"] = Encoding.UTF8.GetBytes("codexcliplus"),
                ["app-package/assets/webui/upstream/dist/index.html"] = Encoding.UTF8.GetBytes("<html></html>"),
                ["app-package/assets/webui/upstream/sync.json"] = Encoding.UTF8.GetBytes("{}"),
                ["mica-setup.json"] = Encoding.UTF8.GetBytes("{}"),
                ["micasetup.json"] = Encoding.UTF8.GetBytes("{}"),
                ["output/CodexCliPlus.Setup.9.9.9.exe"] = Encoding.UTF8.GetBytes("bad"),
                ["app-package/packaging/uninstall-cleanup.json"] = Encoding.UTF8.GetBytes("{}"),
                ["app-package/packaging/dependency-precheck.json"] = Encoding.UTF8.GetBytes("{}"),
                ["app-package/packaging/update-policy.json"] = Encoding.UTF8.GetBytes("{}")
            });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            ["verify-package", "--repo-root", repositoryRoot, "--output", outputRoot, "--version", "9.9.9"],
            output,
            error,
            new RecordingProcessRunner());

        Assert.Equal(1, exitCode);
        Assert.Contains("output/CodexCliPlus.Setup.9.9.9.exe", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildVendoredUsesOverlayMergedWorktree()
    {
        var repositoryRoot = CreateRepositoryWithVendoredWebUiOverlay();
        var outputRoot = Path.Combine(_rootDirectory, "out");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var logger = new BuildLogger(output, error);
        var runner = new OverlayRecordingProcessRunner();
        var context = new BuildContext(
            new BuildOptions("publish", repositoryRoot, outputRoot, "Release", "win-x64", "9.9.9"),
            logger,
            runner,
            new NoOpSigningService());

        var exitCode = await WebUiCommands.BuildVendoredAsync(context);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, runner.Calls.Count);
        Assert.All(runner.Calls, call => Assert.Equal(context.WebUiBuildSourceRoot, call.WorkingDirectory));
        Assert.DoesNotContain(runner.Calls, call => string.Equals(call.WorkingDirectory, context.WebUiSourceRoot, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("overlay", File.ReadAllText(Path.Combine(context.WebUiBuildSourceRoot, "src", "message.txt")));
        Assert.Equal("overlay", File.ReadAllText(Path.Combine(context.WebUiVendoredDistRoot, "index.html")));
        Assert.Equal("upstream", File.ReadAllText(Path.Combine(context.WebUiSourceRoot, "src", "message.txt")));
        Assert.True(File.Exists(Path.Combine(context.WebUiBuildSourceRoot, "node_modules", ".installed")));
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ProcessRunnerClassifiesSuccessfulStandardErrorAsWarning()
    {
        Directory.CreateDirectory(_rootDirectory);
        var scriptPath = Path.Combine(_rootDirectory, "successful-stderr.ps1");
        File.WriteAllText(
            scriptPath,
            """
            Write-Output 'stdout-line'
            [Console]::Error.WriteLine('stderr-line')
            exit 0
            """,
            Encoding.UTF8);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var logger = new BuildLogger(output, error);

        var exitCode = await new ProcessRunner().RunAsync(
            "powershell",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath],
            _rootDirectory,
            logger);

        Assert.Equal(0, exitCode);
        Assert.Contains("[info] stdout-line", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("[warn] stderr-line", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("[error]", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ProcessRunnerClassifiesFailedStandardErrorAsError()
    {
        Directory.CreateDirectory(_rootDirectory);
        var scriptPath = Path.Combine(_rootDirectory, "failed-stderr.ps1");
        File.WriteAllText(
            scriptPath,
            """
            [Console]::Error.WriteLine('fatal-line')
            exit 7
            """,
            Encoding.UTF8);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var logger = new BuildLogger(output, error);

        var exitCode = await new ProcessRunner().RunAsync(
            "powershell",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath],
            _rootDirectory,
            logger);

        Assert.Equal(7, exitCode);
        Assert.DoesNotContain("[warn] fatal-line", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("[error] fatal-line", error.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void RequirePublishRootRejectsMissingVendoredWebUiAssets()
    {
        var publishRoot = Path.Combine(_rootDirectory, "publish");
        Directory.CreateDirectory(publishRoot);
        File.WriteAllText(Path.Combine(publishRoot, "CodexCliPlus.exe"), "codexcliplus");

        var exception = Assert.Throws<FileNotFoundException>(() => SafeFileSystem.RequirePublishRoot(publishRoot));

        Assert.Contains(Path.Combine("assets", "webui", "upstream"), exception.FileName ?? exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private string CreateRepositoryWithBackendAssets()
    {
        var repositoryRoot = Path.Combine(_rootDirectory, "repo");
        Directory.CreateDirectory(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "CodexCliPlus.sln"), string.Empty);
        var assetRoot = Path.Combine(repositoryRoot, "resources", "backend", "windows-x64");
        Directory.CreateDirectory(assetRoot);
        foreach (var fileName in AssetCommands.RequiredFiles)
        {
            File.WriteAllText(Path.Combine(assetRoot, fileName), $"{fileName} content");
        }

        return repositoryRoot;
    }

    private string CreateRepositoryWithVendoredWebUiOverlay()
    {
        var repositoryRoot = Path.Combine(_rootDirectory, "webui-repo");
        Directory.CreateDirectory(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "CodexCliPlus.sln"), string.Empty);

        var upstreamRoot = Path.Combine(repositoryRoot, "resources", "webui", "upstream");
        var sourceRoot = Path.Combine(upstreamRoot, "source");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "src"));
        File.WriteAllText(Path.Combine(sourceRoot, "package.json"), "{\"name\":\"test-webui\",\"private\":true}");
        File.WriteAllText(Path.Combine(sourceRoot, "package-lock.json"), "{\"name\":\"test-webui\",\"lockfileVersion\":3}");
        File.WriteAllText(Path.Combine(sourceRoot, "src", "message.txt"), "upstream");
        File.WriteAllText(Path.Combine(upstreamRoot, "sync.json"), "{}");

        var overlayRoot = Path.Combine(repositoryRoot, "resources", "webui", "modules", "cpa-uv-overlay");
        Directory.CreateDirectory(Path.Combine(overlayRoot, "source", "src"));
        File.WriteAllText(Path.Combine(overlayRoot, "module.json"), "{\"id\":\"cpa-uv-overlay\"}");
        File.WriteAllText(Path.Combine(overlayRoot, "source", "src", "message.txt"), "overlay");

        return repositoryRoot;
    }

    private static void CreateZipWithEntry(string packagePath, string entryName)
    {
        CreateZipWithEntries(packagePath, entryName);
    }

    private static void InvokeExtractBackendArchive(Stream archiveStream, string backendTarget, BuildLogger logger)
    {
        var method = typeof(AssetCommands).GetMethod(
            "ExtractBackendArchive",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(null, [archiveStream, backendTarget, logger]);
    }

    private static void WriteZipEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
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

    private static void CreateZipWithExecutableEntries(string packagePath, IReadOnlyDictionary<string, byte[]> entries)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        foreach (var entryPair in entries)
        {
            var entry = archive.CreateEntry(entryPair.Key);
            using var stream = entry.Open();
            stream.Write(entryPair.Value, 0, entryPair.Value.Length);
        }
    }

    private static void CreateInstallerStagingZip(string packagePath, byte[] installerBytes)
    {
        CreateZipWithExecutableEntries(
            packagePath,
            new Dictionary<string, byte[]>
            {
                ["app-package/CodexCliPlus.exe"] = Encoding.UTF8.GetBytes("codexcliplus"),
                ["app-package/assets/webui/upstream/dist/index.html"] = Encoding.UTF8.GetBytes("<html></html>"),
                ["app-package/assets/webui/upstream/sync.json"] = Encoding.UTF8.GetBytes("{}"),
                ["mica-setup.json"] = Encoding.UTF8.GetBytes("{}"),
                ["micasetup.json"] = Encoding.UTF8.GetBytes("{}"),
                ["output/CodexCliPlus.Setup.9.9.9.exe"] = installerBytes,
                ["app-package/packaging/uninstall-cleanup.json"] = Encoding.UTF8.GetBytes("{}"),
                ["app-package/packaging/dependency-precheck.json"] = Encoding.UTF8.GetBytes("{}"),
                ["app-package/packaging/update-policy.json"] = Encoding.UTF8.GetBytes("{}")
            });
    }

    private static void CreateStubExecutable(string path)
    {
        File.WriteAllBytes(path, CreateStubExecutableBytes());
    }

    private static byte[] CreateStubExecutableBytes()
    {
        var bytes = new byte[80];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
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

    private sealed record ProcessCall(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

    private sealed class OverlayRecordingProcessRunner : IProcessRunner
    {
        public List<ProcessCall> Calls { get; } = [];

        public Task<int> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            BuildLogger logger,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new ProcessCall(fileName, arguments.ToArray(), workingDirectory));
            logger.Info($"{fileName} {string.Join(" ", arguments)}");

            if (arguments.SequenceEqual(["ci"], StringComparer.Ordinal))
            {
                var nodeModulesRoot = Path.Combine(workingDirectory, "node_modules");
                Directory.CreateDirectory(nodeModulesRoot);
                File.WriteAllText(Path.Combine(nodeModulesRoot, ".installed"), "ok");
                return Task.FromResult(0);
            }

            if (arguments.SequenceEqual(["run", "build"], StringComparer.Ordinal))
            {
                var mergedMarkerPath = Path.Combine(workingDirectory, "src", "message.txt");
                var distRoot = Path.Combine(Directory.GetParent(workingDirectory)!.FullName, "dist");
                Directory.CreateDirectory(distRoot);
                File.WriteAllText(
                    Path.Combine(distRoot, "index.html"),
                    File.ReadAllText(mergedMarkerPath));
                return Task.FromResult(0);
            }

            return Task.FromResult(0);
        }
    }
}
