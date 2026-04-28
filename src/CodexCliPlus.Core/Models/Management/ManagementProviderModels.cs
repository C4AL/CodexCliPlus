using System.Text.Json.Serialization;

namespace CodexCliPlus.Core.Models.Management;

public sealed class ManagementModelAlias
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("alias")]
    public string? Alias { get; init; }

    [JsonPropertyName("priority")]
    public int? Priority { get; init; }

    [JsonPropertyName("test-model")]
    public string? TestModel { get; init; }
}

public sealed class ManagementApiKeyEntry
{
    [JsonPropertyName("api-key")]
    public string ApiKey { get; init; } = string.Empty;

    [JsonPropertyName("proxy-url")]
    public string? ProxyUrl { get; init; }

    [JsonPropertyName("headers")]
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("auth-index")]
    public string? AuthIndex { get; init; }
}

public sealed class ManagementCloakConfiguration
{
    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonPropertyName("strict-mode")]
    public bool? StrictMode { get; init; }

    [JsonPropertyName("sensitive-words")]
    public IReadOnlyList<string> SensitiveWords { get; init; } = [];
}

public class ManagementProviderKeyConfiguration
{
    [JsonPropertyName("api-key")]
    public string ApiKey { get; init; } = string.Empty;

    [JsonPropertyName("priority")]
    public int? Priority { get; init; }

    [JsonPropertyName("prefix")]
    public string? Prefix { get; init; }

    [JsonPropertyName("base-url")]
    public string? BaseUrl { get; init; }

    [JsonPropertyName("websockets")]
    public bool? WebSockets { get; init; }

    [JsonPropertyName("proxy-url")]
    public string? ProxyUrl { get; init; }

    [JsonPropertyName("headers")]
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("models")]
    public IReadOnlyList<ManagementModelAlias> Models { get; init; } = [];

    [JsonPropertyName("excluded-models")]
    public IReadOnlyList<string> ExcludedModels { get; init; } = [];

    [JsonPropertyName("cloak")]
    public ManagementCloakConfiguration? Cloak { get; init; }

    [JsonPropertyName("auth-index")]
    public string? AuthIndex { get; init; }
}

public sealed class ManagementGeminiKeyConfiguration : ManagementProviderKeyConfiguration;

public sealed class ManagementOpenAiCompatibilityEntry
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("prefix")]
    public string? Prefix { get; init; }

    [JsonPropertyName("base-url")]
    public string BaseUrl { get; init; } = string.Empty;

    [JsonPropertyName("api-key-entries")]
    public IReadOnlyList<ManagementApiKeyEntry> ApiKeyEntries { get; init; } = [];

    [JsonPropertyName("headers")]
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("models")]
    public IReadOnlyList<ManagementModelAlias> Models { get; init; } = [];

    [JsonPropertyName("priority")]
    public int? Priority { get; init; }

    [JsonPropertyName("test-model")]
    public string? TestModel { get; init; }

    [JsonPropertyName("auth-index")]
    public string? AuthIndex { get; init; }
}
