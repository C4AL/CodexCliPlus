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

        if (!HasVisualStudioInstaller())
        {
            context.Logger.Info(
                "Visual Studio Installer not detected; using dotnet msbuild against the official MicaSetup template."
            );
            return await BuildWithDotnetMsbuildAsync(
                context,
                micaConfigPath,
                payloadArchivePath,
                installerOutputPath
            );
        }

        var makeMicaExitCode = await context.ProcessRunner.RunAsync(
            toolchain.MakeMicaPath,
            [micaConfigPath],
            Path.GetDirectoryName(micaConfigPath)!,
            context.Logger
        );
        if (makeMicaExitCode == 0 && File.Exists(installerOutputPath))
        {
            var validationFailure = WindowsExecutableValidation.ValidateFile(installerOutputPath);
            if (validationFailure is null)
            {
                context.Logger.Info("MicaSetup installer generated by makemica.exe");
                return 0;
            }

            context.Logger.Error(
                $"makemica.exe produced an invalid installer: {validationFailure}"
            );
            File.Delete(installerOutputPath);
        }

        context.Logger.Info(
            makeMicaExitCode == 0
                ? "makemica.exe completed without a valid installer executable; falling back to dotnet msbuild against the official MicaSetup template."
                : $"makemica.exe failed with exit code {makeMicaExitCode}; falling back to dotnet msbuild against the official MicaSetup template."
        );
        return await BuildWithDotnetMsbuildAsync(
            context,
            micaConfigPath,
            payloadArchivePath,
            installerOutputPath
        );
    }

    private static bool HasVisualStudioInstaller()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        foreach (var programFilesRoot in GetProgramFilesRoots())
        {
            var installerRoot = Path.Combine(
                programFilesRoot,
                "Microsoft Visual Studio",
                "Installer"
            );
            if (
                File.Exists(Path.Combine(installerRoot, "setup.exe"))
                || File.Exists(Path.Combine(installerRoot, "vswhere.exe"))
            )
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetProgramFilesRoots()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return programFilesX86;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (
            !string.IsNullOrWhiteSpace(programFiles)
            && !string.Equals(programFiles, programFilesX86, StringComparison.OrdinalIgnoreCase)
        )
        {
            yield return programFiles;
        }
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

        var setupProgram = Path.Combine(distRoot, "Program.cs");
        var uninstProgram = Path.Combine(distRoot, "Program.un.cs");
        PatchProgramSource(setupProgram, context.Options.Version, isUninstaller: false);
        PatchProgramSource(uninstProgram, context.Options.Version, isUninstaller: true);
        PatchForCurrentUserInstall(distRoot);
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
