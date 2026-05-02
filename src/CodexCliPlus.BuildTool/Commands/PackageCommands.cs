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

public static class PackageCommands
{
    public static async Task<int> PackageInstallerAsync(
        BuildContext context,
        InstallerPackageKind packageKind
    )
    {
        SafeFileSystem.RequirePublishRoot(context.PublishRoot);
        var packageMoniker = packageKind == InstallerPackageKind.Online ? "Online" : "Offline";
        var packageType =
            packageKind == InstallerPackageKind.Online ? "online-installer" : "offline-installer";
        var stageRoot = Path.Combine(context.InstallerRoot, packageType, "stage");
        var appPackageRoot = Path.Combine(stageRoot, "app-package");
        var payloadArchivePath = Path.Combine(stageRoot, "publish.7z");
        var installerOutputPath = Path.Combine(
            context.PackageRoot,
            $"{AppConstants.InstallerNamePrefix}.{packageMoniker}.{context.Options.Version}.exe"
        );

        SafeFileSystem.CleanDirectory(stageRoot, context.Options.OutputRoot);
        SafeFileSystem.CopyDirectory(context.PublishRoot, appPackageRoot);
        var webView2Assets = await WebView2RuntimeAssets.StageAsync(
            context,
            appPackageRoot,
            packageKind
        );

        var installerPlan = new InstallerPlan
        {
            ProductName = AppConstants.ProductName,
            InstallerName = Path.GetFileName(installerOutputPath),
            AppUserModelId = AppConstants.AppUserModelId,
            CurrentUserDefault = false,
            PayloadDirectory = "app-package",
            MicaSetupRoute = true,
            RequestExecutionLevel = "admin",
            InstallDirectoryHint = $"%ProgramFiles%\\{AppConstants.ProductKey}",
            LaunchAfterInstall = true,
            CleanupInstallerAfterInstallDefault = true,
            StableReleaseSource = "https://github.com/C4AL/CodexCliPlus/releases/latest",
            BetaChannelReserved = true,
        };
        await WriteJsonAsync(Path.Combine(stageRoot, "mica-setup.json"), installerPlan);
        await InstallerMetadata.WriteAsync(
            context,
            appPackageRoot,
            stageRoot,
            webView2Assets,
            packageKind
        );
        var payloadUncompressedBytes = GetDirectorySize(appPackageRoot);

        var archiveExitCode = await CreateMicaPayloadArchiveAsync(
            context,
            appPackageRoot,
            payloadArchivePath
        );
        if (archiveExitCode != 0)
        {
            return archiveExitCode;
        }

        var micaConfigPath = Path.Combine(stageRoot, "micasetup.json");
        var micaConfig = MicaSetupConfig.Create(context, payloadArchivePath, installerOutputPath);
        await File.WriteAllTextAsync(
            micaConfigPath,
            JsonSerializer.Serialize(micaConfig, MicaSetupConfig.JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        var buildExitCode = await MicaSetupInstallerBuilder.BuildAsync(
            context,
            micaConfigPath,
            payloadArchivePath,
            payloadUncompressedBytes,
            installerOutputPath
        );
        if (buildExitCode != 0)
        {
            return buildExitCode;
        }

        await context.SigningService.SignAsync(installerOutputPath, context);
        Directory.CreateDirectory(Path.Combine(stageRoot, "output"));
        File.Copy(
            installerOutputPath,
            Path.Combine(stageRoot, "output", Path.GetFileName(installerOutputPath)),
            overwrite: true
        );
        CopySigningMetadataIfExists(
            installerOutputPath,
            Path.Combine(stageRoot, "output", Path.GetFileName(installerOutputPath))
        );

        var stagingPackagePath = Path.Combine(
            context.PackageRoot,
            $"{AppConstants.InstallerNamePrefix}.{packageMoniker}.{context.Options.Version}.{context.Options.Runtime}.zip"
        );
        await CreatePackageAsync(context, stageRoot, stagingPackagePath, packageType);
        CleanupPackageStaging(context, stageRoot);
        context.Logger.Info($"installer executable: {installerOutputPath}");
        return 0;
    }

    public static Task<int> VerifyPackagesAsync(BuildContext context)
    {
        var verifier = new PackageVerifier(context);
        var failures = verifier.VerifyAll();
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                context.Logger.Error(failure);
            }

            return Task.FromResult(1);
        }

        context.Logger.Info("package verification passed");
        return Task.FromResult(0);
    }

