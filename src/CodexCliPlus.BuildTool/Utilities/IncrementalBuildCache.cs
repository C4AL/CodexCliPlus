using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexCliPlus.BuildTool;

public enum BuildCompressionMode
{
    Optimal,
    Smallest,
}

[Flags]
public enum ForceRebuildStage
{
    None = 0,
    WebUi = 1,
    Publish = 2,
    Installer = 4,
    All = WebUi | Publish | Installer,
}

public sealed record IncrementalCacheEntry(
    string Stage,
    string InputHash,
    string OutputPath,
    string OutputSha256,
    long OutputSize,
    string Version,
    string Runtime,
    string Configuration,
    string Compression,
    DateTimeOffset UpdatedAtUtc
);

public sealed record IncrementalCacheLookup(bool Hit, string Reason, IncrementalCacheEntry? Entry);

public static class BuildCompressionModeExtensions
{
    public static CompressionLevel ToCompressionLevel(this BuildCompressionMode mode)
    {
        return mode switch
        {
            BuildCompressionMode.Smallest => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal,
        };
    }

    public static string ToArgumentValue(this BuildCompressionMode mode)
    {
        return mode switch
        {
            BuildCompressionMode.Smallest => "smallest",
            _ => "optimal",
        };
    }

    public static bool TryParse(string value, out BuildCompressionMode mode)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "optimal":
                mode = BuildCompressionMode.Optimal;
                return true;
            case "smallest":
            case "smallest-size":
                mode = BuildCompressionMode.Smallest;
                return true;
            default:
                mode = BuildCompressionMode.Optimal;
                return false;
        }
    }
}

public static class ForceRebuildStageExtensions
{
    public static bool Includes(this ForceRebuildStage stages, ForceRebuildStage stage)
    {
        return (stages & stage) != 0;
    }

    public static bool TryParse(string value, out ForceRebuildStage stages, out string? error)
    {
        stages = ForceRebuildStage.None;
        error = null;

        foreach (
            var token in value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            switch (token.ToLowerInvariant())
            {
                case "none":
                    break;
                case "webui":
                case "web-ui":
                    stages |= ForceRebuildStage.WebUi;
                    break;
                case "publish":
                    stages |= ForceRebuildStage.Publish;
                    break;
                case "installer":
                case "package":
                    stages |= ForceRebuildStage.Installer;
                    break;
                case "all":
                    stages |= ForceRebuildStage.All;
                    break;
                default:
                    error =
                        $"Unknown force rebuild stage '{token}'. Expected webui, publish, installer, or all.";
                    return false;
            }
        }

        return true;
    }
}

public static class IncrementalBuildCache
{
    public static async Task<IncrementalCacheLookup> LookupFileAsync(
        BuildContext context,
        string stage,
        string inputHash,
        string outputPath
    )
    {
        var entry = await ReadAsync(context, stage);
        if (entry is null)
        {
            return new IncrementalCacheLookup(false, $"{stage} cache state missing", null);
        }

        if (!string.Equals(entry.InputHash, inputHash, StringComparison.OrdinalIgnoreCase))
        {
            return new IncrementalCacheLookup(false, $"{stage} input changed", entry);
        }

        if (!File.Exists(outputPath))
        {
            return new IncrementalCacheLookup(false, $"{stage} output missing", entry);
        }

        var actualHash = await ComputeFileSha256Async(outputPath);
        if (!string.Equals(actualHash, entry.OutputSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new IncrementalCacheLookup(false, $"{stage} output hash mismatch", entry);
        }

        return new IncrementalCacheLookup(true, $"{stage} cache hit", entry);
    }

    public static async Task<IncrementalCacheLookup> LookupDirectoryAsync(
        BuildContext context,
        string stage,
        string inputHash,
        string outputDirectory
    )
    {
        var entry = await ReadAsync(context, stage);
        if (entry is null)
        {
            return new IncrementalCacheLookup(false, $"{stage} cache state missing", null);
        }

        if (!string.Equals(entry.InputHash, inputHash, StringComparison.OrdinalIgnoreCase))
        {
            return new IncrementalCacheLookup(false, $"{stage} input changed", entry);
        }

        if (!Directory.Exists(outputDirectory))
        {
            return new IncrementalCacheLookup(false, $"{stage} output missing", entry);
        }

        var actualHash = await IncrementalInputHasher.HashDirectoryAsync(outputDirectory);
        if (!string.Equals(actualHash, entry.OutputSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new IncrementalCacheLookup(false, $"{stage} output hash mismatch", entry);
        }

        return new IncrementalCacheLookup(true, $"{stage} cache hit", entry);
    }

    public static async Task WriteFileAsync(
        BuildContext context,
        string stage,
        string inputHash,
        string outputPath
    )
    {
        var info = new FileInfo(outputPath);
        await WriteAsync(
            context,
            stage,
            new IncrementalCacheEntry(
                stage,
                inputHash,
                outputPath,
                await ComputeFileSha256Async(outputPath),
                info.Length,
                context.Options.Version,
                context.Options.Runtime,
                context.Options.Configuration,
                context.Options.Compression.ToArgumentValue(),
                DateTimeOffset.UtcNow
            )
        );
    }

    public static async Task WriteDirectoryAsync(
        BuildContext context,
        string stage,
        string inputHash,
        string outputDirectory
    )
    {
        await WriteAsync(
            context,
            stage,
            new IncrementalCacheEntry(
                stage,
                inputHash,
                outputDirectory,
                await IncrementalInputHasher.HashDirectoryAsync(outputDirectory),
                GetDirectorySize(outputDirectory),
                context.Options.Version,
                context.Options.Runtime,
                context.Options.Configuration,
                context.Options.Compression.ToArgumentValue(),
                DateTimeOffset.UtcNow
            )
        );
    }

    public static async Task<string> ComputeFileSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task<IncrementalCacheEntry?> ReadAsync(BuildContext context, string stage)
    {
        var path = GetEntryPath(context, stage);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<IncrementalCacheEntry>(
            stream,
            JsonDefaults.Options
        );
    }

    private static async Task WriteAsync(
        BuildContext context,
        string stage,
        IncrementalCacheEntry entry
    )
    {
        var path = GetEntryPath(context, stage);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(entry, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
    }

    private static string GetEntryPath(BuildContext context, string stage)
    {
        var fileName = string.Concat(
            stage.Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_'
                    ? character
                    : '-'
            )
        );
        return Path.Combine(context.IncrementalCacheRoot, $"{fileName}.json");
    }

    private static long GetDirectorySize(string directory)
    {
        return Directory.Exists(directory)
            ? Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length)
            : 0;
    }
}
