namespace CPAD.Core.Models.Management;

public sealed class ManagementModelAlias
{
    public string Name { get; init; } = string.Empty;

    public string? Alias { get; init; }

    public int? Priority { get; init; }

    public string? TestModel { get; init; }
}

public sealed class ManagementApiKeyEntry
{
    public string ApiKey { get; init; } = string.Empty;

    public string? ProxyUrl { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string? AuthIndex { get; init; }
}

public sealed class ManagementCloakConfiguration
{
    public string? Mode { get; init; }

    public bool? StrictMode { get; init; }

    public IReadOnlyList<string> SensitiveWords { get; init; } = [];
}

public class ManagementProviderKeyConfiguration
{
    public string ApiKey { get; init; } = string.Empty;

    public int? Priority { get; init; }

    public string? Prefix { get; init; }

    public string? BaseUrl { get; init; }

    public bool? WebSockets { get; init; }

    public string? ProxyUrl { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ManagementModelAlias> Models { get; init; } = [];

    public IReadOnlyList<string> ExcludedModels { get; init; } = [];

    public ManagementCloakConfiguration? Cloak { get; init; }

    public string? AuthIndex { get; init; }
}

public sealed class ManagementGeminiKeyConfiguration : ManagementProviderKeyConfiguration;

public sealed class ManagementOpenAiCompatibilityEntry
{
    public string Name { get; init; } = string.Empty;

    public string? Prefix { get; init; }

    public string BaseUrl { get; init; } = string.Empty;

    public IReadOnlyList<ManagementApiKeyEntry> ApiKeyEntries { get; init; } = [];

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ManagementModelAlias> Models { get; init; } = [];

    public int? Priority { get; init; }

    public string? TestModel { get; init; }

    public string? AuthIndex { get; init; }
}
