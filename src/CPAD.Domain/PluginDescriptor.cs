namespace CPAD.Domain;

public sealed record PluginDescriptor(
    string Id,
    string Name,
    string Version,
    bool Enabled);
