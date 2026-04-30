using System.IO.Compression;
using System.Reflection;
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
    public async Task PackageInstallerBuildsExecutableAndStagingArchiveFromRepoOwnedToolchain()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var outputRoot = Path.Combine(_rootDirectory, "out-success");
        CreatePublishRoot(outputRoot);
        CreateWebView2Cache(outputRoot);
        CreateRepoOwnedToolchain(repositoryRoot);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var runner = new InstallerProcessRunner(forceFallback: false);
        var context = CreateBuildContext(repositoryRoot, outputRoot, output, error, runner);

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
        Assert.True(File.Exists(stagingPackagePath));
        using var archive = ZipFile.OpenRead(stagingPackagePath);
        Assert.Contains(
            archive.Entries,
            entry => entry.FullName == "output/CodexCliPlus.Setup.Offline.9.9.9.exe"
        );
        Assert.Contains(archive.Entries, entry => entry.FullName == "micasetup.json");
        Assert.Contains(
            archive.Entries,
            entry => entry.FullName == "app-package/assets/webui/upstream/dist/index.html"
        );
        Assert.Contains(
            archive.Entries,
            entry => entry.FullName == "app-package/assets/webui/upstream/dist/assets/app.js"
        );
        Assert.Contains(
            archive.Entries,
            entry => entry.FullName == "app-package/assets/webui/upstream/sync.json"
        );
        Assert.Contains(
            archive.Entries,
            entry => entry.FullName == "app-package/packaging/dependency-precheck.json"
        );
        Assert.Contains(
            archive.Entries,
            entry =>
                entry.FullName
                == $"app-package/{WebView2RuntimeAssets.PackagedDirectory}/{WebView2RuntimeAssets.BootstrapperFileName}"
        );
        Assert.Contains(
            archive.Entries,
            entry =>
                entry.FullName
                == $"app-package/{WebView2RuntimeAssets.PackagedDirectory}/{WebView2RuntimeAssets.StandaloneX64FileName}"
        );
        Assert.Contains(
            archive.Entries,
            entry => entry.FullName == "app-package/packaging/uninstall-cleanup.json"
        );

        var installerPlan = ReadArchiveJson<InstallerPlan>(archive, "mica-setup.json");
        Assert.True(installerPlan.CleanupInstallerAfterInstallDefault);

        var micaConfig = ReadArchiveJson<JsonElement>(archive, "micasetup.json");
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

        var cleanupManifest = ReadArchiveJson<InstallerCleanupManifest>(
            archive,
            "app-package/packaging/uninstall-cleanup.json"
        );
        Assert.Equal("KeepMyData", cleanupManifest.KeepMyDataOptionName);
        Assert.False(cleanupManifest.KeepMyDataDefault);
        Assert.Equal("full-clean", cleanupManifest.DefaultUninstallProfile);
        Assert.Contains(
            "%ProgramFiles%\\CodexCliPlus\\config\\secrets\\*.bin",
            cleanupManifest.DeleteByDefault
        );
        Assert.Contains("%ProgramFiles%\\CodexCliPlus", cleanupManifest.AlwaysDelete);

        var dependencyPrecheck = ReadArchiveJson<JsonElement>(
            archive,
            "app-package/packaging/dependency-precheck.json"
        );
        Assert.True(
            dependencyPrecheck.GetProperty("webView2").GetProperty("required").GetBoolean()
        );
        Assert.True(
            dependencyPrecheck.GetProperty("webView2").GetProperty("bundledFirst").GetBoolean()
        );
        Assert.Equal(
            "online-bootstrapper-then-bundled-standalone",
            dependencyPrecheck.GetProperty("webView2").GetProperty("installStrategy").GetString()
        );
        Assert.Equal(
            WebView2RuntimeAssets.BootstrapperFileName,
            dependencyPrecheck
                .GetProperty("webView2")
                .GetProperty("onlineBootstrapper")
                .GetProperty("fileName")
                .GetString()
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

        Assert.Contains("MicaSetup tools repo-owned", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageInstallerUsesRepoOwnedSourceBuildWhenMakemicaOutputIsInvalid()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var outputRoot = Path.Combine(_rootDirectory, "out-fallback");
        CreatePublishRoot(outputRoot);
        CreateWebView2Cache(outputRoot);
        CreateRepoOwnedToolchain(repositoryRoot);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var runner = new InstallerProcessRunner(forceFallback: true);
        var context = CreateBuildContext(repositoryRoot, outputRoot, output, error, runner);

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
        Assert.DoesNotContain("__PAYLOAD_UNCOMPRESSED_BYTES__", setupMainViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("ArchiveFileHelper.TotalUncompressSize", setupMainViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("publish.7z", setupMainViewModel, StringComparison.Ordinal);
        Assert.Contains("LoadLicenseInfo", setupMainViewModel, StringComparison.Ordinal);
        Assert.Contains("EnsureCodexCliPlusWebView2RuntimeInstalled", installViewModel, StringComparison.Ordinal);
        Assert.Contains(WebView2RuntimeAssets.BootstrapperFileName, installViewModel, StringComparison.Ordinal);
        Assert.Contains(WebView2RuntimeAssets.StandaloneX64FileName, installViewModel, StringComparison.Ordinal);
        Assert.Contains("无法安装 Microsoft Edge WebView2 Runtime", installViewModel, StringComparison.Ordinal);
        Assert.Contains("完成后删除安装包", finishPage, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding CleanupInstallerAfterInstall}\"", finishPage, StringComparison.Ordinal);
        Assert.Contains("CleanupOriginalInstallerAfterInstall", finishViewModel, StringComparison.Ordinal);
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
        Assert.Contains("stream.Position = originalPosition;", archiveFileHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("using dynamic? archive = type switch", archiveFileHelper, StringComparison.Ordinal);
        Assert.Contains("long totalUncompressSize = archive.TotalUncompressSize;", archiveFileHelper, StringComparison.Ordinal);
        Assert.True(File.Exists(renderedLicensePath));
        Assert.Equal("notice", File.ReadAllText(renderedLicensePath));

        Assert.True(
            ReadPngWidth(Path.Combine(distRoot, "Resources", "Images", "Favicon.png")) >= 256
        );
        Assert.True(
            ReadPngWidth(Path.Combine(distRoot, "Resources", "Images", "FaviconSetup.png"))
                >= 256
        );
        Assert.True(
            ReadPngWidth(Path.Combine(distRoot, "Resources", "Images", "FaviconUninst.png"))
                >= 256
        );
    }

    [Fact]
    public void MakeMicaCompatibilityIncludesVisualStudioBuildToolsInstances()
    {
        var field = typeof(MakeMicaVisualStudioCompatibility).GetField(
            "Vs2026AwareVsWhereArguments",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.NotNull(field);
        Assert.Equal(
            "-latest -products * -property installationPath",
            field!.GetRawConstantValue()
        );
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
        IProcessRunner runner
    )
    {
        return new BuildContext(
            new BuildOptions(
                "package-offline-installer",
                repositoryRoot,
                outputRoot,
                "Release",
                "win-x64",
                "9.9.9"
            ),
            new BuildLogger(output, error),
            runner,
            new NoOpSigningService()
        );
    }

    private string CreateRepositoryRoot()
    {
        var repositoryRoot = Path.Combine(_rootDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "CodexCliPlus.sln"), string.Empty);
        File.WriteAllText(Path.Combine(repositoryRoot, "LICENSE.txt"), "license");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "build", "micasetup"));
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

    private static void CreateRepoOwnedToolchain(string repositoryRoot)
    {
        var toolchainRoot = Path.Combine(
            repositoryRoot,
            "build",
            "micasetup",
            "toolchain",
            "build"
        );
        Directory.CreateDirectory(Path.Combine(toolchainRoot, "bin"));
        Directory.CreateDirectory(Path.Combine(toolchainRoot, "template"));
        File.WriteAllBytes(Path.Combine(toolchainRoot, "bin", "7z.exe"), CreateExecutableBytes());
        File.WriteAllBytes(Path.Combine(toolchainRoot, "makemica.exe"), CreateExecutableBytes());
        File.WriteAllBytes(Path.Combine(toolchainRoot, "template", "default.7z"), [1, 2, 3, 4]);
        File.WriteAllText(
            Path.Combine(
                repositoryRoot,
                "build",
                "micasetup",
                "toolchain",
                "micasetup-tools-version.txt"
            ),
            "repo-owned-test"
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
            private bool EnsureCodexCliPlusWebView2RuntimeInstalled()
            {
                string bootstrapperPath = "{{WebView2RuntimeAssets.BootstrapperFileName}}";
                string standalonePath = "{{WebView2RuntimeAssets.StandaloneX64FileName}}";
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

    private sealed class InstallerProcessRunner(bool forceFallback) : IProcessRunner
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
            if (fileName.EndsWith("7z.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (
                    arguments.Count > 0
                    && string.Equals(arguments[0], "a", StringComparison.OrdinalIgnoreCase)
                )
                {
                    File.WriteAllBytes(arguments[1], [1, 2, 3, 4]);
                    return Task.FromResult(0);
                }

                if (
                    arguments.Count > 0
                    && string.Equals(arguments[0], "x", StringComparison.OrdinalIgnoreCase)
                )
                {
                    var outputArgument = arguments.Single(arg =>
                        arg.StartsWith("-o", StringComparison.Ordinal)
                    );
                    CreateTemplateTree(outputArgument[2..]);
                    return Task.FromResult(0);
                }
            }

            if (fileName.EndsWith("makemica.exe", StringComparison.OrdinalIgnoreCase))
            {
                var outputPath = ReadOutputPath(arguments[0]);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllBytes(
                    outputPath,
                    forceFallback ? Encoding.UTF8.GetBytes("bad") : CreateExecutableBytes()
                );
                return Task.FromResult(0);
            }

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

        private static string ReadOutputPath(string micaConfigPath)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(micaConfigPath));
            return document.RootElement.GetProperty("Output").GetString()
                ?? throw new Xunit.Sdk.XunitException("micasetup.json is missing Output.");
        }

        private static void CreateTemplateTree(string distRoot)
        {
            Directory.CreateDirectory(distRoot);
            Directory.CreateDirectory(Path.Combine(distRoot, "Helper", "Setup"));
            Directory.CreateDirectory(Path.Combine(distRoot, "Helper", "System"));
            Directory.CreateDirectory(Path.Combine(distRoot, "Resources", "Images"));
            Directory.CreateDirectory(Path.Combine(distRoot, "ViewModels", "Uninst"));
            Directory.CreateDirectory(Path.Combine(distRoot, "ViewModels", "Inst"));
            Directory.CreateDirectory(Path.Combine(distRoot, "Views", "Inst"));
            Directory.CreateDirectory(Path.Combine(distRoot, "Resources", "Setups"));
            File.WriteAllText(Path.Combine(distRoot, "MicaSetup.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(distRoot, "MicaSetup.Uninst.csproj"), "<Project />");
            File.WriteAllText(
                Path.Combine(distRoot, "Program.cs"),
                """
                [assembly: Guid("old-guid")]
                [assembly: AssemblyTitle("Old Title")]
                [assembly: AssemblyProduct("Old Product")]
                [assembly: AssemblyDescription("Old Description")]
                [assembly: AssemblyCompany("Old Company")]
                [assembly: AssemblyVersion("1.0.0.0")]
                [assembly: AssemblyFileVersion("1.0.0.0")]
                [assembly: RequestExecutionLevel("admin")]
                Hosting.CreateBuilder().UseElevated().UseSingleInstance("Original.Setup").UseOptions(option => { option.IsCreateDesktopShortcut = false; option.IsCreateUninst = false; option.IsUninstLower = true; option.IsCreateStartMenu = false; option.IsPinToStartMenu = true; option.IsCreateQuickLaunch = true; option.IsCreateRegistryKeys = false; option.IsCreateAsAutoRun = true; option.IsCustomizeVisiableAutoRun = false; option.AutoRunLaunchCommand = ""; option.IsUseInstallPathPreferX86 = true; option.IsUseInstallPathPreferAppDataLocalPrograms = false; option.IsUseInstallPathPreferAppDataRoaming = true; option.IsAllowFullFolderSecurity = true; option.IsAllowFirewall = true; option.IsRefreshExplorer = false; option.IsInstallCertificate = true; option.IsEnableUninstallDelayUntilReboot = false; option.IsEnvironmentVariable = true; option.AppName = "OldApp"; option.KeyName = "OLD"; option.ExeName = "Old.exe"; option.DisplayVersion = "0.0.0"; option.Publisher = "Old Publisher"; option.MessageOfPage1 = "old1"; option.MessageOfPage2 = "old2"; option.MessageOfPage3 = "old3"; });
                """
            );
            File.WriteAllText(
                Path.Combine(distRoot, "Program.un.cs"),
                """
                [assembly: Guid("old-guid")]
                [assembly: AssemblyTitle("Old Title")]
                [assembly: AssemblyProduct("Old Product")]
                [assembly: AssemblyDescription("Old Description")]
                [assembly: AssemblyCompany("Old Company")]
                [assembly: AssemblyVersion("1.0.0.0")]
                [assembly: AssemblyFileVersion("1.0.0.0")]
                [assembly: RequestExecutionLevel("admin")]
                Hosting.CreateBuilder().UseElevated().UseSingleInstance("Original.Uninstall").UseOptions(option => { option.IsCreateDesktopShortcut = false; option.IsCreateUninst = false; option.IsUninstLower = true; option.IsCreateStartMenu = false; option.IsPinToStartMenu = true; option.IsCreateQuickLaunch = true; option.IsCreateRegistryKeys = false; option.IsCreateAsAutoRun = true; option.IsCustomizeVisiableAutoRun = false; option.AutoRunLaunchCommand = ""; option.IsUseInstallPathPreferX86 = true; option.IsUseInstallPathPreferAppDataLocalPrograms = false; option.IsUseInstallPathPreferAppDataRoaming = true; option.IsAllowFullFolderSecurity = true; option.IsAllowFirewall = true; option.IsRefreshExplorer = false; option.IsInstallCertificate = true; option.IsEnableUninstallDelayUntilReboot = false; option.IsEnvironmentVariable = true; option.AppName = "OldApp"; option.KeyName = "OLD"; option.ExeName = "Old.exe"; option.DisplayVersion = "0.0.0"; option.Publisher = "Old Publisher"; option.MessageOfPage1 = "old1"; option.MessageOfPage2 = "old2"; option.MessageOfPage3 = "old3"; });
                """
            );
            File.WriteAllText(
                Path.Combine(distRoot, "Helper", "System", "StartMenuHelper.cs"),
                "var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @\"Microsoft\\Windows\\Start Menu\\Programs\");"
            );
            File.WriteAllText(
                Path.Combine(distRoot, "Helper", "System", "RegistyUninstallHelper.cs"),
                "var hive = RegistryHive.LocalMachine;"
            );
            File.WriteAllText(
                Path.Combine(distRoot, "Helper", "Setup", "InstallHelper.cs"),
                """
                if (Option.Current.IsCreateRegistryKeys && RuntimeHelper.IsElevated) { }
                if (RuntimeHelper.IsElevated) { StartMenuHelper.CreateStartMenuFolder(Option.Current.DisplayName, Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName), Option.Current.IsCreateUninst); }
                """
            );
            File.WriteAllText(
                Path.Combine(distRoot, "ViewModels", "Uninst", "MainViewModel.cs"),
                "private bool isElevated = RuntimeHelper.IsElevated;"
            );
            File.WriteAllText(
                Path.Combine(distRoot, "ViewModels", "Inst", "InstallViewModel.cs"),
                """
                using MicaSetup.Design.ComponentModel;
                using MicaSetup.Design.Controls;
                using MicaSetup.Helper;
                using MicaSetup.Helper.Helper;
                using System;
                using System.Collections.Generic;
                using System.ComponentModel;
                using System.IO;
                using System.Threading.Tasks;

                namespace MicaSetup.ViewModels;

                public partial class InstallViewModel : ObservableObject
                {
                    public string Message => Option.Current.MessageOfPage2;
                    private string installInfo = string.Empty;
                    public string InstallInfo { get => installInfo; set => installInfo = value; }

                    public InstallViewModel()
                    {
                        _ = Task.Run(() =>
                        {
                            using Stream uninstStream = ResourceHelper.GetStream("pack://application:,,,/MicaSetup;component/Resources/Setups/Uninst.exe");
                            InstallHelper.CreateUninst(uninstStream);
                            ApplicationDispatcherHelper.Invoke(Routing.GoToNext);
                        });
                    }
                }

                partial class InstallViewModel
                {
                }
                """
            );
            File.WriteAllText(
                Path.Combine(distRoot, "Views", "Inst", "FinishPage.xaml"),
                """
                <UserControl x:Class="MicaSetup.Views.FinishPage"
                             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <StackPanel>
                        <TextBlock Text="{Binding Message}" />
                        <StackPanel Grid.Row="2"
                                    Margin="0,56,0,0"
                                    HorizontalAlignment="Center"
                                    Orientation="Horizontal">
                            <Button Command="{Binding CloseCommand}" />
                            <Button Command="{Binding OpenCommand}" />
                        </StackPanel>
                    </StackPanel>
                </UserControl>
                """
            );
            File.WriteAllText(
                Path.Combine(distRoot, "ViewModels", "Inst", "FinishViewModel.cs"),
                """
                using MicaSetup.Design.Commands;
                using MicaSetup.Design.ComponentModel;
                using MicaSetup.Helper;
                using System;
                using System.IO;
                using System.Windows;

                namespace MicaSetup.ViewModels;

                public partial class FinishViewModel : ObservableObject
                {
                    public string Message => Option.Current.MessageOfPage3;

                    [RelayCommand]
                    public void Close()
                    {
                        if (ApplicationDispatcherHelper.MainWindow is Window window)
                        {
                            SystemCommands.CloseWindow(window);
                        }
                    }

                    [RelayCommand]
                    public void Open()
                    {
                        if (ApplicationDispatcherHelper.MainWindow is Window window)
                        {
                            try
                            {
                                FluentProcess.Create()
                                    .FileName(Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName))
                                    .WorkingDirectory(Option.Current.InstallLocation)
                                    .UseShellExecute()
                                    .Start()
                                    .Forget();
                            }
                            catch (Exception e)
                            {
                                Logger.Error(e);
                            }
                            SystemCommands.CloseWindow(window);
                        }
                    }
                }

                partial class FinishViewModel
                {
                }
                """
            );
            File.WriteAllText(
                Path.Combine(distRoot, "Helper", "Setup", "UninstallHelper.cs"),
                """
                using System.IO;
                else { // For security reason, uninst should always keep data because of unundering admin. Option.Current.KeepMyData = true; uinfo = PrepareUninstallPathHelper.GetPrepareUninstallPath(); if (string.IsNullOrWhiteSpace(uinfo.UninstallData)) { MessageBox.Info(ApplicationDispatcherHelper.MainWindow, "InstallationInfoLostHint".Tr()); } }
                try { RegistyUninstallHelper.Delete(Option.Current.KeyName); }
                public static void DeleteUninst()
                """
            );
        }
    }
}
