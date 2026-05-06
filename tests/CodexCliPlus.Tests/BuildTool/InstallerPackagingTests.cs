using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CodexCliPlus.BuildTool;

namespace CodexCliPlus.Tests.BuildTool;

public sealed class InstallerPackagingTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-installer-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task PackageOfflineInstallerBuildsExecutableAndKeepsFullPayloadWhenRequested()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var outputRoot = Path.Combine(_rootDirectory, "out-success");
        CreatePublishRoot(outputRoot);
        CreateWebView2Cache(outputRoot);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var runner = new InstallerProcessRunner();
        var context = CreateBuildContext(
            repositoryRoot,
            outputRoot,
            output,
            error,
            runner,
            keepPackageStaging: true
        );

        var exitCode = await PackageCommands.PackageInstallerAsync(
            context,
            InstallerPackageKind.Offline
        );

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());

        var installerPath = Path.Combine(
            outputRoot,
            "packages",
            "CodexCliPlus.Setup.Offline.9.9.9.exe"
        );
        Assert.True(File.Exists(installerPath));
        Assert.Null(WindowsExecutableValidation.ValidateFile(installerPath));

        var stagingPackagePath = Path.Combine(
            outputRoot,
            "packages",
            "CodexCliPlus.Setup.Offline.9.9.9.win-x64.zip"
        );
        Assert.False(File.Exists(stagingPackagePath));

        var stageRoot = Path.Combine(
            outputRoot,
            "installer",
            "win-x64",
            "offline-installer",
            "stage"
        );
        Assert.True(
            File.Exists(Path.Combine(stageRoot, "output", "CodexCliPlus.Setup.Offline.9.9.9.exe"))
        );
        Assert.True(File.Exists(Path.Combine(stageRoot, "publish.7z")));
        Assert.True(File.Exists(Path.Combine(stageRoot, "micasetup.json")));
        Assert.True(
            File.Exists(
                Path.Combine(
                    stageRoot,
                    "app-package",
                    "assets",
                    "webui",
                    "upstream",
                    "dist",
                    "index.html"
                )
            )
        );
        Assert.True(
            File.Exists(
                Path.Combine(
                    stageRoot,
                    "app-package",
                    "assets",
                    "webui",
                    "upstream",
                    "dist",
                    "assets",
                    "app.js"
                )
            )
        );
        Assert.True(
            File.Exists(
                Path.Combine(stageRoot, "app-package", "assets", "webui", "upstream", "sync.json")
            )
        );
        Assert.False(
            File.Exists(
                Path.Combine(
                    stageRoot,
                    "app-package",
                    WebView2RuntimeAssets.PackagedDirectory.Replace(
                        '/',
                        Path.DirectorySeparatorChar
                    ),
                    WebView2RuntimeAssets.BootstrapperFileName
                )
            )
        );
        Assert.True(
            File.Exists(
                Path.Combine(
                    stageRoot,
                    "app-package",
                    WebView2RuntimeAssets.PackagedDirectory.Replace(
                        '/',
                        Path.DirectorySeparatorChar
                    ),
                    WebView2RuntimeAssets.StandaloneX64FileName
                )
            )
        );

        var installerPlan = ReadJson<InstallerPlan>(Path.Combine(stageRoot, "mica-setup.json"));
        Assert.True(installerPlan.CleanupInstallerAfterInstallDefault);
        Assert.Equal("bundled-archive", installerPlan.PayloadMode);

        var micaConfig = ReadJson<JsonElement>(Path.Combine(stageRoot, "micasetup.json"));
        Assert.EndsWith(
            "codexcliplus-display.png",
            micaConfig.GetProperty("Favicon").GetString(),
            StringComparison.Ordinal
        );
        Assert.EndsWith(
            "codexcliplus.ico",
            micaConfig.GetProperty("Icon").GetString(),
            StringComparison.Ordinal
        );
        Assert.EndsWith(
            "codexcliplus.ico",
            micaConfig.GetProperty("UnIcon").GetString(),
            StringComparison.Ordinal
        );

        var cleanupManifest = ReadJson<InstallerCleanupManifest>(
            Path.Combine(stageRoot, "app-package", "packaging", "uninstall-cleanup.json")
        );
        Assert.Equal("KeepMyData", cleanupManifest.KeepMyDataOptionName);
        Assert.False(cleanupManifest.KeepMyDataDefault);
        Assert.Equal("full-clean", cleanupManifest.DefaultUninstallProfile);
        Assert.Contains(
            "%ProgramFiles%\\CodexCliPlus\\config\\secrets\\*.bin",
            cleanupManifest.DeleteByDefault
        );
        Assert.Contains("%ProgramFiles%\\CodexCliPlus", cleanupManifest.AlwaysDelete);

        var dependencyPrecheck = ReadJson<JsonElement>(
            Path.Combine(stageRoot, "app-package", "packaging", "dependency-precheck.json")
        );
        Assert.True(
            dependencyPrecheck.GetProperty("webView2").GetProperty("required").GetBoolean()
        );
        Assert.True(
            dependencyPrecheck.GetProperty("webView2").GetProperty("bundledFirst").GetBoolean()
        );
        Assert.Equal(
            "bundled-standalone-only",
            dependencyPrecheck.GetProperty("webView2").GetProperty("installStrategy").GetString()
        );
        Assert.False(
            dependencyPrecheck.GetProperty("webView2").TryGetProperty("onlineBootstrapper", out _)
        );
        Assert.Equal(
            WebView2RuntimeAssets.StandaloneX64FileName,
            dependencyPrecheck
                .GetProperty("webView2")
                .GetProperty("bundledStandaloneX64")
                .GetProperty("fileName")
                .GetString()
        );
        Assert.True(
            dependencyPrecheck.GetProperty("webUi").GetProperty("bundledFirst").GetBoolean()
        );
        Assert.True(
            dependencyPrecheck.GetProperty("runtime").GetProperty("bundledFirst").GetBoolean()
        );
        Assert.True(
            dependencyPrecheck.GetProperty("backend").GetProperty("bundledFirst").GetBoolean()
        );

        Assert.Contains(
            "repo-owned MicaSetup source templates",
            output.ToString(),
            StringComparison.Ordinal
        );
        Assert.Contains("kept package staging", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageInstallerUsesRepoOwnedSourceTemplate()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var outputRoot = Path.Combine(_rootDirectory, "out-fallback");
        CreatePublishRoot(outputRoot);
        CreateWebView2Cache(outputRoot);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var runner = new InstallerProcessRunner();
        var context = CreateBuildContext(
            repositoryRoot,
            outputRoot,
            output,
            error,
            runner,
            keepPackageStaging: true
        );

        var exitCode = await PackageCommands.PackageInstallerAsync(
            context,
            InstallerPackageKind.Offline
        );

        Assert.Equal(0, exitCode);
        Assert.True(runner.DotnetMsbuildInvocations >= 2);

        var installerPath = Path.Combine(
            outputRoot,
            "packages",
            "CodexCliPlus.Setup.Offline.9.9.9.exe"
        );
        Assert.True(File.Exists(installerPath));
        Assert.Null(WindowsExecutableValidation.ValidateFile(installerPath));

        var distRoot = Path.Combine(
            outputRoot,
            "installer",
            "win-x64",
            "offline-installer",
            "stage",
            ".dist"
        );
        var setupProgram = File.ReadAllText(Path.Combine(distRoot, "Program.cs"));
        var uninstProgram = File.ReadAllText(Path.Combine(distRoot, "Program.un.cs"));
        var setupMainViewModel = File.ReadAllText(
            Path.Combine(distRoot, "ViewModels", "Inst", "MainViewModel.cs")
        );
        var installViewModel = File.ReadAllText(
            Path.Combine(distRoot, "ViewModels", "Inst", "InstallViewModel.cs")
        );
        var finishPage = File.ReadAllText(
            Path.Combine(distRoot, "Views", "Inst", "FinishPage.xaml")
        );
        var finishViewModel = File.ReadAllText(
            Path.Combine(distRoot, "ViewModels", "Inst", "FinishViewModel.cs")
        );
        var uninstallHelper = File.ReadAllText(
            Path.Combine(distRoot, "Helper", "Setup", "UninstallHelper.cs")
        );
        var archiveFileHelper = File.ReadAllText(
            Path.Combine(distRoot, "Helper", "Setup", "ArchiveFileHelper.cs")
        );
        var renderedLicensePath = Path.Combine(distRoot, "Resources", "Licenses", "license.txt");

        Assert.Contains(".UseElevated()", setupProgram, StringComparison.Ordinal);
        Assert.Contains("BlackblockInc.CodexCliPlus.Setup", setupProgram, StringComparison.Ordinal);
        Assert.Contains("RequestExecutionLevel(\"admin\")", setupProgram, StringComparison.Ordinal);
        Assert.Contains("option.KeepMyData = false;", uninstProgram, StringComparison.Ordinal);
        Assert.Matches(
            "private const long RequestedPayloadUncompressedBytes = [1-9][0-9]*;",
            setupMainViewModel
        );
        Assert.DoesNotContain(
            "__PAYLOAD_UNCOMPRESSED_BYTES__",
            setupMainViewModel,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "ArchiveFileHelper.TotalUncompressSize",
            setupMainViewModel,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("publish.7z", setupMainViewModel, StringComparison.Ordinal);
        Assert.Contains("LoadLicenseInfo", setupMainViewModel, StringComparison.Ordinal);
        Assert.Contains(
            "EnsureCodexCliPlusWebView2RuntimeInstalled",
            installViewModel,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "private const string WebView2BootstrapperFileName = \"\";",
            installViewModel,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            WebView2RuntimeAssets.BootstrapperFileName,
            installViewModel,
            StringComparison.Ordinal
        );
        Assert.Contains(
            WebView2RuntimeAssets.StandaloneX64FileName,
            installViewModel,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "无法安装 Microsoft Edge WebView2 Runtime",
            installViewModel,
            StringComparison.Ordinal
        );
        Assert.Contains("完成后删除安装包", finishPage, StringComparison.Ordinal);
        Assert.Contains(
            "IsChecked=\"{Binding CleanupInstallerAfterInstall}\"",
            finishPage,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "CleanupOriginalInstallerAfterInstall",
            finishViewModel,
            StringComparison.Ordinal
        );
        Assert.Equal(
            2,
            CountOccurrences(finishViewModel, "CleanupOriginalInstallerAfterInstall();")
        );
        Assert.Contains("ComputeCodexCliPlusSha256", finishViewModel, StringComparison.Ordinal);
        Assert.Contains("TempPathForkHelper.ForkedCli", finishViewModel, StringComparison.Ordinal);
        Assert.Contains("CleanupCodexCliPlusUserData", uninstallHelper, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Option.Current.KeepMyData = true;",
            uninstallHelper,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "stream.Position = originalPosition;",
            archiveFileHelper,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "using dynamic? archive = type switch",
            archiveFileHelper,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "long totalUncompressSize = archive.TotalUncompressSize;",
            archiveFileHelper,
            StringComparison.Ordinal
        );
        Assert.True(File.Exists(renderedLicensePath));
        Assert.Equal("notice", File.ReadAllText(renderedLicensePath));

        Assert.True(
            ReadPngWidth(Path.Combine(distRoot, "Resources", "Images", "Favicon.png")) >= 256
        );
        Assert.True(
            ReadPngWidth(Path.Combine(distRoot, "Resources", "Images", "FaviconSetup.png")) >= 256
        );
        Assert.True(
            ReadPngWidth(Path.Combine(distRoot, "Resources", "Images", "FaviconUninst.png")) >= 256
        );
        Assert.Contains("kept package staging", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageOfflineInstallerReusesIncrementalCacheAndInvalidatesOnPublishChange()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var outputRoot = Path.Combine(_rootDirectory, "out-incremental");
        CreatePublishRoot(outputRoot);
        CreateWebView2Cache(outputRoot);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var runner = new InstallerProcessRunner();
        var context = CreateBuildContext(repositoryRoot, outputRoot, output, error, runner);

        Assert.Equal(
            0,
            await PackageCommands.PackageInstallerAsync(context, InstallerPackageKind.Offline)
        );
        Assert.Equal(2, runner.DotnetMsbuildInvocations);

        Assert.Equal(
            0,
            await PackageCommands.PackageInstallerAsync(context, InstallerPackageKind.Offline)
        );
        Assert.Equal(2, runner.DotnetMsbuildInvocations);
        Assert.Contains("offline-installer cache hit", output.ToString(), StringComparison.Ordinal);

        File.AppendAllText(
            Path.Combine(outputRoot, "publish", "win-x64", "appsettings.json"),
            "\n{\"changed\":true}"
        );
        Assert.Equal(
            0,
            await PackageCommands.PackageInstallerAsync(context, InstallerPackageKind.Offline)
        );

        Assert.Equal(3, runner.DotnetMsbuildInvocations);
        Assert.Contains("offline-installer input changed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("micasetup-uninstaller cache hit", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task PackageOnlineInstallerUsesRemoteUpdatePackageWithoutBundledPayload()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var outputRoot = Path.Combine(_rootDirectory, "out-online");
        CreatePublishRoot(outputRoot);

        using var packageOutput = new StringWriter();
        using var packageError = new StringWriter();
        var runner = new InstallerProcessRunner();
        var updateContext = CreateBuildContext(
            repositoryRoot,
            outputRoot,
            packageOutput,
            packageError,
            runner
        );
        var updateCode = await PackageCommands.PackageUpdateAsync(updateContext);
        Assert.Equal(0, updateCode);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var context = CreateBuildContext(
            repositoryRoot,
            outputRoot,
            output,
            error,
            runner,
            keepPackageStaging: true,
            onlinePayloadBaseUrl: "https://downloads.example.test/codex"
        );

        var exitCode = await PackageCommands.PackageInstallerAsync(
            context,
            InstallerPackageKind.Online
        );

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.True(
            File.Exists(Path.Combine(outputRoot, "packages", "CodexCliPlus.Setup.Online.9.9.9.exe"))
        );
        Assert.False(
            File.Exists(
                Path.Combine(outputRoot, "packages", "CodexCliPlus.Setup.Online.9.9.9.win-x64.zip")
            )
        );

        var stageRoot = Path.Combine(
            outputRoot,
            "installer",
            "win-x64",
            "online-installer",
            "stage"
        );
        Assert.True(Directory.Exists(stageRoot));
        Assert.False(Directory.Exists(Path.Combine(stageRoot, "app-package")));
        Assert.False(File.Exists(Path.Combine(stageRoot, "publish.7z")));
        Assert.False(
            File.Exists(
                Path.Combine(
                    stageRoot,
                    WebView2RuntimeAssets.PackagedDirectory.Replace(
                        '/',
                        Path.DirectorySeparatorChar
                    ),
                    WebView2RuntimeAssets.StandaloneX64FileName
                )
            )
        );

        var onlinePayload = ReadJson<OnlineInstallerPayload>(
            Path.Combine(stageRoot, "online-payload.json")
        );
        Assert.Equal("CodexCliPlus.Update.9.9.9.win-x64.zip", onlinePayload.FileName);
        Assert.Equal(
            "https://downloads.example.test/codex/CodexCliPlus.Update.9.9.9.win-x64.zip",
            onlinePayload.Url
        );
        Assert.True(onlinePayload.Size > 0);
        Assert.False(string.IsNullOrWhiteSpace(onlinePayload.Sha256));

        var installerPlan = ReadJson<InstallerPlan>(Path.Combine(stageRoot, "mica-setup.json"));
        Assert.Equal("download-update-zip", installerPlan.PayloadMode);
        Assert.NotNull(installerPlan.OnlinePayload);

        var distRoot = Path.Combine(stageRoot, ".dist");
        Assert.False(File.Exists(Path.Combine(distRoot, "Resources", "Setups", "publish.7z")));
        var installViewModel = File.ReadAllText(
            Path.Combine(distRoot, "ViewModels", "Inst", "InstallViewModel.cs")
        );
        Assert.Contains(
            "private const bool IsOnlinePayloadMode = true;",
            installViewModel,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "DownloadAndPrepareOnlinePayloadArchive",
            installViewModel,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "https://downloads.example.test/codex/CodexCliPlus.Update.9.9.9.win-x64.zip",
            installViewModel,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task PackageUpdateRemovesStagingByDefault()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var outputRoot = Path.Combine(_rootDirectory, "out-update");
        CreatePublishRoot(outputRoot);
        var updatePackagePath = Path.Combine(
            outputRoot,
            "packages",
            "CodexCliPlus.Update.9.9.9.win-x64.zip"
        );
        WriteLegacySidecars(updatePackagePath);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var runner = new InstallerProcessRunner();
        var context = CreateBuildContext(repositoryRoot, outputRoot, output, error, runner);

        var exitCode = await PackageCommands.PackageUpdateAsync(context);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(updatePackagePath));
        Assert.False(
            Directory.Exists(
                Path.Combine(outputRoot, "installer", "win-x64", "update-package", "stage")
            )
        );
        Assert.Contains("removed package staging", output.ToString(), StringComparison.Ordinal);
        AssertLegacySidecarsDoNotExist(
            updatePackagePath
        );
        Assert.Equal(string.Empty, error.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private static BuildContext CreateBuildContext(
        string repositoryRoot,
        string outputRoot,
        StringWriter output,
        StringWriter error,
        IProcessRunner runner,
        bool keepPackageStaging = false,
        string onlinePayloadBaseUrl = ""
    )
    {
        return new BuildContext(
            new BuildOptions(
                "package-offline-installer",
                repositoryRoot,
                outputRoot,
                "Release",
                "win-x64",
                "9.9.9",
                KeepPackageStaging: keepPackageStaging,
                OnlinePayloadBaseUrl: onlinePayloadBaseUrl
            ),
            new BuildLogger(output, error),
            runner
        );
    }

    private static void AssertLegacySidecarsDoNotExist(string artifactPath)
    {
        Assert.False(File.Exists(artifactPath + ".signature" + ".json"));
        Assert.False(File.Exists(artifactPath + ".unsigned" + ".json"));
    }

    private static void WriteLegacySidecars(string artifactPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        File.WriteAllText(artifactPath + ".signature" + ".json", "{}");
        File.WriteAllText(artifactPath + ".unsigned" + ".json", "{}");
    }

    private string CreateRepositoryRoot()
    {
        var repositoryRoot = Path.Combine(_rootDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "CodexCliPlus.sln"), string.Empty);
        File.WriteAllText(Path.Combine(repositoryRoot, "LICENSE.txt"), "license");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "build", "micasetup"));
        CreateRepoOwnedMicaSourceTemplate(repositoryRoot);
        CreateRepoOwnedMicaSourceTemplates(repositoryRoot);
        var iconRoot = Path.Combine(repositoryRoot, "resources", "icons");
        Directory.CreateDirectory(iconRoot);
        File.WriteAllBytes(
            Path.Combine(iconRoot, "codexcliplus-display.png"),
            CreatePngHeaderBytes(256, 256)
        );
        File.WriteAllBytes(Path.Combine(iconRoot, "codexcliplus.ico"), [0, 0, 1, 0]);
        var licenseRoot = Path.Combine(repositoryRoot, "resources", "licenses");
        Directory.CreateDirectory(licenseRoot);
        File.WriteAllText(Path.Combine(licenseRoot, "NOTICE.txt"), "notice");
        return repositoryRoot;
    }

    private static void CreateWebView2Cache(string outputRoot)
    {
        var webView2Root = Path.Combine(outputRoot, "cache", "webview2");
        Directory.CreateDirectory(webView2Root);
        File.WriteAllBytes(
            Path.Combine(webView2Root, WebView2RuntimeAssets.BootstrapperFileName),
            CreateExecutableBytes()
        );
        File.WriteAllBytes(
            Path.Combine(webView2Root, WebView2RuntimeAssets.StandaloneX64FileName),
            CreateExecutableBytes()
        );
    }

    private static void CreatePublishRoot(string outputRoot)
    {
        var publishRoot = Path.Combine(outputRoot, "publish", "win-x64");
        Directory.CreateDirectory(publishRoot);
        File.WriteAllBytes(Path.Combine(publishRoot, "CodexCliPlus.exe"), CreateExecutableBytes());
        File.WriteAllText(Path.Combine(publishRoot, "appsettings.json"), "{}");
        Directory.CreateDirectory(Path.Combine(publishRoot, "assets", "webui", "upstream", "dist"));
        Directory.CreateDirectory(
            Path.Combine(publishRoot, "assets", "webui", "upstream", "dist", "assets")
        );
        File.WriteAllText(
            Path.Combine(publishRoot, "assets", "webui", "upstream", "dist", "index.html"),
            "<html></html>"
        );
        File.WriteAllText(
            Path.Combine(publishRoot, "assets", "webui", "upstream", "dist", "assets", "app.js"),
            "console.log('ok');"
        );
        File.WriteAllText(
            Path.Combine(publishRoot, "assets", "webui", "upstream", "sync.json"),
            "{}"
        );
    }

    private static void CreateRepoOwnedMicaSourceTemplate(string repositoryRoot)
    {
        var sourceTemplateRoot = Path.Combine(
            repositoryRoot,
            "build",
            "micasetup",
            "source-template"
        );
        Directory.CreateDirectory(sourceTemplateRoot);
        Directory.CreateDirectory(Path.Combine(sourceTemplateRoot, "Resources", "Images"));
        Directory.CreateDirectory(Path.Combine(sourceTemplateRoot, "Resources", "Licenses"));
        Directory.CreateDirectory(Path.Combine(sourceTemplateRoot, "Resources", "Setups"));
        File.WriteAllText(Path.Combine(sourceTemplateRoot, "MicaSetup.csproj"), "<Project />");
        File.WriteAllText(
            Path.Combine(sourceTemplateRoot, "MicaSetup.Uninst.csproj"),
            "<Project />"
        );
    }

    private static void CreateRepoOwnedMicaSourceTemplates(string repositoryRoot)
    {
        var overlayRoot = Path.Combine(
            repositoryRoot,
            "build",
            "micasetup",
            "overrides",
            "MicaSetup"
        );
        Directory.CreateDirectory(Path.Combine(overlayRoot, "ViewModels", "Inst"));
        Directory.CreateDirectory(Path.Combine(overlayRoot, "ViewModels", "Uninst"));
        Directory.CreateDirectory(Path.Combine(overlayRoot, "Views", "Inst"));
        Directory.CreateDirectory(Path.Combine(overlayRoot, "Helper", "Setup"));

        File.WriteAllText(
            Path.Combine(overlayRoot, "Program.cs.template"),
            """
            [assembly: AssemblyVersion("__ASSEMBLY_VERSION__")]
            [assembly: RequestExecutionLevel("admin")]
            Hosting.CreateBuilder().UseElevated().UseSingleInstance("BlackblockInc.CodexCliPlus.Setup").UseOptions(option => { option.DisplayVersion = "__DISPLAY_VERSION__"; });
            """
        );
        File.WriteAllText(
            Path.Combine(overlayRoot, "Program.un.cs.template"),
            """
            [assembly: AssemblyVersion("__ASSEMBLY_VERSION__")]
            Hosting.CreateBuilder().UseElevated().UseSingleInstance("BlackblockInc.CodexCliPlus.Uninstall").UseOptions(option => { option.ExeName = "CodexCliPlus.exe"; option.KeepMyData = false; });
            """
        );
        File.WriteAllText(
            Path.Combine(overlayRoot, "ViewModels", "Inst", "MainViewModel.cs.template"),
            """
            public sealed class MainViewModel
            {
                private const long RequestedPayloadUncompressedBytes = __PAYLOAD_UNCOMPRESSED_BYTES__;
                private long requestedFreeSpaceLong = RequestedPayloadUncompressedBytes + 2048000;

                public MainViewModel()
                {
                    LicenseInfo = LoadLicenseInfo();
                }

                public string LicenseInfo { get; set; } = string.Empty;

                private static string LoadLicenseInfo()
                {
                    return string.Empty;
                }
            }
            """
        );
        File.WriteAllText(
            Path.Combine(overlayRoot, "ViewModels", "Inst", "InstallViewModel.cs.template"),
            $$"""
            private const bool IsOnlinePayloadMode = __IS_ONLINE_PAYLOAD_MODE__;
            private const string OnlinePayloadFileName = __ONLINE_PAYLOAD_FILE_NAME__;
            private const string OnlinePayloadUrl = __ONLINE_PAYLOAD_URL__;
            private const string OnlinePayloadSha256 = __ONLINE_PAYLOAD_SHA256__;
            private const long OnlinePayloadSizeBytes = __ONLINE_PAYLOAD_SIZE_BYTES__;
            private const string WebView2BootstrapperFileName = __WEBVIEW2_BOOTSTRAPPER_FILE_NAME__;
            private const string WebView2BootstrapperUrl = __WEBVIEW2_BOOTSTRAPPER_URL__;
            private const string WebView2StandaloneFileName = __WEBVIEW2_STANDALONE_FILE_NAME__;

            private string DownloadAndPrepareOnlinePayloadArchive(string tempRoot)
            {
                return OnlinePayloadUrl + tempRoot;
            }

            private bool EnsureCodexCliPlusWebView2RuntimeInstalled()
            {
                string bootstrapperPath = WebView2BootstrapperFileName;
                string standalonePath = WebView2StandaloneFileName;
                string reason = "无法安装 Microsoft Edge WebView2 Runtime";
                return bootstrapperPath.Length + standalonePath.Length + reason.Length > 0;
            }
            """
        );
        File.WriteAllText(
            Path.Combine(overlayRoot, "Views", "Inst", "FinishPage.xaml.template"),
            """
            <CheckBox Content="完成后删除安装包" IsChecked="{Binding CleanupInstallerAfterInstall}" />
            """
        );
        File.WriteAllText(
            Path.Combine(overlayRoot, "ViewModels", "Inst", "FinishViewModel.cs.template"),
            """
            public sealed class FinishViewModel
            {
                public void Close()
                {
                    CleanupOriginalInstallerAfterInstall();
                }

                public void Open()
                {
                    CleanupOriginalInstallerAfterInstall();
                }

                private void CleanupOriginalInstallerAfterInstall()
                {
                    _ = TempPathForkHelper.ForkedCli;
                    _ = ComputeCodexCliPlusSha256("setup.exe");
                }

                private static string ComputeCodexCliPlusSha256(string path) => path;
            }
            """
        );
        File.WriteAllText(
            Path.Combine(overlayRoot, "ViewModels", "Uninst", "MainViewModel.cs.template"),
            """
            public sealed class MainViewModel
            {
                private bool isElevated = true;
            }
            """
        );
        File.WriteAllText(
            Path.Combine(overlayRoot, "Helper", "Setup", "ArchiveFileHelper.cs.template"),
            """
            public static class ArchiveFileHelper
            {
                public static object OpenArchive(Stream stream)
                {
                    long originalPosition = stream.CanSeek ? stream.Position : 0L;
                    try
                    {
                        return new object();
                    }
                    finally
                    {
                        if (stream.CanSeek)
                        {
                            stream.Position = originalPosition;
                        }
                    }
                }

                public static void ExtractAll(dynamic archive)
                {
                    long totalUncompressSize = archive.TotalUncompressSize;
                }
            }
            """
        );
        File.WriteAllText(
            Path.Combine(overlayRoot, "Helper", "Setup", "UninstallHelper.cs.template"),
            """
            public static class UninstallHelper
            {
                private static void CleanupCodexCliPlusUserData()
                {
                }
            }
            """
        );
    }

    private static T ReadArchiveJson<T>(ZipArchive archive, string entryName)
    {
        var entry =
            archive.GetEntry(entryName)
            ?? throw new Xunit.Sdk.XunitException($"Missing zip entry: {entryName}");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), JsonDefaults.Options)
            ?? throw new Xunit.Sdk.XunitException($"Could not deserialize zip entry: {entryName}");
    }

    private static T ReadJson<T>(string path)
    {
        return JsonSerializer.Deserialize<T>(
                File.ReadAllText(path, Encoding.UTF8),
                JsonDefaults.Options
            ) ?? throw new Xunit.Sdk.XunitException($"Could not deserialize JSON file: {path}");
    }

    private static byte[] CreateExecutableBytes()
    {
        var bytes = new byte[128];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    private static int ReadPngWidth(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
    }

    private static byte[] CreatePngHeaderBytes(int width, int height)
    {
        var bytes = new byte[33];
        bytes[0] = 0x89;
        bytes[1] = (byte)'P';
        bytes[2] = (byte)'N';
        bytes[3] = (byte)'G';
        bytes[4] = 0x0D;
        bytes[5] = 0x0A;
        bytes[6] = 0x1A;
        bytes[7] = 0x0A;
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    private static void WriteBigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)((value >> 24) & 0xFF);
        bytes[offset + 1] = (byte)((value >> 16) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 3] = (byte)(value & 0xFF);
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private sealed class InstallerProcessRunner : IProcessRunner
    {
        public int DotnetMsbuildInvocations { get; private set; }

        public Task<int> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            BuildLogger logger,
            IReadOnlyDictionary<string, string?>? environment = null,
            CancellationToken cancellationToken = default
        )
        {
            if (
                string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase)
                && arguments.Count > 1
                && string.Equals(arguments[0], "msbuild", StringComparison.OrdinalIgnoreCase)
            )
            {
                DotnetMsbuildInvocations++;
                var builtPath = Path.Combine(workingDirectory, "bin", "Release", "MicaSetup.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(builtPath)!);
                File.WriteAllBytes(builtPath, CreateExecutableBytes());
                return Task.FromResult(0);
            }

            logger.Info($"{fileName} {string.Join(" ", arguments)}");
            return Task.FromResult(0);
        }
    }
}
