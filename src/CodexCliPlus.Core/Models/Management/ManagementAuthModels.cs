using System.Text.Json.Serialization;

namespace CodexCliPlus.Core.Models.Management;

public sealed class ManagementAuthFileItem
{
    public string Name { get; init; } = string.Empty;

    public string? Id { get; init; }

    public string? Type { get; init; }

    public string? Provider { get; init; }

    public string? Label { get; init; }

    public string? Email { get; init; }

    public string? AccountType { get; init; }

    public string? Account { get; init; }

    public string? AuthIndex { get; init; }

    public long? Size { get; init; }

    public bool Disabled { get; init; }

    public bool Unavailable { get; init; }

    public bool RuntimeOnly { get; init; }

    public string? Status { get; init; }

    public string? StatusMessage { get; init; }

    public string? Source { get; init; }

    public string? Path { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public DateTimeOffset? LastRefresh { get; init; }

    public DateTimeOffset? NextRetryAfter { get; init; }

    public long Success { get; init; }

    public long Failed { get; init; }

    public IReadOnlyList<ManagementRecentRequestBucket> RecentRequests { get; init; } = [];
}

public sealed class ManagementRecentRequestBucket
{
    public string Time { get; init; } = string.Empty;

    public long Success { get; init; }

    public long Failed { get; init; }
}

public sealed class ManagementOAuthModelAliasEntry
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("alias")]
    public string Alias { get; init; } = string.Empty;

    [JsonPropertyName("fork")]
    public bool Fork { get; init; }
}

public sealed class ManagementModelDescriptor
{
    public string Id { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string? Type { get; init; }

    public string? OwnedBy { get; init; }

    public string? Alias { get; init; }

    public string? Description { get; init; }
}
