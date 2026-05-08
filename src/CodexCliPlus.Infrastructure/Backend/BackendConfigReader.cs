using YamlDotNet.RepresentationModel;

namespace CodexCliPlus.Infrastructure.Backend;

public static class BackendConfigReader
{
    public static string? TryReadAuthDirectory(string yaml)
    {
        return TryReadRootScalar(yaml, "auth-dir");
    }

    public static string? TryReadRemoteManagementSecretKey(string yaml)
    {
        return TryReadNestedScalar(yaml, "remote-management", "secret-key");
    }

    private static string? TryReadRootScalar(string yaml, string key)
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
                && string.Equals(keyNode.Value, key, StringComparison.OrdinalIgnoreCase)
            )
            {
                return NormalizeScalar(entry.Value);
            }
        }

        return null;
    }

    private static string? TryReadNestedScalar(string yaml, string parentKey, string childKey)
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
                && string.Equals(keyNode.Value, parentKey, StringComparison.OrdinalIgnoreCase)
                && entry.Value is YamlMappingNode mapping
            )
            {
                return TryReadScalar(mapping, childKey);
            }
        }

        return null;
    }

    private static string? TryReadScalar(YamlMappingNode mapping, string key)
    {
        foreach (var entry in mapping.Children)
        {
            if (
                entry.Key is YamlScalarNode keyNode
                && string.Equals(keyNode.Value, key, StringComparison.OrdinalIgnoreCase)
            )
            {
                return NormalizeScalar(entry.Value);
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

    private static string? NormalizeScalar(YamlNode node)
    {
        return node is YamlScalarNode scalar ? NormalizeScalar(scalar.Value) : null;
    }

    private static string? NormalizeScalar(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
