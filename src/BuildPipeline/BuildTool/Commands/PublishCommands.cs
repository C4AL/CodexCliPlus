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

public static class PublishCommands
{
    public static async Task<int> PublishAsync(BuildContext context)
    {
        return await BuildTiming.TimeAsync(context, "publish", () => PublishCoreAsync(context));
    }

    private static async Task<int> PublishCoreAsync(BuildContext context)
    {
        var verifyCode = await AssetCommands.VerifyAssetsAsync(context);
        if (verifyCode != 0)
        {
            return verifyCode;
        }

        var webUiBuildCode = await WebUiCommands.BuildAsync(context);
        if (webUiBuildCode != 0)
        {
            return webUiBuildCode;
        }

        var inputHash = await ComputeInputHashAsync(context);
        if (
            context.Options.Incremental
            && !context.Options.ForceRebuild.Includes(ForceRebuildStage.Publish)
        )
        {
            var cache = await IncrementalBuildCache.LookupDirectoryAsync(
                context,
                "publish",
                inputHash,
                context.PublishRoot
            );
            context.Logger.Info(cache.Reason);
            if (cache.Hit)
            {
                SafeFileSystem.RequirePublishRoot(context.PublishRoot);
                DeletePublishLegacySidecars(context);
                return 0;
            }
        }
        else if (!context.Options.Incremental)
        {
            context.Logger.Info("publish incremental cache disabled");
        }
        else
        {
            context.Logger.Info("publish force rebuild requested");
        }

        SafeFileSystem.CleanDirectory(context.PublishRoot, context.Options.OutputRoot);

        var appProject = Path.Combine(
            context.Options.RepositoryRoot,
            "src", "DesktopShell", "App",
            "CodexCliPlus.App.csproj"
        );
        var arguments = new[]
        {
            "publish",
            appProject,
            "--configuration",
            context.Options.Configuration,
            "--no-restore",
            "--runtime",
            context.Options.Runtime,
            "--self-contained",
            "true",
            "--output",
            context.PublishRoot,
            "/p:PublishSingleFile=false",
            "/p:SatelliteResourceLanguages=zh-Hans",
            "/p:DebugType=None",
            "/p:DebugSymbols=false",
            "/p:GenerateDocumentationFile=false",
        };
        var exitCode = await context.ProcessRunner.RunAsync(
            "dotnet",
            arguments,
            context.Options.RepositoryRoot,
            context.Logger
        );
        if (exitCode != 0)
        {
            context.Logger.Error($"dotnet publish failed with exit code {exitCode}.");
            return exitCode;
        }

        SafeFileSystem.CopyDirectory(
            Path.Combine(context.AssetsRoot, "backend"),
            Path.Combine(context.PublishRoot, "assets", "backend")
        );
        SafeFileSystem.CopyDirectory(
            Path.Combine(context.AssetsRoot, "local-environment"),
            Path.Combine(context.PublishRoot, "assets", "local-environment")
        );
        SafeFileSystem.CopyDirectory(
            context.WebUiAssetsRoot,
            Path.Combine(context.PublishRoot, "assets", "webui")
        );
        var updaterExitCode = await PublishUpdaterAsync(context);
        if (updaterExitCode != 0)
        {
            return updaterExitCode;
        }

        DeletePublishLegacySidecars(context);
        CopyLicenseDocuments(context);
        await WritePublishManifestAsync(context);
        if (context.Options.Incremental)
        {
            await IncrementalBuildCache.WriteDirectoryAsync(
                context,
                "publish",
                inputHash,
                context.PublishRoot
            );
        }

        context.Logger.Info($"publish output: {context.PublishRoot}");
        return 0;
    }

