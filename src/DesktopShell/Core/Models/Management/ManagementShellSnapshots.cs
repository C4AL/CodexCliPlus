namespace CodexCliPlus.Core.Models.Management;

public sealed class ManagementShellStatusSnapshot
{
    public string ManagementApiBaseUrl { get; init; } = string.Empty;

    public string? ServerVersion { get; init; }

    public bool IsConnected { get; init; }

    public string? Error { get; init; }
}

public sealed class ManagementSettingsSummarySnapshot
{
    public string ManagementApiBaseUrl { get; init; } = string.Empty;

    public string? ServerVersion { get; init; }

    public int ApiKeyCount { get; init; }

    public int AuthFileCount { get; init; }

    public int GeminiKeyCount { get; init; }

    public int CodexKeyCount { get; init; }

    public int ClaudeKeyCount { get; init; }

    public int VertexKeyCount { get; init; }

    public int OpenAiCompatibilityCount { get; init; }

    public ManagementConfigSnapshot Config { get; init; } = new();
}