    public static async Task<int> PackageUpdateAsync(BuildContext context)
    {
        SafeFileSystem.RequirePublishRoot(context.PublishRoot);

        var stageRoot = Path.Combine(context.InstallerRoot, "update-package", "stage");
        var payloadRoot = Path.Combine(stageRoot, "payload");
        SafeFileSystem.CleanDirectory(stageRoot, context.Options.OutputRoot);
        SafeFileSystem.CopyDirectory(context.PublishRoot, payloadRoot);

        var files = Directory
            .EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderBy(
                file => Path.GetRelativePath(payloadRoot, file.FullName),
                StringComparer.OrdinalIgnoreCase
            )
            .ToArray();
        var entries = new List<object>();
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(payloadRoot, file.FullName).Replace('\\', '/');
            entries.Add(
                new
                {
                    path = relativePath,
                    size = file.Length,
                    sha256 = await ComputeSha256Async(file.FullName),
                }
            );
        }

        var manifest = new
        {
            product = AppConstants.ProductName,
            version = context.Options.Version,
            runtime = context.Options.Runtime,
            createdAtUtc = DateTimeOffset.UtcNow,
            updateKind = "file-manifest-diff",
            signing = SigningOptions.FromEnvironment().SigningRequired
                ? "required"
                : "optional-unsigned",
            files = entries,
        };

        await File.WriteAllTextAsync(
            Path.Combine(stageRoot, "update-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        Directory.CreateDirectory(context.PackageRoot);
        var packagePath = Path.Combine(
            context.PackageRoot,
            $"CodexCliPlus.Update.{context.Options.Version}.{context.Options.Runtime}.zip"
        );
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        ZipFile.CreateFromDirectory(
            stageRoot,
            packagePath,
            CompressionLevel.Optimal,
            includeBaseDirectory: false
        );
        await context.SigningService.SignAsync(packagePath, context);
        CleanupPackageStaging(context, stageRoot);
        context.Logger.Info($"update package: {packagePath}");
        return 0;
    }

    private static void CleanupPackageStaging(BuildContext context, string stageRoot)
    {
        if (context.Options.KeepPackageStaging)
        {
            context.Logger.Info($"kept package staging: {stageRoot}");
            return;
        }

        try
        {
            SafeFileSystem.DeleteDirectory(stageRoot, context.Options.OutputRoot);
            context.Logger.Info($"removed package staging: {stageRoot}");
        }
        catch (IOException exception)
        {
            context.Logger.Warning(
                $"Could not remove package staging because it is in use: {exception.Message}"
            );
        }
        catch (UnauthorizedAccessException exception)
        {
            context.Logger.Warning(
                $"Could not remove package staging because access was denied: {exception.Message}"
            );
        }
    }

    private static async Task CreatePackageAsync(
        BuildContext context,
        string stageRoot,
        string packagePath,
        string packageType
    )
    {
        Directory.CreateDirectory(context.PackageRoot);
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        var manifest = new PackageManifest
        {
            Product = AppConstants.ProductName,
            Version = context.Options.Version,
            Runtime = context.Options.Runtime,
            PackageType = packageType,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await File.WriteAllTextAsync(
            Path.Combine(stageRoot, "package-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        ZipFile.CreateFromDirectory(
            stageRoot,
            packagePath,
            CompressionLevel.Optimal,
            includeBaseDirectory: false
        );
        await context.SigningService.SignAsync(packagePath, context);
        context.Logger.Info($"{packageType} package: {packagePath}");
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static void CopySigningMetadataIfExists(
        string sourceArtifactPath,
        string targetArtifactPath
    )
    {
        foreach (
            var metadataPath in new[]
            {
                ArtifactSignatureMetadata.GetSignaturePath(sourceArtifactPath),
                ArtifactSignatureMetadata.GetUnsignedPath(sourceArtifactPath),
            }
        )
        {
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            var targetMetadataPath = metadataPath.EndsWith(
                ".signature.json",
                StringComparison.OrdinalIgnoreCase
            )
                ? ArtifactSignatureMetadata.GetSignaturePath(targetArtifactPath)
                : ArtifactSignatureMetadata.GetUnsignedPath(targetArtifactPath);
            File.Copy(metadataPath, targetMetadataPath, overwrite: true);
        }
    }

    private static Task<int> CreateMicaPayloadArchiveAsync(
        BuildContext context,
        string appPackageRoot,
        string archivePath
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        ZipFile.CreateFromDirectory(
            appPackageRoot,
            archivePath,
            CompressionLevel.Optimal,
            includeBaseDirectory: false
        );
        context.Logger.Info("MicaSetup payload archive created from repo package contents.");
        return Task.FromResult(File.Exists(archivePath) ? 0 : 1);
    }

    private static long GetDirectorySize(string directory)
    {
        return Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
    }

    private static Task WriteJsonAsync(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(value, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
    }
}

public enum InstallerPackageKind
{
    Online,
    Offline,
}
