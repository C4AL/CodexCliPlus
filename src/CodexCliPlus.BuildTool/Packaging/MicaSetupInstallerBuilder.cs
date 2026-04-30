using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexCliPlus.Core.Constants;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CodexCliPlus.BuildTool;

public sealed class MicaSetupInstallerBuilder(MicaSetupToolchain toolchain)
{
    private const string CleanupMethodMarker = "private static void CleanupCodexCliPlusUserData()";
    private const string WebView2InstallCallMarker =
        "EnsureCodexCliPlusWebView2RuntimeInstalled()";
    private const string WebView2InstallMethodMarker =
        "private bool EnsureCodexCliPlusWebView2RuntimeInstalled()";
    private const string FinishCleanupMethodMarker =
        "private void CleanupOriginalInstallerAfterInstall()";

    private const string UninstallCleanupSource = """
                private static void CleanupCodexCliPlusUserData()
                {
                    RegistyAutoRunHelper.Disable("CodexCliPlus");

                    if (Option.Current.KeepMyData)
                    {
                        return;
                    }

                    foreach (string path in EnumerateCodexCliPlusCleanupRoots())
                    {
                        TryDeleteSafePath(path);
                    }

                    TryDeleteFirewallRule("CodexCliPlus");
                    TryDeleteScheduledTask("CodexCliPlus");
                }

                private static IEnumerable<string> EnumerateCodexCliPlusCleanupRoots()
                {
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexCliPlus");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexCliPlus");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CodexCliPlus");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "CodexCliPlus");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "CodexCliPlus");
                }

                private static void TryDeleteSafePath(string path)
                {
                    if (string.IsNullOrWhiteSpace(path) || !IsSafeCodexCliPlusPath(path))
                    {
                        return;
                    }

                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                        else if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }

                private static bool IsSafeCodexCliPlusPath(string path)
                {
                    string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string name = Path.GetFileName(fullPath);
                    if (!string.Equals(name, "CodexCliPlus", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
                        || IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
                        || IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
                        || IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                        || IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))
                        || IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
                }

                private static bool IsEqualOrUnder(string fullPath, string rootPath)
                {
                    if (string.IsNullOrWhiteSpace(rootPath))
                    {
                        return false;
                    }

                    string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                        || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        || fullPath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                }

                private static void TryDeleteFirewallRule(string ruleName)
                {
                    TryStartAndWait("netsh.exe", $"advfirewall firewall delete rule name=\"{ruleName}\"");
                }

                private static void TryDeleteScheduledTask(string taskName)
                {
                    TryStartAndWait("schtasks.exe", $"/Delete /TN \"{taskName}\" /F");
                }

                private static void TryStartAndWait(string fileName, string arguments)
                {
                    try
                    {
                        using Process? process = Process.Start(new ProcessStartInfo
                        {
                            FileName = fileName,
                            Arguments = arguments,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        process?.WaitForExit(5000);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }
        """;

