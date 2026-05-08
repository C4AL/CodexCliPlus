using YamlDotNet.RepresentationModel;

namespace CodexCliPlus.Infrastructure.Backend;

public static class BackendConfigReader
{
    public static string? TryReadAuthDirectory(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }

        var root = TryLoadRootMapping(yaml);
        if (root is null)
        {
            return null;
        }

        foreach (var entry in root.Children)
        {
            if (
                entry.Key is YamlScalarNode keyNode
                && string.Equals(keyNode.Value, "auth-dir", StringComparison.OrdinalIgnoreCase)
            )
            {
                return entry.Value is YamlScalarNode valueNode
                    ? NormalizeDirectory(valueNode.Value)
                    : null;
            }
        }

        return null;
    }

    private static YamlMappingNode? TryLoadRootMapping(string yaml)
    {
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            return stream.Documents.Count == 0
                ? null
                : stream.Documents[0].RootNode as YamlMappingNode;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeDirectory(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
