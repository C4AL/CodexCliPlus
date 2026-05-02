using System.IO.Compression;
using System.Text;
using CodexCliPlus.BuildTool;
using CodexCliPlus.Core.Constants;

namespace CodexCliPlus.Tests.BuildTool;

public sealed class BuildToolCommandTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-buildtool-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task HelpListsBuildToolCommands()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            ["--help"],
            output,
            error,
            new RecordingProcessRunner()
        );

        Assert.Equal(0, exitCode);
        Assert.Contains("fetch-assets", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("build-webui", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sync-backend-source", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("package-online-installer", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("package-offline-installer", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("verify-package", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("write-checksums", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("export-public-release", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("clean-artifacts", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--keep-package-staging", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--artifact-retention", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void BuildOptionsParsesPackageStagingAndRetentionOptions()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(repositoryRoot, "artifacts", "buildtool-custom");

        var parsed = BuildOptions.TryParse(
            [
                "package-offline-installer",
                "--repo-root",
                repositoryRoot,
                "--output",
                outputRoot,
                "--keep-package-staging",
                "true",
                "--artifact-retention",
                "2",
            ],
            out var options,
            out var error
        );

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.True(options.KeepPackageStaging);
        Assert.Equal(2, options.ArtifactRetention);
        Assert.Equal(Path.GetFullPath(outputRoot), options.OutputRoot);
    }

    [Fact]
    public void BuildOptionsRejectsInvalidRetentionValues()
    {
        var parsed = BuildOptions.TryParse(
            ["fetch-assets", "--artifact-retention", "-1"],
            out var options,
            out var error
        );

        Assert.False(parsed);
        Assert.Null(options);
        Assert.Contains("--artifact-retention", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownCommandReturnsNonZeroExitCode()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            ["unknown"],
            output,
            error,
            new RecordingProcessRunner()
        );

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
            [
                "fetch-assets",
                "--repo-root",
                repositoryRoot,
                "--output",
                outputRoot,
                "--version",
                "9.9.9",
            ],
            output,
            error,
            new RecordingProcessRunner()
        );
        var verifyCode = await BuildToolApp.ExecuteAsync(
            [
                "verify-assets",
                "--repo-root",
                repositoryRoot,
                "--output",
                outputRoot,
                "--version",
                "9.9.9",
            ],
            output,
            error,
            new RecordingProcessRunner()
        );

        Assert.Equal(0, fetchCode);
        Assert.Equal(0, verifyCode);
        var manifestPath = Path.Combine(outputRoot, "assets", "asset-manifest.json");
        Assert.True(File.Exists(manifestPath));
        var manifestText = await File.ReadAllTextAsync(manifestPath);
        Assert.Contains(
            BackendExecutableNames.ManagedExecutableFileName,
            manifestText,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            BackendExecutableNames.UpstreamExecutableFileName,
            manifestText,
            StringComparison.Ordinal
        );
        Assert.True(
            File.Exists(
                Path.Combine(
                    outputRoot,
                    "assets",
                    "backend",
                    "windows-x64",
                    BackendExecutableNames.ManagedExecutableFileName
                )
            )
        );
        Assert.False(
            File.Exists(
                Path.Combine(
                    outputRoot,
                    "assets",
                    "backend",
                    "windows-x64",
                    BackendExecutableNames.UpstreamExecutableFileName
                )
            )
        );
        Assert.Contains("asset verification passed", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task CleanArtifactsClearsGeneratedOutputsButKeepsAssetsAndCaches()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(_rootDirectory, "out-clean");
        var publishRoot = Path.Combine(outputRoot, "publish", "win-x64");
        var packageRoot = Path.Combine(outputRoot, "packages");
        var installerRoot = Path.Combine(outputRoot, "installer", "win-x64");
        var tempRoot = Path.Combine(outputRoot, "temp");
        var cacheRoot = Path.Combine(outputRoot, "cache");
        var assetsRoot = Path.Combine(outputRoot, "assets");
        Directory.CreateDirectory(publishRoot);
        Directory.CreateDirectory(packageRoot);
        Directory.CreateDirectory(installerRoot);
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(cacheRoot);
        Directory.CreateDirectory(assetsRoot);
        File.WriteAllText(Path.Combine(publishRoot, "app.txt"), "publish");
        File.WriteAllText(Path.Combine(packageRoot, "package.txt"), "package");
        File.WriteAllText(Path.Combine(installerRoot, "installer.txt"), "installer");
        File.WriteAllText(Path.Combine(tempRoot, "temp.txt"), "temp");
        File.WriteAllText(Path.Combine(cacheRoot, "cache.txt"), "cache");
        File.WriteAllText(Path.Combine(assetsRoot, "asset.txt"), "asset");

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await BuildToolApp.ExecuteAsync(
            ["clean-artifacts", "--repo-root", repositoryRoot, "--output", outputRoot],
            output,
            error,
            new RecordingProcessRunner()
        );

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(Path.Combine(publishRoot, "app.txt")));
        Assert.False(File.Exists(Path.Combine(packageRoot, "package.txt")));
        Assert.False(File.Exists(Path.Combine(installerRoot, "installer.txt")));
        Assert.False(File.Exists(Path.Combine(tempRoot, "temp.txt")));
        Assert.True(File.Exists(Path.Combine(cacheRoot, "cache.txt")));
        Assert.True(File.Exists(Path.Combine(assetsRoot, "asset.txt")));
        Assert.Contains("cleaned BuildTool", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ArtifactRetentionPrunesOnlyOlderRepositoryBuildToolOutputRoots()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var artifactsRoot = Path.Combine(repositoryRoot, "artifacts");
        var currentOutputRoot = Path.Combine(artifactsRoot, "buildtool-current");
        var keptSiblingRoot = Path.Combine(artifactsRoot, "buildtool-keep");
        var prunedSiblingRoot = Path.Combine(artifactsRoot, "buildtool-prune");
        var unrelatedRoot = Path.Combine(artifactsRoot, "release-output");
        Directory.CreateDirectory(currentOutputRoot);
        Directory.CreateDirectory(keptSiblingRoot);
        Directory.CreateDirectory(prunedSiblingRoot);
        Directory.CreateDirectory(unrelatedRoot);
        File.WriteAllText(Path.Combine(keptSiblingRoot, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(prunedSiblingRoot, "prune.txt"), "prune");
        File.WriteAllText(Path.Combine(unrelatedRoot, "unrelated.txt"), "unrelated");
        Directory.SetLastWriteTimeUtc(keptSiblingRoot, DateTime.UtcNow.AddMinutes(-1));
        Directory.SetLastWriteTimeUtc(prunedSiblingRoot, DateTime.UtcNow.AddMinutes(-2));

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await BuildToolApp.ExecuteAsync(
            [
                "clean-artifacts",
                "--repo-root",
                repositoryRoot,
                "--output",
                currentOutputRoot,
                "--artifact-retention",
                "2",
            ],
            output,
            error,
            new RecordingProcessRunner()
        );

        Assert.Equal(0, exitCode);
        Assert.True(Directory.Exists(currentOutputRoot));
        Assert.True(Directory.Exists(keptSiblingRoot));
        Assert.False(Directory.Exists(prunedSiblingRoot));
        Assert.True(Directory.Exists(unrelatedRoot));
        Assert.Contains(
            "removed old BuildTool output root",
            output.ToString(),
            StringComparison.Ordinal
        );
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ArtifactRetentionZeroDisablesPruning()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var artifactsRoot = Path.Combine(repositoryRoot, "artifacts");
        var currentOutputRoot = Path.Combine(artifactsRoot, "buildtool-current");
        var oldSiblingRoot = Path.Combine(artifactsRoot, "buildtool-old");
        Directory.CreateDirectory(currentOutputRoot);
        Directory.CreateDirectory(oldSiblingRoot);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await BuildToolApp.ExecuteAsync(
            [
                "clean-artifacts",
                "--repo-root",
                repositoryRoot,
                "--output",
                currentOutputRoot,
                "--artifact-retention",
                "0",
            ],
            output,
            error,
            new RecordingProcessRunner()
        );

        Assert.Equal(0, exitCode);
        Assert.True(Directory.Exists(oldSiblingRoot));
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void SafeFileSystemRejectsAllowedRootPrefixSibling()
    {
        var allowedRoot = Path.Combine(_rootDirectory, "out");
        var prefixSibling = Path.Combine(_rootDirectory, "out-old");
        Directory.CreateDirectory(prefixSibling);
        var markerPath = Path.Combine(prefixSibling, "marker.txt");
        File.WriteAllText(markerPath, "do not delete");

        Assert.Throws<InvalidOperationException>(() =>
            SafeFileSystem.CleanDirectory(prefixSibling, allowedRoot)
        );
        Assert.True(File.Exists(markerPath));
    }

    [Fact]
    public async Task VerifyAssetsFetchesManifestWhenCleanOutputHasNone()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(_rootDirectory, "out");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var verifyCode = await BuildToolApp.ExecuteAsync(
            [
                "verify-assets",
                "--repo-root",
                repositoryRoot,
                "--output",
                outputRoot,
                "--version",
                "9.9.9",
            ],
            output,
            error,
            new RecordingProcessRunner()
        );

        Assert.Equal(0, verifyCode);
        Assert.True(File.Exists(Path.Combine(outputRoot, "assets", "asset-manifest.json")));
        Assert.Contains(
            "asset manifest not found; fetching assets",
            output.ToString(),
            StringComparison.Ordinal
        );
        Assert.Contains("asset verification passed", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task FetchAssetsBuildsManagedBackendFromRepositoryOwnedSourceWithoutCloningUpstream()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(_rootDirectory, "out-build");
        var runner = new BackendSourceBuildProcessRunner();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            [
                "fetch-assets",
                "--repo-root",
                repositoryRoot,
                "--output",
                outputRoot,
                "--version",
                "9.9.9",
            ],
            output,
            error,
            runner
        );

        Assert.True(exitCode == 0, error.ToString());
        var backendRoot = Path.Combine(outputRoot, "assets", "backend", "windows-x64");
        Assert.True(
            File.Exists(Path.Combine(backendRoot, BackendExecutableNames.ManagedExecutableFileName))
        );
        Assert.True(File.Exists(Path.Combine(backendRoot, "LICENSE")));
        Assert.Contains("asset built from source", output.ToString(), StringComparison.Ordinal);
        Assert.False(
            Directory.Exists(Path.Combine(outputRoot, "temp", "backend-source")),
            "fetch-assets should build from resources/backend/source directly."
        );
        Assert.DoesNotContain(
            runner.Calls,
            call => call.FileName == "git" && call.Arguments[0] == "clone"
        );
        Assert.DoesNotContain(
            runner.Calls,
            call => call.FileName == "go" && call.Arguments.SequenceEqual(["mod", "download"])
        );

        var goBuildCall = Assert.Single(
            runner.Calls,
            call =>
                call.FileName == "go" && call.Arguments.Count > 0 && call.Arguments[0] == "build"
        );
        Assert.Contains("-mod=readonly", goBuildCall.Arguments);
        Assert.Contains("-trimpath", goBuildCall.Arguments);
        var ldflagsIndex = goBuildCall.Arguments.ToList().IndexOf("-ldflags") + 1;
        Assert.True(ldflagsIndex > 0);
        Assert.Contains("main.Version=6.9.45", goBuildCall.Arguments[ldflagsIndex]);
        Assert.DoesNotContain("'main.Version", goBuildCall.Arguments[ldflagsIndex]);
        Assert.Equal("0", goBuildCall.Environment["CGO_ENABLED"]);
        Assert.Equal("windows", goBuildCall.Environment["GOOS"]);
        Assert.Equal("amd64", goBuildCall.Environment["GOARCH"]);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void RequiredBackendFilesUseManagedExecutableName()
    {
        Assert.Contains(
            BackendExecutableNames.ManagedExecutableFileName,
            AssetCommands.RequiredFiles
        );
        Assert.DoesNotContain(
            BackendExecutableNames.UpstreamExecutableFileName,
            AssetCommands.RequiredFiles
        );
    }

    [Fact]
    public async Task SyncBackendSourceIsNotAPublicCommand()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(_rootDirectory, "out-sync");
        var runner = new BackendSourceBuildProcessRunner();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            [
                "sync-backend-source",
                "--repo-root",
                repositoryRoot,
                "--output",
                outputRoot,
                "--version",
                "9.9.9",
            ],
            output,
            error,
            runner
        );

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain(runner.Calls, call => call.FileName == "git");
        Assert.False(File.Exists(Path.Combine(outputRoot, "backend-source-sync-report.json")));
        Assert.Contains("Unknown command", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildToolSourceDoesNotFetchMicaSetupOrCloneBackendSources()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src", "CodexCliPlus.BuildTool");
        var buildToolSource = string.Join(
            "\n",
            Directory
                .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText)
        );

        Assert.DoesNotContain("lemutec/MicaSetup", buildToolSource, StringComparison.Ordinal);
        Assert.DoesNotContain("browser_download_url", buildToolSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CLIProxyAPI.git", buildToolSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"clone\"", buildToolSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyPackageFailsWhenExpectedArtifactsAreMissing()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(_rootDirectory, "out");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            [
                "verify-package",
                "--repo-root",
                repositoryRoot,
                "--output",
                outputRoot,
                "--version",
                "9.9.9",
            ],
            output,
            error,
            new RecordingProcessRunner()
        );

        Assert.Equal(1, exitCode);
        Assert.Contains("Package missing", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyPackageAcceptsOnlineOfflineInstallersAndUpdatePackage()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(_rootDirectory, "out");
        var packageRoot = Path.Combine(outputRoot, "packages");
        Directory.CreateDirectory(packageRoot);
        CreateInstallerPackage(
            packageRoot,
            "Online",
            CreateStubExecutableBytes(),
            CreateStubExecutableBytes()
        );
        CreateInstallerPackage(
            packageRoot,
            "Offline",
            CreateStubExecutableBytes(),
            CreateStubExecutableBytes()
        );
        CreateUpdatePackage(packageRoot);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            [
                "verify-package",
                "--repo-root",
                repositoryRoot,
                "--output",
                outputRoot,
                "--version",
                "9.9.9",
            ],
            output,
            error,
            new RecordingProcessRunner()
        );

        Assert.Equal(0, exitCode);
        Assert.Contains("package verification passed", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task WriteChecksumsCreatesReleaseManifestForGeneratedArtifacts()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(repositoryRoot, "artifacts", "buildtool");
        var packageRoot = Path.Combine(outputRoot, "packages");
        Directory.CreateDirectory(packageRoot);
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "CodexCliPlus.Setup.Online.9.9.9.exe"),
            "online installer"
        );
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "CodexCliPlus.Setup.Online.9.9.9.win-x64.zip"),
            "online internal staging package"
        );
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "CodexCliPlus.Setup.Offline.9.9.9.exe"),
            "offline installer"
        );
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "CodexCliPlus.Setup.Offline.9.9.9.win-x64.zip"),
            "internal staging package"
        );
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "CodexCliPlus.Update.9.9.9.win-x64.zip"),
            "update"
        );
        Directory.CreateDirectory(Path.Combine(outputRoot, "assets"));
        await File.WriteAllTextAsync(
            Path.Combine(outputRoot, "assets", "asset-manifest.json"),
            "{}"
        );
        Directory.CreateDirectory(Path.Combine(outputRoot, "publish", "win-x64"));
        await File.WriteAllTextAsync(
            Path.Combine(outputRoot, "publish", "win-x64", "publish-manifest.json"),
            "{}"
        );
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            [
                "write-checksums",
                "--repo-root",
                repositoryRoot,
                "--output",
                outputRoot,
                "--version",
                "9.9.9",
            ],
            output,
            error,
            new RecordingProcessRunner()
        );

        Assert.Equal(0, exitCode);
        var checksums = await File.ReadAllTextAsync(Path.Combine(outputRoot, "SHA256SUMS.txt"));
        var manifest = await File.ReadAllTextAsync(
            Path.Combine(outputRoot, "release-manifest.json")
        );
        Assert.Contains(
            "artifacts/buildtool/packages/CodexCliPlus.Setup.Online.9.9.9.exe",
            checksums,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "artifacts/buildtool/packages/CodexCliPlus.Setup.Offline.9.9.9.exe",
            checksums,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "artifacts/buildtool/packages/CodexCliPlus.Update.9.9.9.win-x64.zip",
            checksums,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "artifacts/buildtool/packages/CodexCliPlus.Setup.Online.9.9.9.win-x64.zip",
            checksums,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "artifacts/buildtool/packages/CodexCliPlus.Setup.Offline.9.9.9.win-x64.zip",
            checksums,
            StringComparison.Ordinal
        );
        Assert.Contains("CodexCliPlus.Setup.Online.9.9.9.exe", manifest, StringComparison.Ordinal);
        Assert.Contains("CodexCliPlus.Setup.Offline.9.9.9.exe", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("asset-manifest.json", checksums, StringComparison.Ordinal);
        Assert.DoesNotContain("publish-manifest.json", checksums, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"9.9.9\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"sha256\"", manifest, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ExportPublicReleaseCopiesOnlyInstallableAssetsAndMetadata()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(repositoryRoot, "artifacts", "buildtool");
        var packageRoot = Path.Combine(outputRoot, "packages");
        Directory.CreateDirectory(packageRoot);
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "CodexCliPlus.Setup.Online.9.9.9.exe"),
            "online installer"
        );
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "CodexCliPlus.Setup.Online.9.9.9.win-x64.zip"),
            "online internal staging package"
        );
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "CodexCliPlus.Setup.Offline.9.9.9.exe"),
            "offline installer"
        );
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "CodexCliPlus.Setup.Offline.9.9.9.win-x64.zip"),
            "internal staging package"
        );
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "CodexCliPlus.Update.9.9.9.win-x64.zip"),
            "update"
        );
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            [
                "export-public-release",
                "--repo-root",
                repositoryRoot,
                "--output",
                outputRoot,
                "--version",
                "9.9.9",
            ],
            output,
            error,
            new RecordingProcessRunner()
        );

        Assert.Equal(0, exitCode);
        var publicRoot = Path.Combine(outputRoot, "public-release");
        Assert.True(File.Exists(Path.Combine(publicRoot, "CodexCliPlus.Setup.Online.9.9.9.exe")));
        Assert.True(File.Exists(Path.Combine(publicRoot, "CodexCliPlus.Setup.Offline.9.9.9.exe")));
        Assert.True(File.Exists(Path.Combine(publicRoot, "CodexCliPlus.Update.9.9.9.win-x64.zip")));
        Assert.True(File.Exists(Path.Combine(publicRoot, "release-manifest.json")));
        Assert.False(File.Exists(Path.Combine(publicRoot, "SHA256SUMS.txt")));
        Assert.False(
            File.Exists(Path.Combine(publicRoot, "CodexCliPlus.Setup.Online.9.9.9.win-x64.zip"))
        );
        Assert.False(
            File.Exists(Path.Combine(publicRoot, "CodexCliPlus.Setup.Offline.9.9.9.win-x64.zip"))
        );
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task VerifyPackageRejectsInstallerExecutableWithoutPeHeader()
    {
        var repositoryRoot = CreateRepositoryWithBackendAssets();
        var outputRoot = Path.Combine(_rootDirectory, "out");
        var packageRoot = Path.Combine(outputRoot, "packages");
        Directory.CreateDirectory(packageRoot);
        CreateInstallerPackage(
            packageRoot,
            "Online",
            CreateStubExecutableBytes(),
            CreateStubExecutableBytes()
        );
        CreateInstallerPackage(packageRoot, "Offline", new byte[80], CreateStubExecutableBytes());
        CreateUpdatePackage(packageRoot);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            [
                "verify-package",
                "--repo-root",
                repositoryRoot,
                "--output",
                outputRoot,
                "--version",
                "9.9.9",
            ],
            output,
            error,
            new RecordingProcessRunner()
        );

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
        CreateInstallerPackage(
            packageRoot,
            "Online",
            CreateStubExecutableBytes(),
            CreateStubExecutableBytes()
        );
        CreateStubExecutable(Path.Combine(packageRoot, "CodexCliPlus.Setup.Offline.9.9.9.exe"));
        CreateZipWithExecutableEntries(
            Path.Combine(packageRoot, "CodexCliPlus.Setup.Offline.9.9.9.win-x64.zip"),
            new Dictionary<string, byte[]>
            {
                ["app-package/CodexCliPlus.exe"] = Encoding.UTF8.GetBytes("codexcliplus"),
                ["app-package/assets/webui/upstream/dist/index.html"] = Encoding.UTF8.GetBytes(
                    "<html></html>"
                ),
                ["app-package/assets/webui/upstream/dist/assets/app.js"] = Encoding.UTF8.GetBytes(
                    "console.log('ok');"
                ),
                ["app-package/assets/webui/upstream/sync.json"] = Encoding.UTF8.GetBytes("{}"),
                ["mica-setup.json"] = Encoding.UTF8.GetBytes("{}"),
                ["micasetup.json"] = Encoding.UTF8.GetBytes("{}"),
                ["output/CodexCliPlus.Setup.Offline.9.9.9.exe"] = Encoding.UTF8.GetBytes("bad"),
                [
                    $"app-package/{WebView2RuntimeAssets.PackagedDirectory}/{WebView2RuntimeAssets.BootstrapperFileName}"
                ] = CreateStubExecutableBytes(),
                [
                    $"app-package/{WebView2RuntimeAssets.PackagedDirectory}/{WebView2RuntimeAssets.StandaloneX64FileName}"
                ] = CreateStubExecutableBytes(),
                ["app-package/packaging/uninstall-cleanup.json"] = Encoding.UTF8.GetBytes("{}"),
                ["app-package/packaging/dependency-precheck.json"] = Encoding.UTF8.GetBytes("{}"),
                ["app-package/packaging/update-policy.json"] = Encoding.UTF8.GetBytes("{}"),
            }
        );
        CreateUpdatePackage(packageRoot);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            [
                "verify-package",
                "--repo-root",
                repositoryRoot,
                "--output",
                outputRoot,
                "--version",
                "9.9.9",
            ],
            output,
            error,
            new RecordingProcessRunner()
        );

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "output/CodexCliPlus.Setup.Offline.9.9.9.exe",
            error.ToString(),
            StringComparison.Ordinal
        );
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
            new BuildOptions(
                "build-webui",
                repositoryRoot,
                outputRoot,
                "Release",
                "win-x64",
                "9.9.9"
            ),
            logger,
            runner,
            new NoOpSigningService()
        );

        var exitCode = await WebUiCommands.BuildVendoredAsync(context);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, runner.Calls.Count);
        Assert.All(
            runner.Calls,
            call => Assert.Equal(context.WebUiBuildSourceRoot, call.WorkingDirectory)
        );
        Assert.DoesNotContain(
            runner.Calls,
            call =>
                string.Equals(
                    call.WorkingDirectory,
                    context.WebUiSourceRoot,
                    StringComparison.OrdinalIgnoreCase
                )
        );
        Assert.Equal(
            "overlay",
            File.ReadAllText(Path.Combine(context.WebUiBuildSourceRoot, "src", "message.txt"))
        );
        Assert.Equal(
            "overlay",
            File.ReadAllText(Path.Combine(context.WebUiGeneratedDistRoot, "index.html"))
        );
        Assert.True(File.Exists(Path.Combine(context.WebUiGeneratedDistRoot, "assets", "app.js")));
        Assert.True(
            File.Exists(Path.Combine(context.WebUiGeneratedDistRoot, "bundle-report.json"))
        );
        Assert.True(File.Exists(Path.Combine(context.WebUiGeneratedRoot, "sync.json")));
        Assert.Equal(
            "upstream",
            File.ReadAllText(Path.Combine(context.WebUiSourceRoot, "src", "message.txt"))
        );
        Assert.True(
            File.Exists(Path.Combine(context.WebUiBuildSourceRoot, "node_modules", ".installed"))
        );
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task BuildVendoredReusesCachedNodeModulesForSamePackageLock()
    {
        var repositoryRoot = CreateRepositoryWithVendoredWebUiOverlay();
        var outputRoot = Path.Combine(_rootDirectory, "out-cache");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var logger = new BuildLogger(output, error);
        var runner = new OverlayRecordingProcessRunner();
        var context = new BuildContext(
            new BuildOptions(
                "build-webui",
                repositoryRoot,
                outputRoot,
                "Release",
                "win-x64",
                "9.9.9"
            ),
            logger,
            runner,
            new NoOpSigningService()
        );

        Assert.Equal(0, await WebUiCommands.BuildVendoredAsync(context));
        Assert.Equal(0, await WebUiCommands.BuildVendoredAsync(context));

        Assert.Equal(3, runner.Calls.Count);
        Assert.Equal(1, runner.Calls.Count(call => call.Arguments.SequenceEqual(["ci"])));
        Assert.Equal(2, runner.Calls.Count(call => call.Arguments.SequenceEqual(["run", "build"])));
        Assert.Contains("using cached vendored WebUI dependencies", output.ToString());
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
            Encoding.UTF8
        );
        using var output = new StringWriter();
        using var error = new StringWriter();
        var logger = new BuildLogger(output, error);

        var exitCode = await new ProcessRunner().RunAsync(
            "powershell",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath],
            _rootDirectory,
            logger
        );

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
            Encoding.UTF8
        );
        using var output = new StringWriter();
        using var error = new StringWriter();
        var logger = new BuildLogger(output, error);

        var exitCode = await new ProcessRunner().RunAsync(
            "powershell",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath],
            _rootDirectory,
            logger
        );

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

        var exception = Assert.Throws<FileNotFoundException>(() =>
            SafeFileSystem.RequirePublishRoot(publishRoot)
        );

        Assert.Contains(
            Path.Combine("assets", "webui", "upstream"),
            exception.FileName ?? exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private string CreateRepositoryWithBackendAssets()
    {
        var repositoryRoot = Path.Combine(_rootDirectory, "repo");
        Directory.CreateDirectory(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "CodexCliPlus.sln"), string.Empty);
        BackendSourceBuildProcessRunner.CreatePinnedBackendSource(
            Path.Combine(repositoryRoot, "resources", "backend", "source")
        );
        return repositoryRoot;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CodexCliPlus.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate CodexCliPlus.sln.");
    }

    private string CreateRepositoryWithVendoredWebUiOverlay()
    {
        var repositoryRoot = Path.Combine(_rootDirectory, "webui-repo");
        Directory.CreateDirectory(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "CodexCliPlus.sln"), string.Empty);

        var upstreamRoot = Path.Combine(repositoryRoot, "resources", "webui", "upstream");
        var sourceRoot = Path.Combine(upstreamRoot, "source");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "src"));
        File.WriteAllText(
            Path.Combine(sourceRoot, "package.json"),
            "{\"name\":\"test-webui\",\"private\":true}"
        );
        File.WriteAllText(
            Path.Combine(sourceRoot, "package-lock.json"),
            "{\"name\":\"test-webui\",\"lockfileVersion\":3}"
        );
        File.WriteAllText(Path.Combine(sourceRoot, "src", "message.txt"), "upstream");
        File.WriteAllText(Path.Combine(upstreamRoot, "sync.json"), "{}");

        var overlayRoot = Path.Combine(
            repositoryRoot,
            "resources",
            "webui",
            "modules",
            "cpa-uv-overlay"
        );
        Directory.CreateDirectory(Path.Combine(overlayRoot, "source", "src"));
        File.WriteAllText(Path.Combine(overlayRoot, "module.json"), "{\"id\":\"cpa-uv-overlay\"}");
        File.WriteAllText(Path.Combine(overlayRoot, "source", "src", "message.txt"), "overlay");

        return repositoryRoot;
    }

    private static void CreateZipWithExecutableEntries(
        string packagePath,
        IReadOnlyDictionary<string, byte[]> entries
    )
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        foreach (var entryPair in entries)
        {
            var entry = archive.CreateEntry(entryPair.Key);
            using var stream = entry.Open();
            stream.Write(entryPair.Value, 0, entryPair.Value.Length);
        }
    }

    private static void CreateInstallerPackage(
        string packageRoot,
        string packageMoniker,
        byte[] installerBytes,
        byte[] stagingInstallerBytes
    )
    {
        var installerName = $"CodexCliPlus.Setup.{packageMoniker}.9.9.9.exe";
        File.WriteAllBytes(Path.Combine(packageRoot, installerName), installerBytes);
        CreateInstallerStagingZip(
            Path.Combine(packageRoot, $"CodexCliPlus.Setup.{packageMoniker}.9.9.9.win-x64.zip"),
            installerName,
            stagingInstallerBytes
        );
    }

    private static void CreateUpdatePackage(string packageRoot)
    {
        CreateZipWithExecutableEntries(
            Path.Combine(packageRoot, "CodexCliPlus.Update.9.9.9.win-x64.zip"),
            new Dictionary<string, byte[]>
            {
                ["update-manifest.json"] = Encoding.UTF8.GetBytes("{}"),
                ["payload/CodexCliPlus.exe"] = CreateStubExecutableBytes(),
            }
        );
    }

    private static void CreateInstallerStagingZip(
        string packagePath,
        string installerName,
        byte[] installerBytes
    )
    {
        var entries = new Dictionary<string, byte[]>
        {
            ["app-package/CodexCliPlus.exe"] = Encoding.UTF8.GetBytes("codexcliplus"),
            ["app-package/assets/webui/upstream/dist/index.html"] = Encoding.UTF8.GetBytes(
                "<html></html>"
            ),
            ["app-package/assets/webui/upstream/dist/assets/app.js"] = Encoding.UTF8.GetBytes(
                "console.log('ok');"
            ),
            ["app-package/assets/webui/upstream/sync.json"] = Encoding.UTF8.GetBytes("{}"),
            ["mica-setup.json"] = Encoding.UTF8.GetBytes("{}"),
            ["micasetup.json"] = Encoding.UTF8.GetBytes("{}"),
            [$"output/{installerName}"] = installerBytes,
            [
                $"app-package/{WebView2RuntimeAssets.PackagedDirectory}/{WebView2RuntimeAssets.BootstrapperFileName}"
            ] = CreateStubExecutableBytes(),
            ["app-package/packaging/uninstall-cleanup.json"] = Encoding.UTF8.GetBytes("{}"),
            ["app-package/packaging/dependency-precheck.json"] = Encoding.UTF8.GetBytes("{}"),
            ["app-package/packaging/update-policy.json"] = Encoding.UTF8.GetBytes("{}"),
        };

        if (installerName.Contains(".Offline.", StringComparison.OrdinalIgnoreCase))
        {
            entries[
                $"app-package/{WebView2RuntimeAssets.PackagedDirectory}/{WebView2RuntimeAssets.StandaloneX64FileName}"
            ] = CreateStubExecutableBytes();
        }

        CreateZipWithExecutableEntries(packagePath, entries);
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
            IReadOnlyDictionary<string, string?>? environment = null,
            CancellationToken cancellationToken = default
        )
        {
            logger.Info($"{fileName} {string.Join(" ", arguments)}");
            if (fileName == "go" && arguments.Count > 0 && arguments[0] == "build")
            {
                var outputIndex = arguments.ToList().IndexOf("-o") + 1;
                Assert.True(outputIndex > 0);
                Directory.CreateDirectory(Path.GetDirectoryName(arguments[outputIndex])!);
                File.WriteAllBytes(arguments[outputIndex], CreateStubExecutableBytes());
            }

            return Task.FromResult(0);
        }
    }

    private sealed record ProcessCall(
        string FileName,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        IReadOnlyDictionary<string, string?> Environment
    );

    private sealed class OverlayRecordingProcessRunner : IProcessRunner
    {
        public List<ProcessCall> Calls { get; } = [];

        public Task<int> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            BuildLogger logger,
            IReadOnlyDictionary<string, string?>? environment = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add(
                new ProcessCall(
                    fileName,
                    arguments.ToArray(),
                    workingDirectory,
                    environment?.ToDictionary() ?? new Dictionary<string, string?>()
                )
            );
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
                var distRoot = Path.Combine(
                    Directory.GetParent(workingDirectory)!.FullName,
                    "dist"
                );
                Directory.CreateDirectory(distRoot);
                File.WriteAllText(
                    Path.Combine(distRoot, "index.html"),
                    File.ReadAllText(mergedMarkerPath)
                );
                var assetsRoot = Path.Combine(distRoot, "assets");
                Directory.CreateDirectory(assetsRoot);
                File.WriteAllText(Path.Combine(assetsRoot, "app.js"), "console.log('ok');");
                return Task.FromResult(0);
            }

            return Task.FromResult(0);
        }
    }

    private sealed class BackendSourceBuildProcessRunner : IProcessRunner
    {
        public List<ProcessCall> Calls { get; } = [];

        public Task<int> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            BuildLogger logger,
            IReadOnlyDictionary<string, string?>? environment = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add(
                new ProcessCall(
                    fileName,
                    arguments.ToArray(),
                    workingDirectory,
                    environment?.ToDictionary() ?? new Dictionary<string, string?>()
                )
            );
            logger.Info($"{fileName} {string.Join(" ", arguments)}");

            if (
                fileName == "git"
                && arguments.Count >= 4
                && arguments[0] == "clone"
                && arguments.Contains("--no-checkout")
            )
            {
                CreatePinnedBackendSource(arguments[^1]);
                return Task.FromResult(0);
            }

            if (fileName == "go" && arguments.Count > 0 && arguments[0] == "build")
            {
                var outputIndex = arguments.ToList().IndexOf("-o") + 1;
                Assert.True(outputIndex > 0);
                Directory.CreateDirectory(Path.GetDirectoryName(arguments[outputIndex])!);
                File.WriteAllBytes(arguments[outputIndex], CreateStubExecutableBytes());
            }

            return Task.FromResult(0);
        }

        public static void CreatePinnedBackendSource(string sourceRoot)
        {
            Directory.CreateDirectory(sourceRoot);
            File.WriteAllText(Path.Combine(sourceRoot, "LICENSE"), "license");
            File.WriteAllText(Path.Combine(sourceRoot, "README.md"), "readme");
            File.WriteAllText(Path.Combine(sourceRoot, "README_CN.md"), "readme cn");
            File.WriteAllText(Path.Combine(sourceRoot, "config.example.yaml"), "config");
            File.WriteAllText(
                Path.Combine(sourceRoot, "go.mod"),
                """
                module github.com/router-for-me/CLIProxyAPI/v6

                require github.com/go-git/go-git/v6 v6.0.0-20251009132922-75a182125145
                """
            );
            File.WriteAllText(Path.Combine(sourceRoot, "go.sum"), string.Empty);

            var storeRoot = Path.Combine(sourceRoot, "internal", "store");
            Directory.CreateDirectory(storeRoot);
            var configRoot = Path.Combine(sourceRoot, "internal", "config");
            Directory.CreateDirectory(configRoot);
            var apiRoot = Path.Combine(sourceRoot, "internal", "api");
            Directory.CreateDirectory(apiRoot);
            var cmdRoot = Path.Combine(sourceRoot, "internal", "cmd");
            Directory.CreateDirectory(cmdRoot);
            var serverCmdRoot = Path.Combine(sourceRoot, "cmd", "server");
            Directory.CreateDirectory(serverCmdRoot);
            var runtimeExecutorRoot = Path.Combine(sourceRoot, "internal", "runtime", "executor");
            Directory.CreateDirectory(runtimeExecutorRoot);
            var runtimeExecutorHelpsRoot = Path.Combine(runtimeExecutorRoot, "helps");
            Directory.CreateDirectory(runtimeExecutorHelpsRoot);
            var translatorRoot = Path.Combine(sourceRoot, "internal", "translator");
            Directory.CreateDirectory(translatorRoot);
            var watcherSynthesizerRoot = Path.Combine(
                sourceRoot,
                "internal",
                "watcher",
                "synthesizer"
            );
            Directory.CreateDirectory(watcherSynthesizerRoot);
            var sdkAuthRoot = Path.Combine(sourceRoot, "sdk", "auth");
            Directory.CreateDirectory(sdkAuthRoot);
            var sdkCliproxyRoot = Path.Combine(sourceRoot, "sdk", "cliproxy");
            Directory.CreateDirectory(sdkCliproxyRoot);
            var managementHandlersRoot = Path.Combine(
                sourceRoot,
                "internal",
                "api",
                "handlers",
                "management"
            );
            Directory.CreateDirectory(managementHandlersRoot);
            File.WriteAllText(
                Path.Combine(configRoot, "config.go"),
                """
                package config

                import (
                    "fmt"
                    "gopkg.in/yaml.v3"
                )

                type Config struct {
                    SDKConfig `yaml:",inline"`
                    RemoteManagement RemoteManagement `yaml:"remote-management"`
                    GeminiKey []GeminiKey `yaml:"gemini-api-key"`
                    CodexKey []CodexKey `yaml:"codex-api-key"`
                    ClaudeKey []ClaudeKey `yaml:"claude-api-key"`
                    ClaudeHeaderDefaults ClaudeHeaderDefaults `yaml:"claude-header-defaults"`
                    OpenAICompatibility []OpenAICompatibility `yaml:"openai-compatibility"`
                    VertexCompatAPIKey []VertexCompatKey `yaml:"vertex-api-key"`
                    AmpCode AmpCode `yaml:"ampcode"`
                    QuotaExceeded QuotaExceeded `yaml:"quota-exceeded"`
                    AntigravitySignatureCacheEnabled *bool `yaml:"antigravity-signature-cache-enabled"`
                    AntigravitySignatureBypassStrict *bool `yaml:"antigravity-signature-bypass-strict"`
                    OAuthExcludedModels map[string][]string `yaml:"oauth-excluded-models"`
                    OAuthModelAlias map[string][]OAuthModelAlias `yaml:"oauth-model-alias"`
                }

                type SDKConfig struct { APIKeys []string `yaml:"api-keys"` }
                type RemoteManagement struct { SecretKey string `yaml:"secret-key"` }
                type GeminiKey struct { APIKey string `yaml:"api-key"`; Headers map[string]string `yaml:"headers"` }
                type CodexKey struct { APIKey string `yaml:"api-key"`; Headers map[string]string `yaml:"headers"` }
                type ClaudeKey struct { APIKey string `yaml:"api-key"`; Headers map[string]string `yaml:"headers"` }
                type ClaudeHeaderDefaults struct{}
                type VertexCompatKey struct { APIKey string `yaml:"api-key"`; Headers map[string]string `yaml:"headers"` }
                type QuotaExceeded struct { AntigravityCredits bool `yaml:"antigravity-credits"` }
                type OAuthModelAlias struct { Name string `yaml:"name"`; Alias string `yaml:"alias"`; Fork bool `yaml:"fork"` }
                type OpenAICompatibility struct {
                    Headers map[string]string `yaml:"headers"`
                    APIKeyEntries []OpenAICompatibilityAPIKey `yaml:"api-key-entries"`
                }
                type OpenAICompatibilityAPIKey struct { APIKey string `yaml:"api-key"` }
                type AmpCode struct {
                    UpstreamAPIKey string `yaml:"upstream-api-key"`
                    UpstreamAPIKeys []AmpUpstreamAPIKeyEntry `yaml:"upstream-api-keys"`
                }
                type AmpUpstreamAPIKeyEntry struct {
                    UpstreamAPIKey string `yaml:"upstream-api-key"`
                    APIKeys []string `yaml:"api-keys"`
                }

                func LoadConfigOptional(data []byte, optional bool) (*Config, error) {
                    var cfg Config
                    if err := yaml.Unmarshal(data, &cfg); err != nil {
                        return nil, fmt.Errorf("failed to parse config file: %w", err)
                    }
                    // Normalize global OAuth model name aliases.
                    cfg.SanitizeOAuthModelAlias()
                    // NOTE: Startup legacy key migration is intentionally disabled.
                    return &cfg, nil
                }

                func (cfg *Config) SanitizeOAuthModelAlias() {}

                func SaveConfigPreserveComments(configFile string, cfg *Config) error {
                    persistCfg := cfg
                    data, err := yaml.Marshal(persistCfg)
                    if err != nil {
                        return err
                    }
                    _ = data
                    removeLegacyAmpKeys(original.Content[0])
                    removeLegacyGenerativeLanguageKeys(original.Content[0])
                    return nil
                }

                type Node struct{}
                func removeLegacyAmpKeys(root any) {}
                func removeLegacyGenerativeLanguageKeys(root any) {}
                func removeMapKey(root any, key string) {}
                func findMapKeyIndex(root any, key string) int { return -1 }
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(managementHandlersRoot, "config_basic.go"),
                """
                package management

                import "github.com/router-for-me/CLIProxyAPI/v6/internal/config"

                func WriteConfig(path string, data []byte) error {
                    data = config.NormalizeCommentIndentation(data)
                    _ = path
                    _ = data
                    return nil
                }
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(managementHandlersRoot, "auth_files.go"),
                """
                package management

                import (
                    "context"
                    "fmt"
                    "os"
                    "path/filepath"

                    "github.com/gin-gonic/gin"
                )

                type Handler struct {
                    cfg *AuthConfig
                }

                type AuthConfig struct {
                    AuthDir string
                }

                func (h *Handler) writeAuthFile(ctx context.Context, name string, data []byte) error {
                    _ = ctx
                    dst := filepath.Join(h.cfg.AuthDir, filepath.Base(name))
                    auth, err := h.buildAuthFromFileData(dst, data)
                    if err != nil {
                        return err
                    }
                    if errWrite := os.WriteFile(dst, data, 0o600); errWrite != nil {
                        return fmt.Errorf("failed to write file: %w", errWrite)
                    }
                    _ = auth
                    return nil
                }

                func (h *Handler) buildAuthFromFileData(path string, data []byte) (string, error) {
                    return path + string(data), nil
                }

                func ping(c *gin.Context) {
                    c.JSON(200, gin.H{"status": "ok"})
                }
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(watcherSynthesizerRoot, "config.go"),
                """
                package synthesizer

                func (s *ConfigSynthesizer) Synthesize(ctx *SynthesisContext) ([]*Auth, error) {
                    out := make([]*Auth, 0, 32)
                    if ctx == nil || ctx.Config == nil {
                        return out, nil
                    }

                    // Gemini API Keys
                    out = append(out, s.synthesizeGeminiKeys(ctx)...)
                    // Claude API Keys
                    out = append(out, s.synthesizeClaudeKeys(ctx)...)
                    // Codex API Keys
                    out = append(out, s.synthesizeCodexKeys(ctx)...)
                    // OpenAI-compat
                    out = append(out, s.synthesizeOpenAICompat(ctx)...)
                    // Vertex-compat
                    out = append(out, s.synthesizeVertexCompat(ctx)...)

                    return out, nil
                }
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(watcherSynthesizerRoot, "file.go"),
                """
                package synthesizer

                func synthesizeFileAuths(ctx *SynthesisContext, fullPath string, data []byte) []*Auth {
                    provider := "gemini"
                    if provider == "gemini" {
                        provider = "gemini-cli"
                    }
                    _ = provider
                    return nil
                }
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(sdkCliproxyRoot, "providers.go"),
                """
                package cliproxy

                func load() {
                    geminiCount, vertexCompatCount, claudeCount, codexCount, openAICompat := watcher.BuildAPIKeyClients(cfg)
                    _ = geminiCount
                    _ = vertexCompatCount
                    _ = claudeCount
                    _ = codexCount
                    _ = openAICompat
                }
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(sdkCliproxyRoot, "service.go"),
                """
                package cliproxy

                func newDefaultAuthManager() *sdkAuth.Manager {
                    return sdkAuth.NewManager(
                        sdkAuth.GetTokenStore(),
                        sdkAuth.NewGeminiAuthenticator(),
                        sdkAuth.NewCodexAuthenticator(),
                        sdkAuth.NewClaudeAuthenticator(),
                    )
                }

                func (s *Service) ensureExecutorsForAuthWithMode(a *coreauth.Auth, forceReplace bool) {
                    if s == nil || s.coreManager == nil || a == nil {
                        return
                    }
                    if strings.EqualFold(strings.TrimSpace(a.Provider), "codex") {
                        return
                    }
                    // Skip disabled auth entries when (re)binding executors.
                    // Disabled auths can linger during config reloads (e.g., removed OpenAI-compat entries)
                    // and must not override active provider executors.
                    if a.Disabled {
                        return
                    }
                    if compatProviderKey, _, isCompat := openAICompatInfoFromAuth(a); isCompat {
                        if compatProviderKey == "" {
                            compatProviderKey = strings.ToLower(strings.TrimSpace(a.Provider))
                        }
                        if compatProviderKey == "" {
                            compatProviderKey = "openai-compatibility"
                        }
                        s.coreManager.RegisterExecutor(executor.NewOpenAICompatExecutor(compatProviderKey, s.cfg))
                        return
                    }
                    switch strings.ToLower(a.Provider) {
                    case "gemini":
                        s.coreManager.RegisterExecutor(executor.NewGeminiExecutor(s.cfg))
                    case "vertex":
                        s.coreManager.RegisterExecutor(executor.NewGeminiVertexExecutor(s.cfg))
                    case "gemini-cli":
                        s.coreManager.RegisterExecutor(executor.NewGeminiCLIExecutor(s.cfg))
                    case "aistudio":
                        if s.wsGateway != nil {
                            s.coreManager.RegisterExecutor(executor.NewAIStudioExecutor(s.cfg, a.ID, s.wsGateway))
                        }
                        return
                    case "antigravity":
                        s.coreManager.RegisterExecutor(executor.NewAntigravityExecutor(s.cfg))
                    case "claude":
                        s.coreManager.RegisterExecutor(executor.NewClaudeExecutor(s.cfg))
                    case "kimi":
                        s.coreManager.RegisterExecutor(executor.NewKimiExecutor(s.cfg))
                    default:
                        providerKey := strings.ToLower(strings.TrimSpace(a.Provider))
                        if providerKey == "" {
                            providerKey = "openai-compatibility"
                        }
                        s.coreManager.RegisterExecutor(executor.NewOpenAICompatExecutor(providerKey, s.cfg))
                    }
                }

                func (s *Service) Run() {
                    s.ensureWebsocketGateway()
                    if s.server != nil && s.wsGateway != nil {
                    }
                }
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(cmdRoot, "auth_manager.go"),
                """
                package cmd

                func newAuthManager() *sdkAuth.Manager {
                    store := sdkAuth.GetTokenStore()
                    manager := sdkAuth.NewManager(store,
                        sdkAuth.NewGeminiAuthenticator(),
                        sdkAuth.NewCodexAuthenticator(),
                        sdkAuth.NewClaudeAuthenticator(),
                        sdkAuth.NewAntigravityAuthenticator(),
                        sdkAuth.NewKimiAuthenticator(),
                    )
                    return manager
                }
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(sdkAuthRoot, "refresh_registry.go"),
                """
                package auth

                func init() {
                    registerRefreshLead("codex", func() Authenticator { return NewCodexAuthenticator() })
                    registerRefreshLead("claude", func() Authenticator { return NewClaudeAuthenticator() })
                    registerRefreshLead("gemini", func() Authenticator { return NewGeminiAuthenticator() })
                    registerRefreshLead("gemini-cli", func() Authenticator { return NewGeminiAuthenticator() })
                    registerRefreshLead("antigravity", func() Authenticator { return NewAntigravityAuthenticator() })
                    registerRefreshLead("kimi", func() Authenticator { return NewKimiAuthenticator() })
                }
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(apiRoot, "server.go"),
                """
                package api

                func NewServer() {
                    // Register Amp module using V2 interface with Context
                    s.ampModule = ampmodule.NewLegacy(accessManager, AuthMiddleware(accessManager))
                    ctx := modules.Context{
                        Engine:         engine,
                        BaseHandler:    s.handlers,
                        Config:         cfg,
                        AuthMiddleware: AuthMiddleware(accessManager),
                    }
                    if err := modules.RegisterModule(ctx, s.ampModule); err != nil {
                        log.Errorf("Failed to register Amp module: %v", err)
                    }
                }

                func (s *Server) setupRoutes() {
                    openaiHandlers := openai.NewOpenAIAPIHandler(s.handlers)
                    geminiHandlers := gemini.NewGeminiAPIHandler(s.handlers)
                    geminiCLIHandlers := gemini.NewGeminiCLIAPIHandler(s.handlers)
                    claudeCodeHandlers := claude.NewClaudeCodeAPIHandler(s.handlers)
                    openaiResponsesHandlers := openai.NewOpenAIResponsesAPIHandler(s.handlers)

                    {
                        v1.GET("/models", s.unifiedModelsHandler(openaiHandlers, claudeCodeHandlers))
                        v1.POST("/chat/completions", openaiHandlers.ChatCompletions)
                        v1.POST("/completions", openaiHandlers.Completions)
                        v1.POST("/images/generations", openaiHandlers.ImagesGenerations)
                        v1.POST("/images/edits", openaiHandlers.ImagesEdits)
                        v1.POST("/messages", claudeCodeHandlers.ClaudeMessages)
                        v1.POST("/messages/count_tokens", claudeCodeHandlers.ClaudeCountTokens)
                        v1.GET("/responses", openaiResponsesHandlers.ResponsesWebsocket)
                        v1.POST("/responses", openaiResponsesHandlers.Responses)
                        v1.POST("/responses/compact", openaiResponsesHandlers.Compact)
                    }

                    // Gemini compatible API routes
                    v1beta := s.engine.Group("/v1beta")
                    v1beta.Use(AuthMiddleware(s.accessManager))
                    {
                        v1beta.GET("/models", geminiHandlers.GeminiModels)
                        v1beta.POST("/models/*action", geminiHandlers.GeminiHandler)
                        v1beta.GET("/models/*action", geminiHandlers.GeminiGetHandler)
                    }

                    s.engine.POST("/v1internal:method", geminiCLIHandlers.CLIHandler)

                    s.engine.GET("/anthropic/callback", func(c *gin.Context) {
                        code := c.Query("code")
                        state := c.Query("state")
                        errStr := c.Query("error")
                        if errStr == "" {
                            errStr = c.Query("error_description")
                        }
                        if state != "" {
                            _, _ = managementHandlers.WriteOAuthCallbackFileForPendingSession(s.cfg.AuthDir, "anthropic", state, code, errStr)
                        }
                        c.Header("Content-Type", "text/html; charset=utf-8")
                        c.String(http.StatusOK, oauthCallbackSuccessHTML)
                    })

                    s.engine.GET("/codex/callback", func(c *gin.Context) {})

                    s.engine.GET("/google/callback", func(c *gin.Context) {
                        code := c.Query("code")
                        state := c.Query("state")
                        errStr := c.Query("error")
                        if errStr == "" {
                            errStr = c.Query("error_description")
                        }
                        if state != "" {
                            _, _ = managementHandlers.WriteOAuthCallbackFileForPendingSession(s.cfg.AuthDir, "gemini", state, code, errStr)
                        }
                        c.Header("Content-Type", "text/html; charset=utf-8")
                        c.String(http.StatusOK, oauthCallbackSuccessHTML)
                    })

                    s.engine.GET("/antigravity/callback", func(c *gin.Context) {
                        code := c.Query("code")
                        state := c.Query("state")
                        errStr := c.Query("error")
                        if errStr == "" {
                            errStr = c.Query("error_description")
                        }
                        if state != "" {
                            _, _ = managementHandlers.WriteOAuthCallbackFileForPendingSession(s.cfg.AuthDir, "antigravity", state, code, errStr)
                        }
                        c.Header("Content-Type", "text/html; charset=utf-8")
                        c.String(http.StatusOK, oauthCallbackSuccessHTML)
                    })
                }

                func (s *Server) registerManagementRoutes() {
                    mgmt.GET("/gemini-api-key", s.mgmt.GetGeminiKeys)
                    mgmt.PUT("/gemini-api-key", s.mgmt.PutGeminiKeys)
                    mgmt.PATCH("/gemini-api-key", s.mgmt.PatchGeminiKey)
                    mgmt.DELETE("/gemini-api-key", s.mgmt.DeleteGeminiKey)

                    mgmt.GET("/ampcode", s.mgmt.GetAmpCode)
                    mgmt.GET("/ampcode/upstream-url", s.mgmt.GetAmpUpstreamURL)
                    mgmt.PUT("/ampcode/upstream-url", s.mgmt.PutAmpUpstreamURL)
                    mgmt.PATCH("/ampcode/upstream-url", s.mgmt.PutAmpUpstreamURL)
                    mgmt.DELETE("/ampcode/upstream-url", s.mgmt.DeleteAmpUpstreamURL)
                    mgmt.GET("/ampcode/upstream-api-key", s.mgmt.GetAmpUpstreamAPIKey)
                    mgmt.PUT("/ampcode/upstream-api-key", s.mgmt.PutAmpUpstreamAPIKey)
                    mgmt.PATCH("/ampcode/upstream-api-key", s.mgmt.PutAmpUpstreamAPIKey)
                    mgmt.DELETE("/ampcode/upstream-api-key", s.mgmt.DeleteAmpUpstreamAPIKey)
                    mgmt.GET("/ampcode/restrict-management-to-localhost", s.mgmt.GetAmpRestrictManagementToLocalhost)
                    mgmt.PUT("/ampcode/restrict-management-to-localhost", s.mgmt.PutAmpRestrictManagementToLocalhost)
                    mgmt.PATCH("/ampcode/restrict-management-to-localhost", s.mgmt.PutAmpRestrictManagementToLocalhost)
                    mgmt.GET("/ampcode/model-mappings", s.mgmt.GetAmpModelMappings)
                    mgmt.PUT("/ampcode/model-mappings", s.mgmt.PutAmpModelMappings)
                    mgmt.PATCH("/ampcode/model-mappings", s.mgmt.PatchAmpModelMappings)
                    mgmt.DELETE("/ampcode/model-mappings", s.mgmt.DeleteAmpModelMappings)
                    mgmt.GET("/ampcode/force-model-mappings", s.mgmt.GetAmpForceModelMappings)
                    mgmt.PUT("/ampcode/force-model-mappings", s.mgmt.PutAmpForceModelMappings)
                    mgmt.PATCH("/ampcode/force-model-mappings", s.mgmt.PutAmpForceModelMappings)
                    mgmt.GET("/ampcode/upstream-api-keys", s.mgmt.GetAmpUpstreamAPIKeys)
                    mgmt.PUT("/ampcode/upstream-api-keys", s.mgmt.PutAmpUpstreamAPIKeys)
                    mgmt.PATCH("/ampcode/upstream-api-keys", s.mgmt.PatchAmpUpstreamAPIKeys)
                    mgmt.DELETE("/ampcode/upstream-api-keys", s.mgmt.DeleteAmpUpstreamAPIKeys)

                    mgmt.GET("/claude-api-key", s.mgmt.GetClaudeKeys)
                    mgmt.PUT("/claude-api-key", s.mgmt.PutClaudeKeys)
                    mgmt.PATCH("/claude-api-key", s.mgmt.PatchClaudeKey)
                    mgmt.DELETE("/claude-api-key", s.mgmt.DeleteClaudeKey)

                    mgmt.GET("/vertex-api-key", s.mgmt.GetVertexCompatKeys)
                    mgmt.PUT("/vertex-api-key", s.mgmt.PutVertexCompatKeys)
                    mgmt.PATCH("/vertex-api-key", s.mgmt.PatchVertexCompatKey)
                    mgmt.DELETE("/vertex-api-key", s.mgmt.DeleteVertexCompatKey)

                    mgmt.POST("/vertex/import", s.mgmt.ImportVertexCredential)

                    mgmt.GET("/anthropic-auth-url", s.mgmt.RequestAnthropicToken)
                    mgmt.GET("/codex-auth-url", s.mgmt.RequestCodexToken)
                    mgmt.GET("/gemini-cli-auth-url", s.mgmt.RequestGeminiCLIToken)
                    mgmt.GET("/antigravity-auth-url", s.mgmt.RequestAntigravityToken)
                    mgmt.GET("/kimi-auth-url", s.mgmt.RequestKimiToken)
                    mgmt.POST("/oauth-callback", s.mgmt.PostOAuthCallback)
                }

                func (s *Server) unifiedModelsHandler(openaiHandler *openai.OpenAIAPIHandler, claudeHandler *claude.ClaudeCodeAPIHandler) gin.HandlerFunc {
                    return func(c *gin.Context) {
                        userAgent := c.GetHeader("User-Agent")

                        // Route to Claude handler if User-Agent starts with "claude-cli"
                        if strings.HasPrefix(userAgent, "claude-cli") {
                            // log.Debugf("Routing /v1/models to Claude handler for User-Agent: %s", userAgent)
                            claudeHandler.ClaudeModels(c)
                        } else {
                            // log.Debugf("Routing /v1/models to OpenAI handler for User-Agent: %s", userAgent)
                            openaiHandler.OpenAIModels(c)
                        }
                    }
                }
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(serverCmdRoot, "main.go"),
                """
                package main

                func main() {
                    if vertexImport != "" {
                        // Handle Vertex service account import
                        cmd.DoVertexImport(cfg, vertexImport, vertexImportPrefix)
                    } else if login {
                        // Handle Google/Gemini login
                        cmd.DoLogin(cfg, projectID, options)
                    } else if antigravityLogin {
                        // Handle Antigravity login
                        cmd.DoAntigravityLogin(cfg, options)
                    } else if codexLogin {
                        // Handle Codex login
                        cmd.DoCodexLogin(cfg, options)
                    } else if codexDeviceLogin {
                        // Handle Codex device-code login
                        cmd.DoCodexDeviceLogin(cfg, options)
                    } else if claudeLogin {
                        // Handle Claude login
                        cmd.DoClaudeLogin(cfg, options)
                    } else if kimiLogin {
                        cmd.DoKimiLogin(cfg, options)
                    } else {
                        misc.StartAntigravityVersionUpdater(context.Background())
                        registry.StartModelsUpdater(context.Background())
                        misc.StartAntigravityVersionUpdater(context.Background())
                    }
                }
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(runtimeExecutorHelpsRoot, "thinking_providers.go"),
                """
                package helps

                import (
                    _ "github.com/router-for-me/CLIProxyAPI/v6/internal/thinking/provider/antigravity"
                    _ "github.com/router-for-me/CLIProxyAPI/v6/internal/thinking/provider/claude"
                    _ "github.com/router-for-me/CLIProxyAPI/v6/internal/thinking/provider/codex"
                    _ "github.com/router-for-me/CLIProxyAPI/v6/internal/thinking/provider/gemini"
                    _ "github.com/router-for-me/CLIProxyAPI/v6/internal/thinking/provider/geminicli"
                    _ "github.com/router-for-me/CLIProxyAPI/v6/internal/thinking/provider/kimi"
                    _ "github.com/router-for-me/CLIProxyAPI/v6/internal/thinking/provider/openai"
                )
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(translatorRoot, "init.go"),
                """
                package translator

                import (
                    _ "github.com/router-for-me/CLIProxyAPI/v6/internal/translator/claude/gemini"
                    _ "github.com/router-for-me/CLIProxyAPI/v6/internal/translator/codex/openai/chat-completions"
                    _ "github.com/router-for-me/CLIProxyAPI/v6/internal/translator/codex/openai/responses"
                    _ "github.com/router-for-me/CLIProxyAPI/v6/internal/translator/openai/openai/chat-completions"
                    _ "github.com/router-for-me/CLIProxyAPI/v6/internal/translator/openai/openai/responses"
                    _ "github.com/router-for-me/CLIProxyAPI/v6/internal/translator/antigravity/claude"
                )
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(runtimeExecutorRoot, "openai_compat_executor.go"),
                """
                package executor

                func (e *OpenAICompatExecutor) Execute(ctx context.Context, auth *cliproxyauth.Auth, req cliproxyexecutor.Request, opts cliproxyexecutor.Options) (resp cliproxyexecutor.Response, err error) {
                    translated = helps.ApplyPayloadConfigWithRoot(e.cfg, baseModel, to.String(), "", translated, originalTranslated, requestedModel, requestPath)
                    if opts.Alt == "responses/compact" {
                    }

                    httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(translated))
                    if err != nil {
                        return resp, err
                    }
                    var authID, authLabel, authType, authValue string
                    helps.RecordAPIRequest(ctx, e.cfg, helps.UpstreamRequestLog{
                        URL:       url,
                        Method:    http.MethodPost,
                        Headers:   httpReq.Header.Clone(),
                        Body:      translated,
                        Provider:  e.Identifier(),
                        AuthID:    authID,
                        AuthLabel: authLabel,
                        AuthType:  authType,
                        AuthValue: authValue,
                    })

                    httpClient := helps.NewProxyAwareHTTPClient(ctx, e.cfg, auth, 0)
                    httpResp, err := httpClient.Do(httpReq)
                    if err != nil {
                        helps.RecordAPIResponseError(ctx, e.cfg, err)
                        return resp, err
                    }
                    defer func() {
                        if errClose := httpResp.Body.Close(); errClose != nil {
                            log.Errorf("openai compat executor: close response body error: %v", errClose)
                        }
                    }()
                    helps.RecordAPIResponseMetadata(ctx, e.cfg, httpResp.StatusCode, httpResp.Header.Clone())
                    if httpResp.StatusCode < 200 || httpResp.StatusCode >= 300 {
                        b, _ := io.ReadAll(httpResp.Body)
                        helps.AppendAPIResponseChunk(ctx, e.cfg, b)
                        helps.LogWithRequestID(ctx).Debugf("request error, error status: %d, error message: %s", httpResp.StatusCode, helps.SummarizeErrorBody(httpResp.Header.Get("Content-Type"), b))
                        err = statusErr{code: httpResp.StatusCode, msg: string(b)}
                        return resp, err
                    }
                    return resp, nil
                }

                func (e *OpenAICompatExecutor) ExecuteStream(ctx context.Context, auth *cliproxyauth.Auth, req cliproxyexecutor.Request, opts cliproxyexecutor.Options) (_ *cliproxyexecutor.StreamResult, err error) {
                    translated = helps.ApplyPayloadConfigWithRoot(e.cfg, baseModel, to.String(), "", translated, originalTranslated, requestedModel, requestPath)

                    translated, err = thinking.ApplyThinking(translated, req.Model, from.String(), to.String(), e.Identifier())
                    if err != nil {
                        return nil, err
                    }

                    httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(translated))
                    if err != nil {
                        return nil, err
                    }
                    var authID, authLabel, authType, authValue string
                    helps.RecordAPIRequest(ctx, e.cfg, helps.UpstreamRequestLog{
                        URL:       url,
                        Method:    http.MethodPost,
                        Headers:   httpReq.Header.Clone(),
                        Body:      translated,
                        Provider:  e.Identifier(),
                        AuthID:    authID,
                        AuthLabel: authLabel,
                        AuthType:  authType,
                        AuthValue: authValue,
                    })

                    httpClient := helps.NewProxyAwareHTTPClient(ctx, e.cfg, auth, 0)
                    httpResp, err := httpClient.Do(httpReq)
                    if err != nil {
                        helps.RecordAPIResponseError(ctx, e.cfg, err)
                        return nil, err
                    }
                    helps.RecordAPIResponseMetadata(ctx, e.cfg, httpResp.StatusCode, httpResp.Header.Clone())
                    if httpResp.StatusCode < 200 || httpResp.StatusCode >= 300 {
                        b, _ := io.ReadAll(httpResp.Body)
                        helps.AppendAPIResponseChunk(ctx, e.cfg, b)
                        helps.LogWithRequestID(ctx).Debugf("request error, error status: %d, error message: %s", httpResp.StatusCode, helps.SummarizeErrorBody(httpResp.Header.Get("Content-Type"), b))
                        if errClose := httpResp.Body.Close(); errClose != nil {
                            log.Errorf("openai compat executor: close response body error: %v", errClose)
                        }
                        err = statusErr{code: httpResp.StatusCode, msg: string(b)}
                        return nil, err
                    }
                    return nil, nil
                }
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(sdkAuthRoot, "filestore.go"),
                """
                package auth

                import (
                    "context"
                    "encoding/json"
                    "fmt"
                    "os"

                    cliproxyauth "github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth"
                )

                type FileTokenStore struct{}

                func (s *FileTokenStore) Save(ctx context.Context, auth *cliproxyauth.Auth) (string, error) {
                    _ = ctx
                    path := "auth.json"
                    switch {
                    case auth.Storage != nil:
                        if err := auth.Storage.SaveTokenToFile(path); err != nil {
                            return "", err
                        }
                    case auth.Metadata != nil:
                        raw, errMarshal := json.Marshal(auth.Metadata)
                        if errMarshal != nil {
                            return "", fmt.Errorf("auth filestore: marshal metadata failed: %w", errMarshal)
                        }
                        if existing, errRead := os.ReadFile(path); errRead == nil {
                            if jsonEqual(existing, raw) {
                                return path, nil
                            }
                        }
                        if errWrite := os.WriteFile(path, raw, 0o600); errWrite != nil {
                            return "", errWrite
                        }
                    }
                    return path, nil
                }

                func jsonEqual(left, right []byte) bool {
                    return string(left) == string(right)
                }
                """,
                Encoding.UTF8
            );
            File.WriteAllText(
                Path.Combine(storeRoot, "gitstore.go"),
                """
                package store

                import (
                    "github.com/go-git/go-git/v6"
                    "github.com/go-git/go-git/v6/config"
                    "github.com/go-git/go-git/v6/plumbing"
                    "github.com/go-git/go-git/v6/plumbing/object"
                    "github.com/go-git/go-git/v6/plumbing/transport"
                    "github.com/go-git/go-git/v6/plumbing/transport/http"
                )

                type GitTokenStore struct {
                    username string
                    password string
                }

                func sample(repo *git.Repository, remote *git.Remote, authMethod transport.AuthMethod) {
                    _ = config.RefSpec("")
                    _ = plumbing.ReferenceName("")
                    _ = object.Commit{}
                    _ = &git.CloneOptions{Auth: authMethod, URL: ""}
                    _ = &git.PullOptions{Auth: authMethod, RemoteName: "origin"}
                    _ = &git.FetchOptions{Auth: authMethod, RemoteName: "origin"}
                    _ = &git.ListOptions{Auth: authMethod}
                    s := &GitTokenStore{}
                    _ = &git.PushOptions{Auth: s.gitAuth(), Force: true}
                    _, _ = repo.Head()
                    _, _ = remote.List(nil)
                }

                func (s *GitTokenStore) gitAuth() transport.AuthMethod {
                    if s.username == "" && s.password == "" {
                        return nil
                    }
                    user := s.username
                    if user == "" {
                        user = "git"
                    }
                    return &http.BasicAuth{Username: user, Password: s.password}
                }
                """,
                Encoding.UTF8
            );
        }
    }
}