    private static async Task<string> ComputeInputHashAsync(BuildContext context)
    {
        var hasher = new IncrementalInputHasher();
        hasher.AddText("stage", "publish");
        hasher.AddText("version", context.Options.Version);
        hasher.AddText("runtime", context.Options.Runtime);
        hasher.AddText("configuration", context.Options.Configuration);
        hasher.AddText("local-environment-assets-in-publish", "1");
        foreach (
            var (inputName, projectDirectory) in new[]
            {
                (
                    "src/DesktopShell/App",
                    Path.Combine(context.Options.RepositoryRoot, "src", "DesktopShell", "App")
                ),
                (
                    "src/DesktopShell/Core",
                    Path.Combine(context.Options.RepositoryRoot, "src", "DesktopShell", "Core")
                ),
                (
                    "src/DesktopShell/Infrastructure",
                    Path.Combine(
                        context.Options.RepositoryRoot,
                        "src", "DesktopShell", "Infrastructure"
                    )
                ),
                (
                    "src/DesktopShell/Updater",
                    Path.Combine(
                        context.Options.RepositoryRoot,
                        "src", "DesktopShell", "Updater"
                    )
                ),
            }
        )
        {
            await hasher.AddDirectoryAsync(inputName, projectDirectory, ["bin", "obj"]);
        }
        await hasher.AddFileAsync(
            "directory-build-props",
            Path.Combine(context.Options.RepositoryRoot, "Directory.Build.props")
        );
        await hasher.AddFileAsync(
            "global-json",
            Path.Combine(context.Options.RepositoryRoot, "global.json")
        );
        await hasher.AddDirectoryAsync("assets", context.AssetsRoot);
        return hasher.Finish();
    }

    private static async Task<int> PublishUpdaterAsync(BuildContext context)
    {
        var updaterProject = Path.Combine(
            context.Options.RepositoryRoot,
            "src", "DesktopShell", "Updater",
            "CodexCliPlus.Updater.csproj"
        );
        var updaterOutput = Path.Combine(context.PublishRoot, "updater");
        var exitCode = await context.ProcessRunner.RunAsync(
            "dotnet",
            [
                "publish",
                updaterProject,
                "--configuration",
                context.Options.Configuration,
                "--no-restore",
                "--output",
                updaterOutput,
                "/p:PublishSingleFile=false",
                "/p:SatelliteResourceLanguages=zh-Hans",
                "/p:DebugType=None",
                "/p:DebugSymbols=false",
                "/p:GenerateDocumentationFile=false",
            ],
            context.Options.RepositoryRoot,
            context.Logger
        );
        if (exitCode != 0)
        {
            context.Logger.Error($"dotnet publish updater failed with exit code {exitCode}.");
            return exitCode;
        }

        ArtifactSidecarCleanup.DeleteLegacySidecars(
            Path.Combine(updaterOutput, "CodexCliPlus.Updater.exe")
        );
        return 0;
    }

    private static void DeletePublishLegacySidecars(BuildContext context)
    {
        ArtifactSidecarCleanup.DeleteLegacySidecars(
            Path.Combine(context.PublishRoot, AppConstants.ExecutableName)
        );
        ArtifactSidecarCleanup.DeleteLegacySidecars(
            Path.Combine(context.PublishRoot, "updater", "CodexCliPlus.Updater.exe")
        );
    }

    private static void CopyLicenseDocuments(BuildContext context)
    {
        var targetDirectory = Path.Combine(context.PublishRoot, "Licenses");
        Directory.CreateDirectory(targetDirectory);
        foreach (
            var (source, target) in new[]
            {
                (context.RepositoryLicensePath, "CodexCliPlus.LICENSE.txt"),
                (
                    Path.Combine(context.RepositoryLicensesRoot, "CLIProxyAPI.MIT.txt"),
                    "CLIProxyAPI.LICENSE.txt"
                ),
                (
                    Path.Combine(
                        context.RepositoryLicensesRoot,
                        "CliProxyApiManagementCenter.MIT.txt"
                    ),
                    "CliProxyApiManagementCenter.LICENSE.txt"
                ),
                (
                    Path.Combine(context.RepositoryLicensesRoot, "BetterGI.GPL-3.0.txt"),
                    "BetterGI.GPL-3.0.txt"
                ),
                (
                    Path.Combine(context.RepositoryLicensesRoot, "cpa-usage-keeper.MIT.txt"),
                    "cpa-usage-keeper.MIT.txt"
                ),
                (Path.Combine(context.RepositoryLicensesRoot, "NOTICE.txt"), "NOTICE.txt"),
            }
        )
        {
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("License document missing.", source);
            }

            File.Copy(source, Path.Combine(targetDirectory, target), overwrite: true);
        }
    }

    private static async Task WritePublishManifestAsync(BuildContext context)
    {
        var manifest = new PublishManifest
        {
            Product = AppConstants.ProductName,
            Version = context.Options.Version,
            Runtime = context.Options.Runtime,
            Configuration = context.Options.Configuration,
            Application = AppConstants.ExecutableName,
            AssetsManifest = Path.GetRelativePath(context.PublishRoot, context.AssetManifestPath),
        };
        await File.WriteAllTextAsync(
            Path.Combine(context.PublishRoot, "publish-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
    }
}
