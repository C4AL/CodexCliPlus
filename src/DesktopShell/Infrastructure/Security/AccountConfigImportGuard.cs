using YamlDotNet.RepresentationModel;

namespace CodexCliPlus.Infrastructure.Security;

public static class AccountConfigImportGuard
{
    private static readonly string[] LocalRuntimeKeys =
    [
        "host",
        "port",
        "auth-dir",
        "remote-management",
    ];

    public static string PreserveLocalRuntimeSettings(string importedYaml, string currentYaml)
    {
        if (string.IsNullOrWhiteSpace(importedYaml) || string.IsNullOrWhiteSpace(currentYaml))
        {
            return importedYaml;
        }

        var importedRoot = TryLoadRootMapping(importedYaml, out var importedStream);
        var currentRoot = TryLoadRootMapping(currentYaml, out _);
        if (importedRoot is null)
        {
            return importedYaml;
        }

        if (currentRoot is null)
        {
            throw new InvalidOperationException("本机运行配置无法解析。");
        }

        foreach (var key in LocalRuntimeKeys)
        {
            if (TryFindMappingEntry(currentRoot, key, out _, out var currentValue))
            {
                SetMappingValue(importedRoot, key, CloneNode(currentValue));
            }
            else
            {
                RemoveMappingValue(importedRoot, key);
            }
        }

        using var writer = new StringWriter();
        importedStream!.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private static YamlMappingNode? TryLoadRootMapping(string yaml, out YamlStream? stream)
    {
        stream = new YamlStream();
        using var reader = new StringReader(yaml);
        try
        {
            stream.Load(reader);
        }
        catch
        {
            stream = null;
            return null;
        }

        if (stream.Documents.Count == 0)
        {
            return null;
        }

        return stream.Documents[0].RootNode as YamlMappingNode;
    }

    private static bool TryFindMappingEntry(
        YamlMappingNode mapping,
        string key,
        out YamlNode keyNode,
        out YamlNode valueNode
    )
    {
        foreach (var entry in mapping.Children)
        {
            if (
                entry.Key is YamlScalarNode scalar
                && string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase)
            )
            {
                keyNode = entry.Key;
                valueNode = entry.Value;
                return true;
            }
        }

        keyNode = new YamlScalarNode(key);
        valueNode = new YamlScalarNode();
        return false;
    }

    private static void SetMappingValue(YamlMappingNode mapping, string key, YamlNode value)
    {
        if (TryFindMappingEntry(mapping, key, out var keyNode, out _))
        {
            mapping.Children[keyNode] = value;
            return;
        }

        mapping.Children.Add(new YamlScalarNode(key), value);
    }

    private static void RemoveMappingValue(YamlMappingNode mapping, string key)
    {
        if (TryFindMappingEntry(mapping, key, out var keyNode, out _))
        {
            mapping.Children.Remove(keyNode);
        }
    }

    private static YamlNode CloneNode(YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                return new YamlScalarNode(scalar.Value) { Style = scalar.Style };
            case YamlSequenceNode sequence:
                {
                    var clone = new YamlSequenceNode { Style = sequence.Style };
                    foreach (var child in sequence.Children)
                    {
                        clone.Children.Add(CloneNode(child));
                    }

                    return clone;
                }
            case YamlMappingNode mapping:
                {
                    var clone = new YamlMappingNode { Style = mapping.Style };
                    foreach (var child in mapping.Children)
                    {
                        clone.Children.Add(CloneNode(child.Key), CloneNode(child.Value));
                    }

                    return clone;
                }
            default:
                return node;
        }
    }
}
