namespace CPAD.Core.Models.Management;

public sealed class ManagementConfigSnapshot
{
    public bool? Debug { get; init; }

    public string? ProxyUrl { get; init; }

    public int? RequestRetry { get; init; }

    public int? MaxRetryInterval { get; init; }

    public ManagementQuotaExceededSettings? QuotaExceeded { get; init; }

    public bool? UsageStatisticsEnabled { get; init; }

    public bool? RequestLog { get; init; }

    public bool? LoggingToFile { get; init; }

    public int? LogsMaxTotalSizeMb { get; init; }

    public int? ErrorLogsMaxFiles { get; init; }

    public bool? WebSocketAuth { get; init; }

    public bool? ForceModelPrefix { get; init; }

    public string? RoutingStrategy { get; init; }

    public IReadOnlyList<string> ApiKeys { get; init; } = [];

    public IReadOnlyList<ManagementGeminiKeyConfiguration> GeminiApiKeys { get; init; } = [];

    public IReadOnlyList<ManagementProviderKeyConfiguration> CodexApiKeys { get; init; } = [];

    public IReadOnlyList<ManagementProviderKeyConfiguration> ClaudeApiKeys { get; init; } = [];

    public IReadOnlyList<ManagementProviderKeyConfiguration> VertexApiKeys { get; init; } = [];

    public IReadOnlyList<ManagementOpenAiCompatibilityEntry> OpenAiCompatibility { get; init; } = [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> OAuthExcludedModels { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public ManagementAmpCodeConfiguration? AmpCode { get; init; }

    public string RawJson { get; init; } = "{}";
}

public sealed class ManagementQuotaExceededSettings
{
    public bool? SwitchProject { get; init; }

    public bool? SwitchPreviewModel { get; init; }

    public bool? AntigravityCredits { get; init; }
}

public sealed class ManagementAmpCodeConfiguration
{
    public string? UpstreamUrl { get; init; }

    public string? UpstreamApiKey { get; init; }

    public bool? ForceModelMappings { get; init; }

    public IReadOnlyList<ManagementAmpCodeModelMapping> ModelMappings { get; init; } = [];

    public IReadOnlyList<ManagementAmpCodeUpstreamApiKeyMapping> UpstreamApiKeys { get; init; } = [];
}

public sealed class ManagementAmpCodeModelMapping
{
    public string From { get; init; } = string.Empty;

    public string To { get; init; } = string.Empty;
}

public sealed class ManagementAmpCodeUpstreamApiKeyMapping
{
    public string UpstreamApiKey { get; init; } = string.Empty;

    public IReadOnlyList<string> ApiKeys { get; init; } = [];
}
