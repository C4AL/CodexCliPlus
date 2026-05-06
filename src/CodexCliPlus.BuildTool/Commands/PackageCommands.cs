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
    public static async Task<int> BuildReleaseAsync(BuildContext context)
    {
        context.Logger.Info(
            $"selected release package(s): {context.Options.ReleasePackages.ToDisplayString()}"
        );

        var publishExitCode = await PublishCommands.PublishAsync(context);
        if (publishExitCode != 0)
        {
            return publishExitCode;
        }

        if (
            context.Options.ReleasePackages.IncludesUpdatePackage()
            || context.Options.ReleasePackages.IncludesOnlineInstaller()
        )
        {
            var updateExitCode = await PackageUpdateAsync(context);
            if (updateExitCode != 0)
            {
                return updateExitCode;
            }
        }

        if (context.Options.ReleasePackages.IncludesOnlineInstaller())
        {
            var onlineInstallerExitCode = await PackageInstallerAsync(
                context,
                InstallerPackageKind.Online
            );
            if (onlineInstallerExitCode != 0)
            {
                return onlineInstallerExitCode;
            }
        }

        if (context.Options.ReleasePackages.IncludesOfflineInstaller())
        {
            var offlineInstallerExitCode = await PackageInstallerAsync(
                context,
                InstallerPackageKind.Offline
            );
            if (offlineInstallerExitCode != 0)
            {
                return offlineInstallerExitCode;
            }
        }

        return await VerifyPackagesAsync(context);
    }

    public static async Task<int> PackageInstallerAsync(
        BuildContext context,
        InstallerPackageKind packageKind
    )
    {
        var packageType =
            packageKind == InstallerPackageKind.Online ? "online-installer" : "offline-installer";
        return await BuildTiming.TimeAsync(
            context,
            packageType,
            () => PackageInstallerCoreAsync(context, packageKind)
        );
    }

    private static async Task<int> PackageInstallerCoreAsync(
        BuildContext context,
        InstallerPackageKind packageKind
    )
    {
        var packageMoniker = packageKind == InstallerPackageKind.Online ? "Online" : "Offline";
        var packageType =
            packageKind == InstallerPackageKind.Online ? "online-installer" : "offline-installer";
        var isOnline = packageKind == InstallerPackageKind.Online;
        var stageRoot = Path.Combine(context.InstallerRoot, packageType, "stage");
        var appPackageRoot = isOnline ? null : Path.Combine(stageRoot, "app-package");
        var payloadArchivePath = isOnline ? null : Path.Combine(stageRoot, "publish.7z");
        var installerOutputPath = Path.Combine(
            context.PackageRoot,
            $"{AppConstants.InstallerNamePrefix}.{packageMoniker}.{context.Options.Version}.exe"
        );

        OnlineInstallerPayload? onlinePayload = null;
        if (isOnline)
        {
            onlinePayload = await CreateOnlineInstallerPayloadAsync(context);
        }
        else
        {
            SafeFileSystem.RequirePublishRoot(context.PublishRoot);
        }

        var installerInputHash = await ComputeInstallerInputHashAsync(
            context,
            packageKind,
            onlinePayload
        );
        if (
            context.Options.Incremental
            && !context.Options.ForceRebuild.Includes(ForceRebuildStage.Installer)
        )
        {
            var cache = await IncrementalBuildCache.LookupFileAsync(
                context,
                packageType,
                installerInputHash,
                installerOutputPath
            );
            var cacheHit = cache.Hit && HasSigningMetadata(installerOutputPath);
            context.Logger.Info(
                cacheHit
                    ? $"{packageType} cache hit"
                    : cache.Hit
                        ? $"{packageType} signature metadata missing"
                        : cache.Reason
            );
            if (cacheHit)
            {
                return 0;
            }
        }
        else if (!context.Options.Incremental)
        {
            context.Logger.Info($"{packageType} incremental cache disabled");
        }
        else
        {
            context.Logger.Info($"{packageType} force rebuild requested");
        }

        CleanInstallerStage(context, stageRoot);
        long payloadUncompressedBytes;
        WebView2RuntimeAssets webView2Assets;
        if (isOnline)
        {
            await WriteJsonAsync(Path.Combine(stageRoot, "online-payload.json"), onlinePayload!);
            payloadUncompressedBytes = GetUpdatePackagePayloadSize(context, onlinePayload!);
            webView2Assets = await WebView2RuntimeAssets.StageAsync(
                context,
                stageRoot,
                packageKind
            );
        }
        else
        {
            var materialized = FileMaterializer.MaterializeDirectory(
                context.PublishRoot,
                appPackageRoot!,
                preferHardLinks: true
            );
            context.Logger.Info(
                $"offline staging materialized: {materialized.LinkedFiles} hardlink(s), {materialized.CopiedFiles} copy fallback(s)"
            );
            webView2Assets = await WebView2RuntimeAssets.StageAsync(
                context,
                appPackageRoot!,
                packageKind
            );
            installerInputHash = await ComputeInstallerInputHashAsync(
                context,
                packageKind,
                onlinePayload
            );
            payloadUncompressedBytes = GetDirectorySize(appPackageRoot!);
        }

        var installerPlan = new InstallerPlan
        {
            ProductName = AppConstants.ProductName,
            InstallerName = Path.GetFileName(installerOutputPath),
            AppUserModelId = AppConstants.AppUserModelId,
            CurrentUserDefault = false,
            PayloadDirectory = isOnline ? "payload" : "app-package",
            PayloadMode = isOnline ? "download-update-zip" : "bundled-archive",
            OnlinePayload = onlinePayload,
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
            packageKind,
            onlinePayload
        );

        if (!isOnline)
        {
            var archiveExitCode = await CreateMicaPayloadArchiveAsync(
                context,
                appPackageRoot!,
                payloadArchivePath!,
                installerInputHash
            );
            if (archiveExitCode != 0)
            {
                return archiveExitCode;
            }
        }

        var micaConfigPath = Path.Combine(stageRoot, "micasetup.json");
        var micaConfig = MicaSetupConfig.Create(
            context,
            payloadArchivePath ?? onlinePayload!.Url,
            installerOutputPath
        );
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
            installerOutputPath,
            packageKind,
            onlinePayload
        );
        if (buildExitCode != 0)
        {
            return buildExitCode;
        }

        await context.SigningService.SignAsync(installerOutputPath, context);
        if (context.Options.Incremental)
        {
            await IncrementalBuildCache.WriteFileAsync(
                context,
                packageType,
                installerInputHash,
                installerOutputPath
            );
        }

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

        CleanupPackageStaging(context, stageRoot);
        context.Logger.Info($"installer executable: {installerOutputPath}");
        return 0;
    }

    public static Task<int> VerifyPackagesAsync(BuildContext context)
    {
        return BuildTiming.TimeAsync(context, "verify", () => VerifyPackagesCoreAsync(context));
    }

    private static Task<int> VerifyPackagesCoreAsync(BuildContext context)
    {
        var verifier = new PackageVerifier(
            context,
            releasePackages: context.Options.ReleasePackages
        );
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
        FileMaterializer.MaterializeDirectory(context.PublishRoot, payloadRoot);

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
            context.Options.Compression.ToCompressionLevel(),
            includeBaseDirectory: false
        );
        await context.SigningService.SignAsync(packagePath, context);
        CleanupPackageStaging(context, stageRoot);
        context.Logger.Info($"update package: {packagePath}");
        return 0;
    }

    private static async Task<OnlineInstallerPayload> CreateOnlineInstallerPayloadAsync(
        BuildContext context
    )
    {
        var fileName =
            $"CodexCliPlus.Update.{context.Options.Version}.{context.Options.Runtime}.zip";
        var packagePath = Path.Combine(context.PackageRoot, fileName);
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException(
                "Online installer requires the update package. Run package-update before package-online-installer.",
                packagePath
            );
        }

        var baseUrl = string.IsNullOrWhiteSpace(context.Options.OnlinePayloadBaseUrl)
            ? $"https://github.com/C4AL/CodexCliPlus/releases/download/v{context.Options.Version}"
            : context.Options.OnlinePayloadBaseUrl.Trim().TrimEnd('/', '\\');
        return new OnlineInstallerPayload
        {
            FileName = fileName,
            Url = $"{baseUrl}/{fileName}",
            Size = new FileInfo(packagePath).Length,
            Sha256 = await ComputeSha256Async(packagePath),
            InstallRoot = "payload",
        };
    }

    private static long GetUpdatePackagePayloadSize(
        BuildContext context,
        OnlineInstallerPayload onlinePayload
    )
    {
        var packagePath = Path.Combine(context.PackageRoot, onlinePayload.FileName);
        using var archive = ZipFile.OpenRead(packagePath);
        return archive
            .Entries.Where(entry =>
                entry.FullName.StartsWith("payload/", StringComparison.OrdinalIgnoreCase)
                && !entry.FullName.EndsWith('/')
            )
            .Sum(entry => entry.Length);
    }

    private static void CleanupPackageStaging(BuildContext context, string stageRoot)
    {
        if (context.Options.KeepPackageStaging)
        {
            context.Logger.Info($"kept package staging: {stageRoot}");
            return;
        }

        if (context.Options.Incremental && Directory.Exists(Path.Combine(stageRoot, ".dist")))
        {
            try
            {
                SafeFileSystem.CleanDirectoryExcept(stageRoot, context.Options.OutputRoot, [".dist"]);
                context.Logger.Info($"removed package staging; kept MicaSetup cache: {stageRoot}");
                return;
            }
            catch (IOException exception)
            {
                context.Logger.Warning(
                    $"Could not trim package staging because it is in use: {exception.Message}"
                );
                return;
            }
            catch (UnauthorizedAccessException exception)
            {
                context.Logger.Warning(
                    $"Could not trim package staging because access was denied: {exception.Message}"
                );
                return;
            }
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
            context.Options.Compression.ToCompressionLevel(),
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

    private static void CleanInstallerStage(BuildContext context, string stageRoot)
    {
        if (context.Options.Incremental)
        {
            SafeFileSystem.CleanDirectoryExcept(stageRoot, context.Options.OutputRoot, [".dist"]);
            return;
        }

        SafeFileSystem.CleanDirectory(stageRoot, context.Options.OutputRoot);
    }

    private static async Task<string> ComputeInstallerInputHashAsync(
        BuildContext context,
        InstallerPackageKind packageKind,
        OnlineInstallerPayload? onlinePayload
    )
    {
        var hasher = new IncrementalInputHasher();
        hasher.AddText("stage", packageKind == InstallerPackageKind.Online ? "online" : "offline");
        hasher.AddText("version", context.Options.Version);
        hasher.AddText("runtime", context.Options.Runtime);
        hasher.AddText("configuration", context.Options.Configuration);
        hasher.AddText("compression", context.Options.Compression.ToArgumentValue());
        hasher.AddText(
            "signing-required",
            SigningOptions.FromEnvironment().SigningRequired ? "true" : "false"
        );
        await hasher.AddDirectoryAsync("publish", context.PublishRoot);
        await hasher.AddDirectoryAsync(
            "micasetup-source",
            Path.Combine(context.Options.RepositoryRoot, "build", "micasetup", "source-template"),
            ["bin", "obj"]
        );
        await hasher.AddDirectoryAsync(
            "micasetup-overrides",
            Path.Combine(context.Options.RepositoryRoot, "build", "micasetup", "overrides")
        );
        await hasher.AddDirectoryAsync(
            "icons",
            Path.Combine(context.Options.RepositoryRoot, "resources", "icons")
        );
        await hasher.AddDirectoryAsync(
            "licenses",
            Path.Combine(context.Options.RepositoryRoot, "resources", "licenses")
        );
        await hasher.AddFileAsync(
            "root-license",
            Path.Combine(context.Options.RepositoryRoot, "LICENSE.txt")
        );

        if (packageKind == InstallerPackageKind.Offline)
        {
            await hasher.AddFileAsync(
                "webview2-standalone-x64",
                Path.Combine(
                    context.CacheRoot,
                    "webview2",
                    WebView2RuntimeAssets.StandaloneX64FileName
                )
            );
        }
        else if (onlinePayload is not null)
        {
            hasher.AddText("online-payload-file", onlinePayload.FileName);
            hasher.AddText("online-payload-url", onlinePayload.Url);
            hasher.AddText(
                "online-payload-size",
                onlinePayload.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            hasher.AddText("online-payload-sha256", onlinePayload.Sha256);
        }

        return hasher.Finish();
    }

    private static bool HasSigningMetadata(string installerOutputPath)
    {
        return File.Exists(ArtifactSignatureMetadata.GetSignaturePath(installerOutputPath))
            || File.Exists(ArtifactSignatureMetadata.GetUnsignedPath(installerOutputPath));
    }

    private static Task<int> CreateMicaPayloadArchiveAsync(
        BuildContext context,
        string appPackageRoot,
        string archivePath,
        string installerInputHash
    )
    {
        return BuildTiming.TimeAsync(
            context,
            "payload archive",
            () => CreateMicaPayloadArchiveCoreAsync(
                context,
                appPackageRoot,
                archivePath,
                installerInputHash
            )
        );
    }

    private static async Task<int> CreateMicaPayloadArchiveCoreAsync(
        BuildContext context,
        string appPackageRoot,
        string archivePath,
        string installerInputHash
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        var appPackageHash = await IncrementalInputHasher.HashDirectoryAsync(appPackageRoot);
        var archiveInputHasher = new IncrementalInputHasher();
        archiveInputHasher.AddText("stage", "payload-archive");
        archiveInputHasher.AddText("installer-input", installerInputHash);
        archiveInputHasher.AddText("app-package", appPackageHash);
        archiveInputHasher.AddText("compression", context.Options.Compression.ToArgumentValue());
        var archiveInputHash = archiveInputHasher.Finish();
        var cachedArchivePath = Path.Combine(
            context.IncrementalCacheRoot,
            "payload",
            $"offline-{context.Options.Version}-{context.Options.Runtime}-{context.Options.Compression.ToArgumentValue()}.7z"
        );
        if (context.Options.Incremental)
        {
            var cache = await IncrementalBuildCache.LookupFileAsync(
                context,
                "payload-archive",
                archiveInputHash,
                cachedArchivePath
            );
            context.Logger.Info(cache.Reason);
            if (cache.Hit)
            {
                FileMaterializer.MaterializeFile(
                    cachedArchivePath,
                    archivePath,
                    preferHardLink: true
                );
                return File.Exists(archivePath) ? 0 : 1;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cachedArchivePath)!);
        if (File.Exists(cachedArchivePath))
        {
            File.Delete(cachedArchivePath);
        }

        ZipFile.CreateFromDirectory(
            appPackageRoot,
            cachedArchivePath,
            context.Options.Compression.ToCompressionLevel(),
            includeBaseDirectory: false
        );
        if (context.Options.Incremental)
        {
            await IncrementalBuildCache.WriteFileAsync(
                context,
                "payload-archive",
                archiveInputHash,
                cachedArchivePath
            );
        }

        FileMaterializer.MaterializeFile(cachedArchivePath, archivePath, preferHardLink: true);
        context.Logger.Info("MicaSetup payload archive created from repo package contents.");
        return File.Exists(archivePath) ? 0 : 1;
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
