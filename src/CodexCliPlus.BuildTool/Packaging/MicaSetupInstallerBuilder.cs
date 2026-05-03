using System.Text;

namespace CodexCliPlus.BuildTool;

public static class MicaSetupInstallerBuilder
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly (string Template, string Target)[] SourceTemplateFiles =
    [
        ("Program.cs.template", "Program.cs"),
        ("Program.un.cs.template", "Program.un.cs"),
        (
            Path.Combine("ViewModels", "Inst", "MainViewModel.cs.template"),
            Path.Combine("ViewModels", "Inst", "MainViewModel.cs")
        ),
        (
            Path.Combine("ViewModels", "Inst", "InstallViewModel.cs.template"),
            Path.Combine("ViewModels", "Inst", "InstallViewModel.cs")
        ),
        (
            Path.Combine("ViewModels", "Inst", "FinishViewModel.cs.template"),
            Path.Combine("ViewModels", "Inst", "FinishViewModel.cs")
        ),
        (
            Path.Combine("ViewModels", "Uninst", "MainViewModel.cs.template"),
            Path.Combine("ViewModels", "Uninst", "MainViewModel.cs")
        ),
        (
            Path.Combine("Views", "Inst", "FinishPage.xaml.template"),
            Path.Combine("Views", "Inst", "FinishPage.xaml")
        ),
        (
            Path.Combine("Helper", "Setup", "ArchiveFileHelper.cs.template"),
            Path.Combine("Helper", "Setup", "ArchiveFileHelper.cs")
        ),
        (
            Path.Combine("Helper", "Setup", "UninstallHelper.cs.template"),
            Path.Combine("Helper", "Setup", "UninstallHelper.cs")
        ),
    ];

    public static async Task<int> BuildAsync(
        BuildContext context,
        string micaConfigPath,
        string? payloadArchivePath,
        long payloadUncompressedBytes,
        string installerOutputPath,
        InstallerPackageKind packageKind,
        OnlineInstallerPayload? onlinePayload
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(installerOutputPath)!);
        if (File.Exists(installerOutputPath))
        {
            File.Delete(installerOutputPath);
        }

        context.Logger.Info(
            "Using repo-owned MicaSetup source templates so installer dependency repair and cleanup logic are reviewable."
        );
        return await BuildWithDotnetMsbuildAsync(
            context,
            micaConfigPath,
            payloadArchivePath,
            payloadUncompressedBytes,
            installerOutputPath,
            packageKind,
            onlinePayload
        );
    }

    private static async Task<int> BuildWithDotnetMsbuildAsync(
        BuildContext context,
        string micaConfigPath,
        string? payloadArchivePath,
        long payloadUncompressedBytes,
        string installerOutputPath,
        InstallerPackageKind packageKind,
        OnlineInstallerPayload? onlinePayload
    )
    {
        var stageRoot = Path.GetDirectoryName(micaConfigPath)!;
        var distRoot = Path.Combine(stageRoot, ".dist");
        SafeFileSystem.CleanDirectory(distRoot, context.Options.OutputRoot);

        var sourceTemplateRoot = Path.Combine(
            context.Options.RepositoryRoot,
            "build",
            "micasetup",
            "source-template"
        );
        if (!Directory.Exists(sourceTemplateRoot))
        {
            context.Logger.Error($"MicaSetup source template missing: {sourceTemplateRoot}");
            return 1;
        }

        SafeFileSystem.CopyDirectory(sourceTemplateRoot, distRoot, ["bin", "obj"]);

        try
        {
            ApplyRepoOwnedInstallerSource(
                context,
                distRoot,
                stageRoot,
                payloadArchivePath,
                payloadUncompressedBytes,
                packageKind,
                onlinePayload
            );
        }
        catch (Exception exception)
        {
            context.Logger.Error(
                $"MicaSetup repo-owned source template rendering failed: {exception.Message}"
            );
            return 1;
        }

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
                "/p:RestoreLockedMode=true",
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
                "/p:RestoreLockedMode=true",
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

        context.Logger.Info("MicaSetup installer generated by dotnet msbuild source route");
        return 0;
    }

    private static void ApplyRepoOwnedInstallerSource(
        BuildContext context,
        string distRoot,
        string stageRoot,
        string? payloadArchivePath,
        long payloadUncompressedBytes,
        InstallerPackageKind packageKind,
        OnlineInstallerPayload? onlinePayload
    )
    {
        if (packageKind == InstallerPackageKind.Offline)
        {
            if (string.IsNullOrWhiteSpace(payloadArchivePath))
            {
                throw new InvalidOperationException("Offline installer payload archive is missing.");
            }

            var setupResourcePath = Path.Combine(distRoot, "Resources", "Setups", "publish.7z");
            Directory.CreateDirectory(Path.GetDirectoryName(setupResourcePath)!);
            File.Copy(payloadArchivePath, setupResourcePath, overwrite: true);
        }
        else if (onlinePayload is null)
        {
            throw new InvalidOperationException("Online installer payload metadata is missing.");
        }

        var cleanupManifestSource = Path.Combine(stageRoot, "uninstall-cleanup.json");
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
        RenderRepoOwnedSourceTemplates(
            context,
            distRoot,
            payloadUncompressedBytes,
            packageKind,
            onlinePayload
        );
    }

    private static void RenderRepoOwnedSourceTemplates(
        BuildContext context,
        string distRoot,
        long payloadUncompressedBytes,
        InstallerPackageKind packageKind,
        OnlineInstallerPayload? onlinePayload
    )
    {
        var overlayRoot = Path.Combine(
            context.Options.RepositoryRoot,
            "build",
            "micasetup",
            "overrides",
            "MicaSetup"
        );
        if (!Directory.Exists(overlayRoot))
        {
            throw new DirectoryNotFoundException(overlayRoot);
        }

        var tokens = CreateTemplateTokens(
            context,
            payloadUncompressedBytes,
            packageKind,
            onlinePayload
        );
        foreach (var (templateRelativePath, targetRelativePath) in SourceTemplateFiles)
        {
            var templatePath = Path.Combine(overlayRoot, templateRelativePath);
            if (!File.Exists(templatePath))
            {
                throw new FileNotFoundException(
                    $"Missing repo-owned MicaSetup source template: {templatePath}",
                    templatePath
                );
            }

            var targetPath = Path.Combine(distRoot, targetRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var rendered = RenderTemplate(File.ReadAllText(templatePath), tokens);
            File.WriteAllText(targetPath, rendered, Utf8NoBom);
        }
    }

    private static Dictionary<string, string> CreateTemplateTokens(
        BuildContext context,
        long payloadUncompressedBytes,
        InstallerPackageKind packageKind,
        OnlineInstallerPayload? onlinePayload
    )
    {
        var noticePath = Path.Combine(
            context.Options.RepositoryRoot,
            "resources",
            "licenses",
            "NOTICE.txt"
        );

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["__PRODUCT_GUID__"] = "6f8dd8b7-21ea-4c6b-9695-40a27874ce4d",
            ["__PUBLISHER__"] = "Blackblock Inc.",
            ["__ASSEMBLY_VERSION__"] = NormalizeAssemblyVersion(context.Options.Version),
            ["__DISPLAY_VERSION__"] = context.Options.Version,
            ["__AUTO_RUN_LAUNCH_COMMAND__"] = "/autostart",
            ["__IS_USE_LICENSE_FILE__"] = File.Exists(noticePath) ? "true" : "false",
            ["__PAYLOAD_UNCOMPRESSED_BYTES__"] = payloadUncompressedBytes.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            ["__IS_ONLINE_PAYLOAD_MODE__"] =
                packageKind == InstallerPackageKind.Online ? "true" : "false",
            ["__ONLINE_PAYLOAD_FILE_NAME__"] = ToCSharpString(onlinePayload?.FileName ?? string.Empty),
            ["__ONLINE_PAYLOAD_URL__"] = ToCSharpString(onlinePayload?.Url ?? string.Empty),
            ["__ONLINE_PAYLOAD_SHA256__"] = ToCSharpString(onlinePayload?.Sha256 ?? string.Empty),
            ["__ONLINE_PAYLOAD_SIZE_BYTES__"] = (onlinePayload?.Size ?? 0).ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            ["__WEBVIEW2_BOOTSTRAPPER_FILE_NAME__"] = ToCSharpString(
                WebView2RuntimeAssets.BootstrapperFileName
            ),
            ["__WEBVIEW2_BOOTSTRAPPER_URL__"] = ToCSharpString(WebView2RuntimeAssets.BootstrapperUrl),
            ["__WEBVIEW2_STANDALONE_FILE_NAME__"] = ToCSharpString(
                WebView2RuntimeAssets.StandaloneX64FileName
            ),
        };
    }

    private static string ToCSharpString(string value)
    {
        return "\""
            + value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
            + "\"";
    }

    private static string RenderTemplate(string source, Dictionary<string, string> tokens)
    {
        foreach (var (token, value) in tokens)
        {
            source = source.Replace(token, value, StringComparison.Ordinal);
        }

        return source;
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
            (Path.Combine(repositoryRoot, "resources", "licenses", "NOTICE.txt"), "license.txt"),
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
