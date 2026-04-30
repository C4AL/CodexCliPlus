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

public static class SafeFileSystem
{
    public static void CleanDirectory(string targetDirectory, string allowedRoot)
    {
        var fullTarget = Path.GetFullPath(targetDirectory);
        var fullAllowedRoot = Path.GetFullPath(allowedRoot);
        if (!fullTarget.StartsWith(fullAllowedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to clean outside BuildTool output root: {fullTarget}"
            );
        }

        if (Directory.Exists(fullTarget))
        {
            ClearReadOnlyAttributes(fullTarget);
            Directory.Delete(fullTarget, recursive: true);
        }

        Directory.CreateDirectory(fullTarget);
    }

    private static void ClearReadOnlyAttributes(string directory)
    {
        foreach (
            var path in Directory.EnumerateFileSystemEntries(
                directory,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        File.SetAttributes(directory, FileAttributes.Normal);
    }

    public static void CopyDirectory(
        string sourceDirectory,
        string targetDirectory,
        IReadOnlyCollection<string>? excludedRootDirectoryNames = null
    )
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");
        }

        var excludedRoots = excludedRootDirectoryNames is null
            ? null
            : new HashSet<string>(excludedRootDirectoryNames, StringComparer.OrdinalIgnoreCase);
        CopyDirectoryCore(sourceDirectory, targetDirectory, sourceDirectory, excludedRoots);
    }

    private static void CopyDirectoryCore(
        string currentSourceDirectory,
        string currentTargetDirectory,
        string sourceRootDirectory,
        HashSet<string>? excludedRootDirectoryNames
    )
    {
        Directory.CreateDirectory(currentTargetDirectory);

        foreach (var file in Directory.EnumerateFiles(currentSourceDirectory))
        {
            var targetPath = Path.Combine(currentTargetDirectory, Path.GetFileName(file));
            File.Copy(file, targetPath, overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(currentSourceDirectory))
        {
            var relativePath = Path.GetRelativePath(sourceRootDirectory, directory);
            if (IsExcludedRootDirectory(relativePath, excludedRootDirectoryNames))
            {
                continue;
            }

            CopyDirectoryCore(
                directory,
                Path.Combine(currentTargetDirectory, Path.GetFileName(directory)),
                sourceRootDirectory,
                excludedRootDirectoryNames
            );
        }
    }

    private static bool IsExcludedRootDirectory(
        string relativePath,
        HashSet<string>? excludedRootDirectoryNames
    )
    {
        if (excludedRootDirectoryNames is null || excludedRootDirectoryNames.Count == 0)
        {
            return false;
        }

        var firstSegment = relativePath
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries
            )
            .FirstOrDefault();
        return firstSegment is not null && excludedRootDirectoryNames.Contains(firstSegment);
    }

    public static void RequirePublishRoot(string publishRoot)
    {
        var requiredFiles = new[]
        {
            AppConstants.ExecutableName,
            Path.Combine("assets", "webui", "upstream", "dist", "index.html"),
            Path.Combine("assets", "webui", "upstream", "sync.json"),
        };

        foreach (var relativePath in requiredFiles)
        {
            var fullPath = Path.Combine(publishRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Publish output is missing required file. Run publish first: {fullPath}",
                    fullPath
                );
            }
        }

        var webUiAssetsRoot = Path.Combine(
            publishRoot,
            "assets",
            "webui",
            "upstream",
            "dist",
            "assets"
        );
        if (
            !Directory.Exists(webUiAssetsRoot)
            || !Directory.EnumerateFiles(webUiAssetsRoot, "*", SearchOption.AllDirectories).Any()
        )
        {
            throw new FileNotFoundException(
                $"Publish output is missing WebUI asset files. Run publish first: {webUiAssetsRoot}",
                webUiAssetsRoot
            );
        }
    }
}
