using System.Runtime.InteropServices;

namespace CodexCliPlus.BuildTool;

public sealed record FileMaterializeResult(int LinkedFiles, int CopiedFiles);

public static class FileMaterializer
{
    public static bool MaterializeFile(
        string sourcePath,
        string targetPath,
        bool preferHardLink = true,
        Func<string, string, bool>? hardLinkCreator = null
    )
    {
        var fullSource = NormalizePath(sourcePath);
        var fullTarget = NormalizePath(targetPath);
        if (!File.Exists(fullSource))
        {
            throw new FileNotFoundException($"Source file not found: {fullSource}", fullSource);
        }

        if (PathsEqual(fullSource, fullTarget))
        {
            throw new InvalidOperationException(
                $"Refusing to materialize file onto itself: {fullSource}"
            );
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullTarget)!);
        if (File.Exists(fullTarget))
        {
            File.Delete(fullTarget);
        }

        if (preferHardLink && TryCreateHardLink(fullTarget, fullSource, hardLinkCreator))
        {
            return true;
        }

        File.Copy(fullSource, fullTarget, overwrite: true);
        return false;
    }

    public static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        MaterializeDirectory(sourceDirectory, targetDirectory, preferHardLinks: false);
    }

    public static FileMaterializeResult MaterializeDirectory(
        string sourceDirectory,
        string targetDirectory,
        bool preferHardLinks = true,
        Func<string, string, bool>? hardLinkCreator = null
    )
    {
        var fullSource = NormalizePath(sourceDirectory);
        var fullTarget = NormalizePath(targetDirectory);
        if (!Directory.Exists(fullSource))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {fullSource}");
        }

        if (PathsEqual(fullSource, fullTarget) || IsSameOrDescendant(fullTarget, fullSource))
        {
            throw new InvalidOperationException(
                $"Refusing to materialize directory into itself: {fullSource} -> {fullTarget}"
            );
        }

        var linked = 0;
        var copied = 0;
        foreach (
            var directory in Directory.EnumerateDirectories(
                fullSource,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            Directory.CreateDirectory(
                Path.Combine(fullTarget, Path.GetRelativePath(fullSource, directory))
            );
        }

        Directory.CreateDirectory(fullTarget);
        foreach (
            var sourcePath in Directory.EnumerateFiles(
                fullSource,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            var targetPath = Path.Combine(
                fullTarget,
                Path.GetRelativePath(fullSource, sourcePath)
            );
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            if (preferHardLinks && TryCreateHardLink(targetPath, sourcePath, hardLinkCreator))
            {
                linked++;
                continue;
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
            copied++;
        }

        return new FileMaterializeResult(linked, copied);
    }

    private static string NormalizePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            NormalizePath(left),
            NormalizePath(right),
            StringComparison.OrdinalIgnoreCase
        );
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

    private static bool TryCreateHardLink(
        string targetPath,
        string sourcePath,
        Func<string, string, bool>? hardLinkCreator
    )
    {
        try
        {
            if (hardLinkCreator is not null)
            {
                return hardLinkCreator(targetPath, sourcePath);
            }

            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            if (!CreateHardLink(targetPath, sourcePath, IntPtr.Zero))
            {
                return false;
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes
    );
}
