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
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Source file not found: {sourcePath}", sourcePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        if (preferHardLink && TryCreateHardLink(targetPath, sourcePath, hardLinkCreator))
        {
            return true;
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
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
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");
        }

        var linked = 0;
        var copied = 0;
        foreach (
            var directory in Directory.EnumerateDirectories(
                sourceDirectory,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            Directory.CreateDirectory(
                Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, directory))
            );
        }

        Directory.CreateDirectory(targetDirectory);
        foreach (
            var sourcePath in Directory.EnumerateFiles(
                sourceDirectory,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            var targetPath = Path.Combine(
                targetDirectory,
                Path.GetRelativePath(sourceDirectory, sourcePath)
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
