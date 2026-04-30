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
        EnsureVendoredLayout(context);

        context.Logger.Info("preparing vendored WebUI temporary build worktree");
        SafeFileSystem.CleanDirectory(context.WebUiBuildRoot, context.Options.OutputRoot);
        SafeFileSystem.CopyDirectory(
            context.WebUiSourceRoot,
            context.WebUiBuildSourceRoot,
            excludedRootDirectoryNames: ["node_modules", "dist"]
        );
        SafeFileSystem.CopyDirectory(context.WebUiOverlaySourceRoot, context.WebUiBuildSourceRoot);

        var dependencyCacheKey = await ComputeSha256Async(context.WebUiSourcePackageLockPath);
        var cachedNodeModulesRoot = Path.Combine(
            context.WebUiNodeModulesCacheRoot,
            dependencyCacheKey,
            "node_modules"
        );

        if (Directory.Exists(cachedNodeModulesRoot))
        {
            context.Logger.Info("using cached vendored WebUI dependencies");
            SafeFileSystem.CopyDirectory(
                cachedNodeModulesRoot,
                Path.Combine(context.WebUiBuildSourceRoot, "node_modules")
            );
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
            SafeFileSystem.CopyDirectory(installedNodeModulesRoot, cachedNodeModulesRoot);
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
        return 0;
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
