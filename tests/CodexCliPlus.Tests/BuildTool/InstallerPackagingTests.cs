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
            entry => entry.FullName == "app-package/packaging/uninstall-cleanup.json"
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
    public async Task PackageInstallerFallsBackToPatchedTemplateWhenMakemicaOutputIsInvalid()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var outputRoot = Path.Combine(_rootDirectory, "out-fallback");
        CreatePublishRoot(outputRoot);
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
        var uninstallHelper = File.ReadAllText(
            Path.Combine(distRoot, "Helper", "Setup", "UninstallHelper.cs")
        );

        Assert.Contains(".UseElevated()", setupProgram, StringComparison.Ordinal);
        Assert.Contains("BlackblockInc.CodexCliPlus.Setup", setupProgram, StringComparison.Ordinal);
        Assert.Contains("RequestExecutionLevel(\"admin\")", setupProgram, StringComparison.Ordinal);
        Assert.Contains("option.KeepMyData = false;", uninstProgram, StringComparison.Ordinal);
        Assert.Contains("CleanupCodexCliPlusUserData", uninstallHelper, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Option.Current.KeepMyData = true;",
            uninstallHelper,
            StringComparison.Ordinal
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
        return repositoryRoot;
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
            Directory.CreateDirectory(Path.Combine(distRoot, "ViewModels", "Uninst"));
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
