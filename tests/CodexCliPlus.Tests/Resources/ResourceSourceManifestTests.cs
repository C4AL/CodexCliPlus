using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace CodexCliPlus.Tests.Resources;

public sealed class ResourceSourceManifestTests
{
    private static readonly string[] RequiredBinaryResources =
    [
        "resources/fonts/Fgi-Regular.ttf",
        "resources/fonts/MiSans-Regular.ttf",
        "resources/icons/codexcliplus-display.png",
        "resources/icons/codexcliplus.ico",
        "resources/icons/ico-transparent.png",
        "resources/webui/upstream/source/logo.jpg",
    ];

    [Fact]
    public async Task SourceManifestCoversTrackedBinaryResourcesAndMatchesHashes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifestPath = Path.Combine(repositoryRoot, "resources", "source-manifest.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var resources = document
            .RootElement.GetProperty("resources")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("path").GetString() ?? string.Empty,
                item => item,
                StringComparer.OrdinalIgnoreCase
            );

        foreach (var requiredPath in RequiredBinaryResources)
        {
            Assert.True(resources.ContainsKey(requiredPath), $"Missing resource: {requiredPath}");
        }

        foreach (var trackedBinaryPath in GetTrackedBinaryResourcePaths(repositoryRoot))
        {
            Assert.True(
                resources.ContainsKey(trackedBinaryPath),
                $"Tracked binary resource is missing from resources/source-manifest.json: {trackedBinaryPath}"
            );
        }

        foreach (var (relativePath, entry) in resources)
        {
            var fullPath = Path.Combine(
                repositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)
            );
            Assert.True(File.Exists(fullPath), $"Resource file missing: {relativePath}");
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("source").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("license").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("updatePolicy").GetString()));
            Assert.Equal(new FileInfo(fullPath).Length, entry.GetProperty("size").GetInt64());
            Assert.Equal(
                await ComputeSha256Async(fullPath),
                entry.GetProperty("sha256").GetString()
            );
        }
    }

    private static string[] GetTrackedBinaryResourcePaths(string repositoryRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("ls-files");
        startInfo.ArgumentList.Add("resources");

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return RequiredBinaryResources;
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            return RequiredBinaryResources;
        }

        return output
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .Where(IsBinaryResourcePath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsBinaryResourcePath(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant()
            is ".ttf"
                or ".otf"
                or ".woff"
                or ".woff2"
                or ".png"
                or ".jpg"
                or ".jpeg"
                or ".ico";
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodexCliPlus.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
