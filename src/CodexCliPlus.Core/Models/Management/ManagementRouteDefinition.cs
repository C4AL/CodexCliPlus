namespace CodexCliPlus.Core.Models.Management;

public sealed record ManagementRouteDefinition(
    string Key,
    string Path,
    string Title,
    bool IsPrimary,
    string? ParentKey = null
);