    private const string WebView2RuntimeInstallSource = """

            private bool EnsureCodexCliPlusWebView2RuntimeInstalled()
            {
                InstallInfo = "正在检查 WebView2 运行时";
                if (IsCodexCliPlusWebView2RuntimeInstalled())
                {
                    return true;
                }

                string webView2Root = Path.Combine(Option.Current.InstallLocation, "packaging", "dependencies", "webview2");
                string bootstrapperPath = Path.Combine(webView2Root, "MicrosoftEdgeWebview2Setup.exe");
                string standalonePath = Path.Combine(webView2Root, "MicrosoftEdgeWebView2RuntimeInstallerX64.exe");

                InstallInfo = "正在安装 WebView2 运行时（在线）";
                int? bootstrapperExitCode = RunCodexCliPlusWebView2Installer(
                    bootstrapperPath,
                    "/silent /install");
                if (WaitForCodexCliPlusWebView2Runtime())
                {
                    return true;
                }

                InstallInfo = "正在安装 WebView2 运行时（离线）";
                int? standaloneExitCode = RunCodexCliPlusWebView2Installer(
                    standalonePath,
                    "/silent /install");
                if (WaitForCodexCliPlusWebView2Runtime())
                {
                    return true;
                }

                string reason = "无法安装 Microsoft Edge WebView2 Runtime。" +
                    $"在线安装退出码：{FormatCodexCliPlusExitCode(bootstrapperExitCode)}；" +
                    $"离线安装退出码：{FormatCodexCliPlusExitCode(standaloneExitCode)}。" +
                    "请确认安装包完整，或手动安装 WebView2 Runtime 后再启动 CodexCliPlus。";
                Logger.Error(reason);
                InstallInfo = "WebView2 运行时安装失败";
                ApplicationDispatcherHelper.Invoke(() =>
                {
                    _ = MessageBox.Info(null!, reason);
                });
                return false;
            }

            private static bool WaitForCodexCliPlusWebView2Runtime()
            {
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    if (IsCodexCliPlusWebView2RuntimeInstalled())
                    {
                        return true;
                    }

                    System.Threading.Thread.Sleep(500);
                }

                return false;
            }

            private static int? RunCodexCliPlusWebView2Installer(string fileName, string arguments)
            {
                if (!File.Exists(fileName))
                {
                    Logger.Error($"[WebView2] installer missing: {fileName}");
                    return null;
                }

                try
                {
                    Logger.Information($"[WebView2] start installer: {fileName} {arguments}");
                    using Process? process = Process.Start(new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        WorkingDirectory = Path.GetDirectoryName(fileName) ?? Option.Current.InstallLocation,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (process is null)
                    {
                        Logger.Error($"[WebView2] installer did not start: {fileName}");
                        return null;
                    }

                    if (!process.WaitForExit(10 * 60 * 1000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception killException)
                        {
                            Logger.Error(killException);
                        }

                        Logger.Error($"[WebView2] installer timed out: {fileName}");
                        return -1;
                    }

                    Logger.Information($"[WebView2] installer exit code: {process.ExitCode}");
                    return process.ExitCode;
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                    return null;
                }
            }

            private static string FormatCodexCliPlusExitCode(int? exitCode)
            {
                return exitCode.HasValue ? exitCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "未启动";
            }

            private static bool IsCodexCliPlusWebView2RuntimeInstalled()
            {
                return HasCodexCliPlusWebView2RegistryVersion()
                    || HasCodexCliPlusWebView2Executable();
            }

            private static bool HasCodexCliPlusWebView2RegistryVersion()
            {
                const string clientKey = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
                const string clientKeyWow64 = @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

                return HasCodexCliPlusWebView2RegistryVersion(RegistryHive.LocalMachine, RegistryView.Registry64, clientKey)
                    || HasCodexCliPlusWebView2RegistryVersion(RegistryHive.LocalMachine, RegistryView.Registry32, clientKey)
                    || HasCodexCliPlusWebView2RegistryVersion(RegistryHive.LocalMachine, RegistryView.Registry64, clientKeyWow64)
                    || HasCodexCliPlusWebView2RegistryVersion(RegistryHive.CurrentUser, RegistryView.Registry64, clientKey)
                    || HasCodexCliPlusWebView2RegistryVersion(RegistryHive.CurrentUser, RegistryView.Registry32, clientKey);
            }

            private static bool HasCodexCliPlusWebView2RegistryVersion(RegistryHive hive, RegistryView view, string keyPath)
            {
                try
                {
                    using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using RegistryKey? key = baseKey.OpenSubKey(keyPath);
                    string? version = key?.GetValue("pv") as string;
                    return !string.IsNullOrWhiteSpace(version)
                        && !string.Equals(version, "0.0.0.0", StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                    return false;
                }
            }

            private static bool HasCodexCliPlusWebView2Executable()
            {
                foreach (string root in EnumerateCodexCliPlusWebView2InstallRoots())
                {
                    try
                    {
                        if (!Directory.Exists(root))
                        {
                            continue;
                        }

                        foreach (string _ in Directory.EnumerateFiles(root, "msedgewebview2.exe", SearchOption.AllDirectories))
                        {
                            return true;
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }

                return false;
            }

            private static IEnumerable<string> EnumerateCodexCliPlusWebView2InstallRoots()
            {
                string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrWhiteSpace(programFilesX86))
                {
                    yield return Path.Combine(programFilesX86, "Microsoft", "EdgeWebView", "Application");
                }

                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrWhiteSpace(programFiles))
                {
                    yield return Path.Combine(programFiles, "Microsoft", "EdgeWebView", "Application");
                }

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrWhiteSpace(localAppData))
                {
                    yield return Path.Combine(localAppData, "Microsoft", "EdgeWebView", "Application");
                }
            }
    """;

