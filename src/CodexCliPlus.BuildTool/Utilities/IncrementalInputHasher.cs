using System.Security.Cryptography;
using System.Text;

namespace CodexCliPlus.BuildTool;

public sealed class IncrementalInputHasher
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public void AddText(string name, string value)
    {
        AppendString("text");
        AppendString(name);
        AppendString(value);
    }

    public async Task AddFileAsync(string name, string path)
    {
        AppendString("file");
        AppendString(name);
        if (!File.Exists(path))
        {
            AppendString("missing");
            return;
        }

        var info = new FileInfo(path);
        AppendString(info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await using var stream = File.OpenRead(path);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
    }

    public async Task AddDirectoryAsync(
        string name,
        string directory,
        IReadOnlyCollection<string>? excludedRootDirectoryNames = null
    )
    {
        AppendString("directory");
        AppendString(name);
        if (!Directory.Exists(directory))
        {
            AppendString("missing");
            return;
        }

        IReadOnlySet<string> excludedRoots = excludedRootDirectoryNames is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(excludedRootDirectoryNames, StringComparer.OrdinalIgnoreCase);
        foreach (
            var path in Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => !IsExcludedRoot(path, directory, excludedRoots))
                .OrderBy(
                    path => Path.GetRelativePath(directory, path),
                    StringComparer.OrdinalIgnoreCase
                )
        )
        {
            var relativePath = Path.GetRelativePath(directory, path).Replace('\\', '/');
            await AddFileAsync(relativePath, path);
        }
    }

    public string Finish()
    {
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static async Task<string> HashDirectoryAsync(
        string directory,
        IReadOnlyCollection<string>? excludedRootDirectoryNames = null
    )
    {
        var hasher = new IncrementalInputHasher();
        await hasher.AddDirectoryAsync("root", directory, excludedRootDirectoryNames);
        return hasher.Finish();
    }

    private void AppendString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var lengthBytes = BitConverter.GetBytes(bytes.Length);
        hash.AppendData(lengthBytes);
        hash.AppendData(bytes);
    }

    private static bool IsExcludedRoot(
        string path,
        string root,
        IReadOnlySet<string> excludedRootDirectoryNames
    )
    {
        if (excludedRootDirectoryNames.Count == 0)
        {
            return false;
        }

        var relativePath = Path.GetRelativePath(root, path);
        var firstSegment = relativePath
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries
            )
            .FirstOrDefault();
        return firstSegment is not null && excludedRootDirectoryNames.Contains(firstSegment);
    }
}
