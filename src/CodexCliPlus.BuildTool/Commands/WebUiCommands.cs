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

public static class WebUiCommands
{
    private const long MaxIndexHtmlBytes = 512 * 1024;
    private const long MaxAssetBytesTotal = 24 * 1024 * 1024;

    public static async Task<int> BuildVendoredAsync(BuildContext context)
    {
        return await BuildTiming.TimeAsync(context, "webui", () => BuildVendoredCoreAsync(context));
    }

    private static async Task<int> BuildVendoredCoreAsync(BuildContext context)
    {
        EnsureVendoredLayout(context);

        var inputHash = await ComputeInputHashAsync(context);
        if (
            context.Options.Incremental
            && !context.Options.ForceRebuild.Includes(ForceRebuildStage.WebUi)
        )
        {
            var cache = await IncrementalBuildCache.LookupDirectoryAsync(
                context,
                "webui",
                inputHash,
                context.WebUiGeneratedRoot
            );
            context.Logger.Info(cache.Reason);
            if (cache.Hit)
            {
                return 0;
            }
        }
        else if (!context.Options.Incremental)
        {
            context.Logger.Info("webui incremental cache disabled");
        }
        else
        {
            context.Logger.Info("webui force rebuild requested");
        }

        context.Logger.Info("preparing vendored WebUI persistent build worktree");
        PrepareBuildSourceRoot(context);
        SafeFileSystem.CopyDirectory(
            context.WebUiSourceRoot,
            context.WebUiBuildSourceRoot,
            excludedRootDirectoryNames: ["node_modules", "dist"]
        );
        SafeFileSystem.CopyDirectory(context.WebUiOverlaySourceRoot, context.WebUiBuildSourceRoot);
        SafeFileSystem.CleanDirectory(context.WebUiBuildDistRoot, context.Options.OutputRoot);

        var dependencyCacheKey = await ComputeSha256Async(context.WebUiSourcePackageLockPath);
        var cachedNodeModulesRoot = Path.Combine(
            context.WebUiNodeModulesCacheRoot,
            dependencyCacheKey,
            "node_modules"
        );
        var localNodeModulesRoot = Path.Combine(context.WebUiBuildSourceRoot, "node_modules");
        var localNodeModulesKeyPath = Path.Combine(
            context.WebUiBuildSourceRoot,
            ".codexcliplus-node-modules-key"
        );

        if (
            Directory.Exists(localNodeModulesRoot)
            && File.Exists(localNodeModulesKeyPath)
            && string.Equals(
                await File.ReadAllTextAsync(localNodeModulesKeyPath),
                dependencyCacheKey,
                StringComparison.Ordinal
            )
        )
        {
            context.Logger.Info("using persistent vendored WebUI dependencies");
        }
        else if (Directory.Exists(cachedNodeModulesRoot))
        {
            context.Logger.Info("using cached vendored WebUI dependencies");
            if (Directory.Exists(localNodeModulesRoot))
            {
                Directory.Delete(localNodeModulesRoot, recursive: true);
            }

            FileMaterializer.MaterializeDirectory(
                cachedNodeModulesRoot,
                localNodeModulesRoot,
                preferHardLinks: true
            );
            await File.WriteAllTextAsync(localNodeModulesKeyPath, dependencyCacheKey);
        }
        else
        {
            context.Logger.Info("installing vendored WebUI dependencies in temporary worktree");
            var installExitCode = await context.ProcessRunner.RunAsync(
                ResolveNpmExecutable(),
                ["ci"],
                context.WebUiBuildSourceRoot,
                context.Logger
            );
            if (installExitCode != 0)
            {
                context.Logger.Error($"npm ci failed with exit code {installExitCode}.");
                return installExitCode;
            }

            var installedNodeModulesRoot = Path.Combine(
                context.WebUiBuildSourceRoot,
                "node_modules"
            );
            if (!Directory.Exists(installedNodeModulesRoot))
            {
                context.Logger.Error("npm ci completed without creating node_modules.");
                return 1;
            }

            var cacheEntryRoot = Path.Combine(
                context.WebUiNodeModulesCacheRoot,
                dependencyCacheKey
            );
            SafeFileSystem.CleanDirectory(cacheEntryRoot, context.Options.OutputRoot);
            FileMaterializer.MaterializeDirectory(
                installedNodeModulesRoot,
                cachedNodeModulesRoot,
                preferHardLinks: true
            );
            await File.WriteAllTextAsync(localNodeModulesKeyPath, dependencyCacheKey);
        }

        context.Logger.Info("building vendored WebUI");
        var buildExitCode = await context.ProcessRunner.RunAsync(
            ResolveNpmExecutable(),
            ["run", "build"],
            context.WebUiBuildSourceRoot,
            context.Logger
        );
        if (buildExitCode != 0)
        {
            context.Logger.Error($"npm run build failed with exit code {buildExitCode}.");
            return buildExitCode;
        }

        var report = CreateBundleReport(context.WebUiBuildDistRoot);
        var distValidationError = ValidateBuiltDist(report);
        if (distValidationError is not null)
        {
            context.Logger.Error(distValidationError);
            return 1;
        }

        await File.WriteAllTextAsync(
            Path.Combine(context.WebUiBuildDistRoot, "bundle-report.json"),
            JsonSerializer.Serialize(report, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        SafeFileSystem.CleanDirectory(context.WebUiGeneratedRoot, context.Options.OutputRoot);
        SafeFileSystem.CopyDirectory(context.WebUiBuildDistRoot, context.WebUiGeneratedDistRoot);
        Directory.CreateDirectory(context.WebUiGeneratedRoot);
        File.Copy(
            context.WebUiSyncMetadataPath,
            context.WebUiGeneratedSyncMetadataPath,
            overwrite: true
        );
        context.Logger.Info($"vendored WebUI dist generated: {context.WebUiGeneratedDistRoot}");
        if (context.Options.Incremental)
        {
            await IncrementalBuildCache.WriteDirectoryAsync(
                context,
                "webui",
                inputHash,
                context.WebUiGeneratedRoot
            );
        }

        return 0;
    }

    private static async Task<string> ComputeInputHashAsync(BuildContext context)
    {
        var hasher = new IncrementalInputHasher();
        hasher.AddText("stage", "webui");
        await hasher.AddDirectoryAsync(
            "upstream-source",
            context.WebUiSourceRoot,
            ["node_modules", "dist"]
        );
        await hasher.AddDirectoryAsync("overlay-source", context.WebUiOverlaySourceRoot);
        await hasher.AddFileAsync("overlay-manifest", context.WebUiOverlayManifestPath);
        await hasher.AddFileAsync("sync", context.WebUiSyncMetadataPath);
        return hasher.Finish();
    }

    private static void PrepareBuildSourceRoot(BuildContext context)
    {
        Directory.CreateDirectory(context.WebUiBuildSourceRoot);
        foreach (var file in Directory.EnumerateFiles(context.WebUiBuildSourceRoot))
        {
            if (
                string.Equals(
                    Path.GetFileName(file),
                    ".codexcliplus-node-modules-key",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }

            File.Delete(file);
        }

        foreach (var directory in Directory.EnumerateDirectories(context.WebUiBuildSourceRoot))
        {
            if (
                string.Equals(
                    Path.GetFileName(directory),
                    "node_modules",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static WebUiBundleReport CreateBundleReport(string distRoot)
    {
        var indexPath = Path.Combine(distRoot, "index.html");
        var assetsRoot = Path.Combine(distRoot, "assets");
        var assets = Directory.Exists(assetsRoot)
            ? Directory
                .EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories)
                .Select(path => new WebUiBundleAsset(
                    Path.GetRelativePath(distRoot, path).Replace('\\', '/'),
                    new FileInfo(path).Length
                ))
                .OrderByDescending(asset => asset.Bytes)
                .ThenBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        return new WebUiBundleReport(
            File.Exists(indexPath) ? new FileInfo(indexPath).Length : 0,
            assets.Sum(asset => asset.Bytes),
            assets
        );
    }

    private static string? ValidateBuiltDist(WebUiBundleReport report)
    {
        if (report.IndexHtmlBytes <= 0)
        {
            return "vendored WebUI build output is missing dist/index.html.";
        }

        if (report.IndexHtmlBytes > MaxIndexHtmlBytes)
        {
            return $"vendored WebUI index.html is too large: {report.IndexHtmlBytes} bytes.";
        }

        if (report.Assets.Count == 0)
        {
            return "vendored WebUI build output is missing dist/assets files.";
        }

        if (report.AssetBytesTotal > MaxAssetBytesTotal)
        {
            return $"vendored WebUI assets are too large: {report.AssetBytesTotal} bytes.";
        }

        return null;
    }

    private static void EnsureVendoredLayout(BuildContext context)
    {
        if (!Directory.Exists(context.WebUiSourceRoot))
        {
            throw new DirectoryNotFoundException(
                $"Vendored WebUI source directory not found: {context.WebUiSourceRoot}"
            );
        }

        if (!File.Exists(context.WebUiSourcePackageJsonPath))
        {
            throw new FileNotFoundException(
                $"Vendored WebUI package.json not found: {context.WebUiSourcePackageJsonPath}",
                context.WebUiSourcePackageJsonPath
            );
        }

        if (!File.Exists(context.WebUiSourcePackageLockPath))
        {
            throw new FileNotFoundException(
                $"Vendored WebUI package-lock.json not found: {context.WebUiSourcePackageLockPath}",
                context.WebUiSourcePackageLockPath
            );
        }

        if (!File.Exists(context.WebUiSyncMetadataPath))
        {
            throw new FileNotFoundException(
                $"Vendored WebUI sync metadata not found: {context.WebUiSyncMetadataPath}",
                context.WebUiSyncMetadataPath
            );
        }

        if (!Directory.Exists(context.WebUiOverlayModuleRoot))
        {
            throw new DirectoryNotFoundException(
                $"Vendored WebUI overlay directory not found: {context.WebUiOverlayModuleRoot}"
            );
        }

        if (!File.Exists(context.WebUiOverlayManifestPath))
        {
            throw new FileNotFoundException(
                $"Vendored WebUI overlay manifest not found: {context.WebUiOverlayManifestPath}",
                context.WebUiOverlayManifestPath
            );
        }

        if (!Directory.Exists(context.WebUiOverlaySourceRoot))
        {
            throw new DirectoryNotFoundException(
                $"Vendored WebUI overlay source directory not found: {context.WebUiOverlaySourceRoot}"
            );
        }
    }

    private static string ResolveNpmExecutable()
    {
        var candidateNames = OperatingSystem.IsWindows()
            ? new[] { "npm.cmd", "npm.exe", "npm" }
            : new[] { "npm" };

        foreach (var candidateName in candidateNames)
        {
            var resolvedPath = TryResolveFromPath(candidateName);
            if (resolvedPath is not null)
            {
                return resolvedPath;
            }
        }

        return candidateNames[0];
    }

    private static string? TryResolveFromPath(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (
            var segment in pathValue.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            var candidatePath = Path.Combine(segment.Trim('"'), fileName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private sealed record WebUiBundleReport(
        long IndexHtmlBytes,
        long AssetBytesTotal,
        IReadOnlyList<WebUiBundleAsset> Assets
    );

    private sealed record WebUiBundleAsset(string Path, long Bytes);
}