    private const string FinishCleanupSource = """

            private void CleanupOriginalInstallerAfterInstall()
            {
                if (!CleanupInstallerAfterInstall)
                {
                    return;
                }

                try
                {
                    if (!CommandLineHelper.Has(TempPathForkHelper.ForkedCli))
                    {
                        Logger.Information("[InstallerCleanup] skip because installer was not started from a temp fork.");
                        return;
                    }

                    string originalPath = CommandLineHelper.Values[TempPathForkHelper.ForkedCli];
                    if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath))
                    {
                        Logger.Information($"[InstallerCleanup] original installer missing: {originalPath}");
                        return;
                    }

                    string originalFullPath = Path.GetFullPath(originalPath);
                    string currentFullPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.FriendlyName));
                    if (string.Equals(originalFullPath, currentFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Warning("[InstallerCleanup] original installer path is the current fork path; skip delete.");
                        return;
                    }

                    if (!string.Equals(Path.GetExtension(originalFullPath), ".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Warning($"[InstallerCleanup] original installer is not an exe: {originalFullPath}");
                        return;
                    }

                    string originalHash = ComputeCodexCliPlusSha256(originalFullPath);
                    string currentHash = ComputeCodexCliPlusSha256(currentFullPath);
                    if (!string.Equals(originalHash, currentHash, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Warning("[InstallerCleanup] original installer hash does not match the current fork; skip delete.");
                        return;
                    }

                    File.Delete(originalFullPath);
                    Logger.Information($"[InstallerCleanup] deleted original installer: {originalFullPath}");
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
            }

            private static string ComputeCodexCliPlusSha256(string path)
            {
                using SHA256 sha256 = SHA256.Create();
                using FileStream stream = File.OpenRead(path);
                byte[] hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
    """;

    public async Task<int> BuildAsync(
        BuildContext context,
        string micaConfigPath,
        string payloadArchivePath,
        string installerOutputPath
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(installerOutputPath)!);
        if (File.Exists(installerOutputPath))
        {
            File.Delete(installerOutputPath);
        }

