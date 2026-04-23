namespace CPAD.Core.Models.Management;

public sealed class ManagementOverviewSnapshot
{
    public string ManagementApiBaseUrl { get; init; } = string.Empty;

    public int ApiKeyCount { get; init; }

    public int AuthFileCount { get; init; }

    public int GeminiKeyCount { get; init; }

    public int CodexKeyCount { get; init; }

    public int ClaudeKeyCount { get; init; }

    public int VertexKeyCount { get; init; }

    public int OpenAiCompatibilityCount { get; init; }

    public int? AvailableModelCount { get; init; }

    public string? AvailableModelsError { get; init; }

    public ManagementConfigSnapshot Config { get; init; } = new();
}
