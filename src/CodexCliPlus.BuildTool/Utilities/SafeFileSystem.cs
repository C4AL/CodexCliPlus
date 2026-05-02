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
        var fullTarget = NormalizeDirectoryPath(targetDirectory);
        var fullAllowedRoot = NormalizeDirectoryPath(allowedRoot);
        if (!IsSameOrDescendant(fullTarget, fullAllowedRoot))
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

    public static void DeleteDirectory(string targetDirectory, string allowedRoot)
    {
        var fullTarget = NormalizeDirectoryPath(targetDirectory);
        var fullAllowedRoot = NormalizeDirectoryPath(allowedRoot);
        if (!IsSameOrDescendant(fullTarget, fullAllowedRoot))
        {
            throw new InvalidOperationException(
                $"Refusing to delete outside BuildTool output root: {fullTarget}"
            );
        }

        if (Directory.Exists(fullTarget))
        {
            ClearReadOnlyAttributes(fullTarget);
            Directory.Delete(fullTarget, recursive: true);
        }
    }

    public static void DeleteBuildToolOutputRoot(
        string targetDirectory,
        string repositoryRoot,
        string currentOutputRoot
    )
    {
        var repositoryArtifactsRoot = Path.Combine(repositoryRoot, "artifacts");
        var fullTarget = NormalizeDirectoryPath(targetDirectory);
        if (!IsBuildToolOutputRoot(fullTarget, repositoryArtifactsRoot))
        {
            throw new InvalidOperationException(
                $"Refusing to delete non-BuildTool output root: {fullTarget}"
            );
        }

        if (PathsEqual(fullTarget, currentOutputRoot))
        {
            throw new InvalidOperationException(
                $"Refusing to delete current BuildTool output root: {fullTarget}"
            );
        }

        DeleteDirectory(fullTarget, repositoryArtifactsRoot);
    }

    public static bool IsBuildToolOutputRoot(string outputRoot, string repositoryArtifactsRoot)
    {
        var fullOutputRoot = NormalizeDirectoryPath(outputRoot);
        var fullArtifactsRoot = NormalizeDirectoryPath(repositoryArtifactsRoot);
        var parent = Directory.GetParent(fullOutputRoot)?.FullName;
        if (parent is null || !PathsEqual(parent, fullArtifactsRoot))
        {
            return false;
        }

        return Path.GetFileName(fullOutputRoot)
            .StartsWith("buildtool", StringComparison.OrdinalIgnoreCase);
    }

    public static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            NormalizeDirectoryPath(left),
            NormalizeDirectoryPath(right),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool IsSameOrDescendant(string path, string root)
    {
        if (PathsEqual(path, root))
        {
            return true;
        }

        var relativePath = Path.GetRelativePath(root, path);
        return relativePath.Length > 0
            && relativePath != "."
            && !relativePath.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(relativePath);
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