        context.Logger.Info(
            "Using patched MicaSetup source build so installer dependency repair and cleanup logic are included."
        );
        return await BuildWithDotnetMsbuildAsync(
            context,
            micaConfigPath,
            payloadArchivePath,
            installerOutputPath
        );
    }

    private async Task<int> BuildWithDotnetMsbuildAsync(
        BuildContext context,
        string micaConfigPath,
        string payloadArchivePath,
        string installerOutputPath
    )
    {
        var stageRoot = Path.GetDirectoryName(micaConfigPath)!;
        var distRoot = Path.Combine(stageRoot, ".dist");
        SafeFileSystem.CleanDirectory(distRoot, context.Options.OutputRoot);

        var extractExitCode = await context.ProcessRunner.RunAsync(
            toolchain.SevenZipPath,
            ["x", toolchain.TemplatePath, $"-o{distRoot}", "-y"],
            stageRoot,
            context.Logger
        );
        if (extractExitCode != 0)
        {
            context.Logger.Error(
                $"MicaSetup template extraction failed with exit code {extractExitCode}."
            );
            return extractExitCode;
        }

        ApplyTemplateConfig(context, distRoot, payloadArchivePath);

        var uninstExitCode = await context.ProcessRunner.RunAsync(
            "dotnet",
            [
                "msbuild",
                Path.Combine(distRoot, "MicaSetup.Uninst.csproj"),
                "/t:Rebuild",
                "/p:Configuration=Release",
                "/p:DeployOnBuild=true",
                "/p:PublishProfile=FolderProfile",
                "/p:ImportDirectoryBuildProps=false",
                "/p:RestoreUseStaticGraphEvaluation=false",
                "/restore",
            ],
            distRoot,
            context.Logger
        );
        if (uninstExitCode != 0)
        {
            context.Logger.Error(
                $"MicaSetup uninstaller build failed with exit code {uninstExitCode}."
            );
            return uninstExitCode;
        }

        var builtUninstaller = Path.Combine(distRoot, "bin", "Release", "MicaSetup.exe");
        var uninstallerResource = Path.Combine(distRoot, "Resources", "Setups", "Uninst.exe");
        if (!File.Exists(builtUninstaller))
        {
            context.Logger.Error($"MicaSetup uninstaller output missing: {builtUninstaller}");
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(uninstallerResource)!);
        File.Copy(builtUninstaller, uninstallerResource, overwrite: true);

        var setupExitCode = await context.ProcessRunner.RunAsync(
            "dotnet",
            [
                "msbuild",
                Path.Combine(distRoot, "MicaSetup.csproj"),
                "/t:Rebuild",
                "/p:Configuration=Release",
                "/p:DeployOnBuild=true",
                "/p:PublishProfile=FolderProfile",
                "/p:ImportDirectoryBuildProps=false",
                "/p:RestoreUseStaticGraphEvaluation=false",
                "/restore",
            ],
            distRoot,
            context.Logger
        );
        if (setupExitCode != 0)
        {
            context.Logger.Error($"MicaSetup setup build failed with exit code {setupExitCode}.");
            return setupExitCode;
        }

        var builtInstaller = Path.Combine(distRoot, "bin", "Release", "MicaSetup.exe");
        if (!File.Exists(builtInstaller))
        {
            context.Logger.Error($"MicaSetup installer output missing: {builtInstaller}");
            return 1;
        }

        File.Copy(builtInstaller, installerOutputPath, overwrite: true);
        var validationFailure = WindowsExecutableValidation.ValidateFile(installerOutputPath);
        if (validationFailure is not null)
        {
            context.Logger.Error(validationFailure);
            return 1;
        }

        context.Logger.Info("MicaSetup installer generated by dotnet msbuild fallback");
        return 0;
    }

    private static void ApplyTemplateConfig(
        BuildContext context,
        string distRoot,
        string payloadArchivePath
    )
    {
        var setupResourcePath = Path.Combine(distRoot, "Resources", "Setups", "publish.7z");
        Directory.CreateDirectory(Path.GetDirectoryName(setupResourcePath)!);
        File.Copy(payloadArchivePath, setupResourcePath, overwrite: true);

        var cleanupManifestSource = Path.Combine(
            Path.GetDirectoryName(payloadArchivePath)!,
            "uninstall-cleanup.json"
        );
        if (File.Exists(cleanupManifestSource))
        {
            File.Copy(
                cleanupManifestSource,
                Path.Combine(distRoot, "Resources", "Setups", "uninstall-cleanup.json"),
                overwrite: true
            );
        }

        CopyLicenseDocuments(context, Path.Combine(distRoot, "Resources", "Licenses"));
        CopyInstallerImages(context, Path.Combine(distRoot, "Resources", "Images"));

        var setupProgram = Path.Combine(distRoot, "Program.cs");
        var uninstProgram = Path.Combine(distRoot, "Program.un.cs");
        PatchProgramSource(setupProgram, context.Options.Version, isUninstaller: false);
        PatchProgramSource(uninstProgram, context.Options.Version, isUninstaller: true);
        PatchForCurrentUserInstall(distRoot);
        PatchWebView2RuntimeInstall(distRoot);
        PatchFinishPageCleanup(distRoot);
        PatchUninstallCleanup(distRoot);
    }

    private static void CopyLicenseDocuments(BuildContext context, string targetDirectory)
    {
        var repositoryRoot = context.Options.RepositoryRoot;
        var documents = new (string Source, string Target)[]
        {
            (Path.Combine(repositoryRoot, "LICENSE.txt"), "CodexCliPlus.LICENSE.txt"),
            (
                Path.Combine(context.AssetsRoot, "backend", "windows-x64", "LICENSE"),
                "CLIProxyAPI.LICENSE.txt"
            ),
            (
                Path.Combine(repositoryRoot, "resources", "licenses", "BetterGI.GPL-3.0.txt"),
                "BetterGI.GPL-3.0.txt"
            ),
            (Path.Combine(repositoryRoot, "resources", "licenses", "NOTICE.txt"), "NOTICE.txt"),
        };

        foreach (var (source, target) in documents)
        {
            if (!File.Exists(source))
            {
                continue;
            }

            Directory.CreateDirectory(targetDirectory);
            File.Copy(source, Path.Combine(targetDirectory, target), overwrite: true);
        }
    }

    private static void CopyInstallerImages(BuildContext context, string targetDirectory)
    {
        var repositoryRoot = context.Options.RepositoryRoot;
        var displayPngPath = Path.Combine(
            repositoryRoot,
            "resources",
            "icons",
            "codexcliplus-display.png"
        );
        var iconPath = Path.Combine(repositoryRoot, "resources", "icons", "codexcliplus.ico");
        Directory.CreateDirectory(targetDirectory);

        if (File.Exists(displayPngPath))
        {
            foreach (var target in new[] { "Favicon.png", "FaviconSetup.png", "FaviconUninst.png" })
            {
                File.Copy(displayPngPath, Path.Combine(targetDirectory, target), overwrite: true);
            }
        }

        if (File.Exists(iconPath))
        {
            foreach (var target in new[] { "Favicon.ico", "FaviconSetup.ico", "FaviconUninst.ico" })
            {
                File.Copy(iconPath, Path.Combine(targetDirectory, target), overwrite: true);
            }
        }
    }

    private static void PatchProgramSource(string path, string version, bool isUninstaller)
    {
        var source = File.ReadAllText(path);
        if (!source.Contains(".UseElevated()", StringComparison.Ordinal))
        {
            source = source.Replace(
                "Hosting.CreateBuilder()",
                "Hosting.CreateBuilder().UseElevated()",
                StringComparison.Ordinal
            );
        }

        source = ReplaceBetween(
            source,
            ".UseSingleInstance(\"",
            "\")",
            isUninstaller
                ? "BlackblockInc.CodexCliPlus.Uninstall"
                : "BlackblockInc.CodexCliPlus.Setup"
        );
        source = ReplaceBetween(
            source,
            "[assembly: Guid(\"",
            "\")]",
            "6f8dd8b7-21ea-4c6b-9695-40a27874ce4d"
        );
        source = ReplaceBetween(
            source,
            "[assembly: AssemblyTitle(\"",
            "\")]",
            isUninstaller ? "CodexCliPlus Uninstall" : "CodexCliPlus Setup"
        );
        source = ReplaceBetween(source, "[assembly: AssemblyProduct(\"", "\")]", "CodexCliPlus");
        source = ReplaceBetween(
            source,
            "[assembly: AssemblyDescription(\"",
            "\")]",
            isUninstaller ? "CodexCliPlus Uninstall" : "CodexCliPlus Setup"
        );
        source = ReplaceBetween(source, "[assembly: AssemblyCompany(\"", "\")]", "Blackblock Inc.");
        source = ReplaceBetween(
            source,
            "[assembly: AssemblyVersion(\"",
            "\")]",
            NormalizeAssemblyVersion(version)
        );
        source = ReplaceBetween(
            source,
            "[assembly: AssemblyFileVersion(\"",
            "\")]",
            NormalizeAssemblyVersion(version)
        );
        source = ReplaceBetween(source, "[assembly: RequestExecutionLevel(\"", "\")]", "admin");

        source = ReplaceAssignment(source, "option.IsCreateDesktopShortcut", "true");
        source = ReplaceAssignment(source, "option.IsCreateUninst", "true");
        source = ReplaceAssignment(source, "option.IsUninstLower", "false");
        source = ReplaceAssignment(source, "option.IsCreateStartMenu", "true");
        source = ReplaceAssignment(source, "option.IsPinToStartMenu", "false");
        source = ReplaceAssignment(source, "option.IsCreateQuickLaunch", "false");
        source = ReplaceAssignment(source, "option.IsCreateRegistryKeys", "true");
        source = ReplaceAssignment(source, "option.IsCreateAsAutoRun", "false");
        source = ReplaceAssignment(source, "option.IsCustomizeVisiableAutoRun", "true");
        source = ReplaceAssignment(source, "option.AutoRunLaunchCommand", "\"/autostart\"");
        source = ReplaceAssignment(source, "option.IsUseInstallPathPreferX86", "false");
        source = ReplaceAssignment(
            source,
            "option.IsUseInstallPathPreferAppDataLocalPrograms",
            "false"
        );
        source = ReplaceAssignment(source, "option.IsUseInstallPathPreferAppDataRoaming", "false");
        source = ReplaceAssignment(source, "option.IsAllowFullFolderSecurity", "true");
        source = ReplaceAssignment(source, "option.IsAllowFirewall", "false");
        source = ReplaceAssignment(source, "option.IsRefreshExplorer", "true");
        source = ReplaceAssignment(source, "option.IsInstallCertificate", "false");
        source = ReplaceAssignment(source, "option.IsEnableUninstallDelayUntilReboot", "true");
        source = ReplaceAssignment(source, "option.IsEnvironmentVariable", "false");
        source = ReplaceAssignment(source, "option.AppName", "\"CodexCliPlus\"");
        source = ReplaceAssignment(source, "option.KeyName", "\"CodexCliPlus\"");
        source = ReplaceAssignment(source, "option.ExeName", "\"CodexCliPlus.exe\"");
        source = ReplaceAssignment(source, "option.DisplayVersion", $"\"{version}\"");
        source = ReplaceAssignment(source, "option.Publisher", "\"Blackblock Inc.\"");
        source = ReplaceAssignment(source, "option.MessageOfPage1", "\"CodexCliPlus\"");
        source = ReplaceAssignment(
            source,
            "option.MessageOfPage2",
            isUninstaller ? "\"正在卸载 CodexCliPlus\"" : "\"正在安装 CodexCliPlus\""
        );
        source = ReplaceAssignment(
            source,
            "option.MessageOfPage3",
            isUninstaller ? "\"卸载完成\"" : "\"安装完成\""
        );
        if (isUninstaller)
        {
            source = EnsureOptionAssignment(source, "option.KeepMyData", "false", "option.ExeName");
        }

        File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void PatchForCurrentUserInstall(string distRoot)
    {
        var uninstMainViewModel = Path.Combine(
            distRoot,
            "ViewModels",
            "Uninst",
            "MainViewModel.cs"
        );
        if (File.Exists(uninstMainViewModel))
        {
            var source = File.ReadAllText(uninstMainViewModel)
                .Replace(
                    "private bool isElevated = RuntimeHelper.IsElevated;",
                    "private bool isElevated = true;",
                    StringComparison.Ordinal
                );
            File.WriteAllText(
                uninstMainViewModel,
                source,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
        }
    }

    private static void PatchWebView2RuntimeInstall(string distRoot)
    {
        var path = Path.Combine(distRoot, "ViewModels", "Inst", "InstallViewModel.cs");
        if (!File.Exists(path))
        {
            return;
        }

        var source = File.ReadAllText(path);
        source = AddUsing(source, "using Microsoft.Win32;");
        source = AddUsing(source, "using System.Diagnostics;");
        source = AddUsing(source, "using System.Threading;");

        if (!source.Contains(WebView2InstallCallMarker, StringComparison.Ordinal))
        {
            source = source.Replace(
                """
                                InstallHelper.CreateUninst(uninstStream);
                """,
                """
                                InstallHelper.CreateUninst(uninstStream);

                                if (!EnsureCodexCliPlusWebView2RuntimeInstalled())
                                {
                                    Option.Current.Installing = false;
                                    return;
                                }
                """,
                StringComparison.Ordinal
            );
            if (!source.Contains(WebView2InstallCallMarker, StringComparison.Ordinal))
            {
                source = source.Replace(
                    "InstallHelper.CreateUninst(uninstStream);",
                    """
                    InstallHelper.CreateUninst(uninstStream);

                                if (!EnsureCodexCliPlusWebView2RuntimeInstalled())
                                {
                                    Option.Current.Installing = false;
                                    return;
                                }
                    """,
                    StringComparison.Ordinal
                );
            }
        }

        if (!source.Contains(WebView2InstallMethodMarker, StringComparison.Ordinal))
        {
            source = source.Replace(
                "\r\n}\r\n\r\npartial class InstallViewModel",
                WebView2RuntimeInstallSource + "\r\n}\r\n\r\npartial class InstallViewModel",
                StringComparison.Ordinal
            );
            source = source.Replace(
                "\n}\n\npartial class InstallViewModel",
                WebView2RuntimeInstallSource.Replace("\r\n", "\n")
                    + "\n}\n\npartial class InstallViewModel",
                StringComparison.Ordinal
            );
        }

        File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void PatchFinishPageCleanup(string distRoot)
    {
        PatchFinishPageXaml(Path.Combine(distRoot, "Views", "Inst", "FinishPage.xaml"));
        PatchFinishViewModel(Path.Combine(distRoot, "ViewModels", "Inst", "FinishViewModel.cs"));
    }

    private static void PatchFinishPageXaml(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var source = File.ReadAllText(path);
        if (!source.Contains("完成后删除安装包", StringComparison.Ordinal))
        {
            source = source.Replace(
                """
                                <StackPanel Grid.Row="2"
                                            Margin="0,56,0,0"
                                            HorizontalAlignment="Center"
                                            Orientation="Horizontal">
                """,
                """
                                <CheckBox Margin="0,24,0,0"
                                          HorizontalAlignment="Center"
                                          VerticalAlignment="Center"
                                          Content="完成后删除安装包"
                                          Foreground="{DynamicResource TextFillColorPrimaryBrush}"
                                          IsChecked="{Binding CleanupInstallerAfterInstall}" />
                                <StackPanel Grid.Row="2"
                                            Margin="0,24,0,0"
                                            HorizontalAlignment="Center"
                                            Orientation="Horizontal">
                """,
                StringComparison.Ordinal
            );
            if (!source.Contains("完成后删除安装包", StringComparison.Ordinal))
            {
                var marker = "<StackPanel Grid.Row=\"2\"";
                var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
                if (markerIndex >= 0)
                {
                    var insertAt = source.LastIndexOf('\n', markerIndex);
                    insertAt = insertAt < 0 ? markerIndex : insertAt + 1;
                    var indent = source[insertAt..markerIndex];
                    source = source.Insert(insertAt, BuildFinishCleanupCheckboxXaml(indent));
                    source = source.Replace(
                        "Margin=\"0,56,0,0\"",
                        "Margin=\"0,24,0,0\"",
                        StringComparison.Ordinal
                    );
                }
            }
        }

        File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string BuildFinishCleanupCheckboxXaml(string indent)
    {
        return string.Join(
                Environment.NewLine,
                [
                    $"{indent}<CheckBox Margin=\"0,24,0,0\"",
                    $"{indent}          HorizontalAlignment=\"Center\"",
                    $"{indent}          VerticalAlignment=\"Center\"",
                    $"{indent}          Content=\"完成后删除安装包\"",
                    $"{indent}          Foreground=\"{{DynamicResource TextFillColorPrimaryBrush}}\"",
                    $"{indent}          IsChecked=\"{{Binding CleanupInstallerAfterInstall}}\" />",
                ]
            )
            + Environment.NewLine;
    }

    private static void PatchFinishViewModel(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var source = File.ReadAllText(path);
        source = AddUsing(source, "using System.Security.Cryptography;");

        if (!source.Contains("CleanupInstallerAfterInstall", StringComparison.Ordinal))
        {
            source = source.Replace(
                "    public string Message => Option.Current.MessageOfPage3;\r\n",
                "    public string Message => Option.Current.MessageOfPage3;\r\n\r\n    public bool CleanupInstallerAfterInstall { get; set; } = true;\r\n",
                StringComparison.Ordinal
            );
            source = source.Replace(
                "    public string Message => Option.Current.MessageOfPage3;\n",
                "    public string Message => Option.Current.MessageOfPage3;\n\n    public bool CleanupInstallerAfterInstall { get; set; } = true;\n",
                StringComparison.Ordinal
            );
        }

        if (
            !source.Contains(
                "CleanupOriginalInstallerAfterInstall();\r\n            SystemCommands.CloseWindow(window);",
                StringComparison.Ordinal
            )
            && !source.Contains(
                "CleanupOriginalInstallerAfterInstall();\n            SystemCommands.CloseWindow(window);",
                StringComparison.Ordinal
            )
        )
        {
            source = source.Replace(
                "SystemCommands.CloseWindow(window);",
                "CleanupOriginalInstallerAfterInstall();\r\n            SystemCommands.CloseWindow(window);",
                StringComparison.Ordinal
            );
        }

        if (!source.Contains(FinishCleanupMethodMarker, StringComparison.Ordinal))
        {
            source = source.Replace(
                "\r\n}\r\n\r\npartial class FinishViewModel",
                FinishCleanupSource + "\r\n}\r\n\r\npartial class FinishViewModel",
                StringComparison.Ordinal
            );
            source = source.Replace(
                "\n}\n\npartial class FinishViewModel",
                FinishCleanupSource.Replace("\r\n", "\n")
                    + "\n}\n\npartial class FinishViewModel",
                StringComparison.Ordinal
            );
        }

        File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void PatchUninstallCleanup(string distRoot)
    {
        var path = Path.Combine(distRoot, "Helper", "Setup", "UninstallHelper.cs");
        if (!File.Exists(path))
        {
            return;
        }

        var source = File.ReadAllText(path);
        source = source.Replace(
            "using System.IO;",
            "using System.Diagnostics;\r\nusing System.IO;",
            StringComparison.Ordinal
        );
        source = source.Replace(
            "else { // For security reason, uninst should always keep data because of unundering admin. Option.Current.KeepMyData = true; uinfo = PrepareUninstallPathHelper.GetPrepareUninstallPath(); if (string.IsNullOrWhiteSpace(uinfo.UninstallData)) { MessageBox.Info(ApplicationDispatcherHelper.MainWindow, \"InstallationInfoLostHint\".Tr()); } }",
            "else { uinfo = PrepareUninstallPathHelper.GetPrepareUninstallPath(); if (string.IsNullOrWhiteSpace(uinfo.UninstallData)) { MessageBox.Info(ApplicationDispatcherHelper.MainWindow, \"InstallationInfoLostHint\".Tr()); } }",
            StringComparison.Ordinal
        );
        source = source.Replace(
            """
                    else
                    {
                        // For security reason, uninst should always keep data because of unundering admin.
                        Option.Current.KeepMyData = true;

                        uinfo = PrepareUninstallPathHelper.GetPrepareUninstallPath();

                        if (string.IsNullOrWhiteSpace(uinfo.UninstallData))
                        {
                            MessageBox.Info(ApplicationDispatcherHelper.MainWindow, "InstallationInfoLostHint".Tr());
                        }
                    }
            """,
            """
                    else
                    {
                        uinfo = PrepareUninstallPathHelper.GetPrepareUninstallPath();

                        if (string.IsNullOrWhiteSpace(uinfo.UninstallData))
                        {
                            MessageBox.Info(ApplicationDispatcherHelper.MainWindow, "InstallationInfoLostHint".Tr());
                        }
                    }
            """,
            StringComparison.Ordinal
        );
        source = source.Replace(
            "try { RegistyUninstallHelper.Delete(Option.Current.KeyName); }",
            "CleanupCodexCliPlusUserData(); try { RegistyUninstallHelper.Delete(Option.Current.KeyName); }",
            StringComparison.Ordinal
        );
        source = source.Replace(
            """
                    try
                    {
                        RegistyUninstallHelper.Delete(Option.Current.KeyName);
                    }
            """,
            """
                    CleanupCodexCliPlusUserData();

                    try
                    {
                        RegistyUninstallHelper.Delete(Option.Current.KeyName);
                    }
            """,
            StringComparison.Ordinal
        );

        if (!source.Contains(CleanupMethodMarker, StringComparison.Ordinal))
        {
            source = source.Replace(
                "public static void DeleteUninst()",
                UninstallCleanupSource
                    + "\r\n\r\n                public static void DeleteUninst()",
                StringComparison.Ordinal
            );
        }

        File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string EnsureOptionAssignment(
        string source,
        string assignmentTarget,
        string value,
        string insertAfterTarget
    )
    {
        if (source.Contains(assignmentTarget, StringComparison.Ordinal))
        {
            return ReplaceAssignment(source, assignmentTarget, value);
        }

        var insertAfter = source.IndexOf(insertAfterTarget, StringComparison.Ordinal);
        if (insertAfter < 0)
        {
            return source;
        }

        var semicolon = source.IndexOf(';', insertAfter);
        return semicolon < 0
            ? source
            : source.Insert(semicolon + 1, $" {assignmentTarget} = {value};");
    }

    private static string AddUsing(string source, string usingDirective)
    {
        if (source.Contains(usingDirective, StringComparison.Ordinal))
        {
            return source;
        }

        var namespaceIndex = source.IndexOf("namespace ", StringComparison.Ordinal);
        if (namespaceIndex < 0)
        {
            return usingDirective + Environment.NewLine + source;
        }

        var insertAt = source.LastIndexOf("using ", namespaceIndex, StringComparison.Ordinal);
        if (insertAt < 0)
        {
            return source.Insert(namespaceIndex, usingDirective + Environment.NewLine);
        }

        var lineEnd = source.IndexOf('\n', insertAt);
        while (lineEnd >= 0 && lineEnd < namespaceIndex)
        {
            var nextUsing = source.IndexOf("using ", lineEnd + 1, StringComparison.Ordinal);
            if (nextUsing < 0 || nextUsing >= namespaceIndex)
            {
                break;
            }

            lineEnd = source.IndexOf('\n', nextUsing);
        }

        return lineEnd < 0
            ? source + Environment.NewLine + usingDirective
            : source.Insert(lineEnd + 1, usingDirective + Environment.NewLine);
    }

    private static string ReplaceBetween(string source, string prefix, string suffix, string value)
    {
        var start = source.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return source;
        }

        start += prefix.Length;
        var end = source.IndexOf(suffix, start, StringComparison.Ordinal);
        return end < 0 ? source : source[..start] + value + source[end..];
    }

    private static string ReplaceAssignment(string source, string assignmentTarget, string value)
    {
        var start = source.IndexOf(assignmentTarget, StringComparison.Ordinal);
        if (start < 0)
        {
            return source;
        }

        var equals = source.IndexOf('=', start);
        var semicolon = source.IndexOf(';', equals);
        if (equals < 0 || semicolon < 0)
        {
            return source;
        }

        return source[..(equals + 1)] + " " + value + source[semicolon..];
    }

    private static string NormalizeAssemblyVersion(string version)
    {
        var parts = version
            .Split(['.', '-', '+'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var value) ? value : 0)
            .Take(4)
            .ToList();
        while (parts.Count < 4)
        {
            parts.Add(0);
        }

        return string.Join('.', parts);
    }
}
