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
        var verifyCode = await AssetCommands.VerifyAssetsAsync(context);
        if (verifyCode != 0)
        {
            return verifyCode;
        }

        var webUiBuildCode = await WebUiCommands.BuildVendoredAsync(context);
        if (webUiBuildCode != 0)
        {
            return webUiBuildCode;
        }

        SafeFileSystem.CleanDirectory(context.PublishRoot, context.Options.OutputRoot);

        var appProject = Path.Combine(
            context.Options.RepositoryRoot,
            "src",
            "CodexCliPlus.App",
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
            context.WebUiAssetsRoot,
            Path.Combine(context.PublishRoot, "assets", "webui")
        );
        var updaterExitCode = await PublishUpdaterAsync(context);
        if (updaterExitCode != 0)
        {
            return updaterExitCode;
        }

        await context.SigningService.SignAsync(
            Path.Combine(context.PublishRoot, AppConstants.ExecutableName),
            context
        );
        CopyBackendLicenseDocument(context);
        await WritePublishManifestAsync(context);
        context.Logger.Info($"publish output: {context.PublishRoot}");
        return 0;
    }

    private static async Task<int> PublishUpdaterAsync(BuildContext context)
    {
        var updaterProject = Path.Combine(
            context.Options.RepositoryRoot,
            "src",
            "CodexCliPlus.Updater",
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
                "--runtime",
                context.Options.Runtime,
                "--self-contained",
                "true",
                "--output",
                updaterOutput,
                "/p:PublishSingleFile=false",
            ],
            context.Options.RepositoryRoot,
            context.Logger
        );
        if (exitCode != 0)
        {
            context.Logger.Error($"dotnet publish updater failed with exit code {exitCode}.");
            return exitCode;
        }

        await context.SigningService.SignAsync(
            Path.Combine(updaterOutput, "CodexCliPlus.Updater.exe"),
            context
        );
        return 0;
    }

    private static void CopyBackendLicenseDocument(BuildContext context)
    {
        var source = Path.Combine(context.AssetsRoot, "backend", "windows-x64", "LICENSE");
        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                "Backend license missing from verified assets.",
                source
            );
        }

        var targetDirectory = Path.Combine(context.PublishRoot, "Licenses");
        Directory.CreateDirectory(targetDirectory);
        File.Copy(
            source,
            Path.Combine(targetDirectory, "CLIProxyAPI.LICENSE.txt"),
            overwrite: true
        );
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
