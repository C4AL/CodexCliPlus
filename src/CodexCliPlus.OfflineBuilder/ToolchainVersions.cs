using System.Text.Json;

namespace CodexCliPlus.OfflineBuilder;

internal sealed record ToolchainVersions(string DotNetSdk, string Node, string Go)
{
    public const string FixedGoVersion = "1.26.2";

    public static ToolchainVersions Read(string repositoryRoot)
    {
        var dotNetVersion = ReadDotNetSdkVersion(Path.Combine(repositoryRoot, "global.json"));
        var nodeVersion = ReadMiseToolVersion(Path.Combine(repositoryRoot, ".mise.toml"), "node");
        return new ToolchainVersions(dotNetVersion, nodeVersion, FixedGoVersion);
    }

    private static string ReadDotNetSdkVersion(string path)
    {
        if (!File.Exists(path))
        {
            throw new OfflineBuilderException($"未找到 global.json：{path}");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (
            !document.RootElement.TryGetProperty("sdk", out var sdk)
            || !sdk.TryGetProperty("version", out var version)
            || string.IsNullOrWhiteSpace(version.GetString())
        )
        {
            throw new OfflineBuilderException("global.json 中缺少 sdk.version。");
        }

        return version.GetString()!;
    }

    private static string ReadMiseToolVersion(string path, string toolName)
    {
        if (!File.Exists(path))
        {
            throw new OfflineBuilderException($"未找到 .mise.toml：{path}");
        }

        var inToolsSection = false;
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inToolsSection = string.Equals(line, "[tools]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inToolsSection)
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (!string.Equals(key, toolName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new OfflineBuilderException($".mise.toml 的 [tools] 中缺少 {toolName} 版本。");
    }
}
