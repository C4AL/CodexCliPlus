using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.Core.Abstractions.Management;

public interface IManagementOverviewService
{
    Task<ManagementApiResponse<ManagementOverviewSnapshot>> GetOverviewAsync(CancellationToken cancellationToken = default);
}

public interface IManagementSessionService
{
    Task<ManagementConnectionInfo> GetConnectionAsync(CancellationToken cancellationToken = default);
}

public interface IManagementConfigurationService
{
    Task<ManagementApiResponse<ManagementConfigSnapshot>> GetConfigAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<string>> GetConfigYamlAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> PutConfigYamlAsync(string yamlContent, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UpdateBooleanSettingAsync(string path, bool value, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UpdateIntegerSettingAsync(string path, int value, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UpdateStringSettingAsync(string path, string value, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> DeleteSettingAsync(string path, CancellationToken cancellationToken = default);
}

public interface IManagementProvidersService
{
    Task<ManagementApiResponse<IReadOnlyList<ManagementGeminiKeyConfiguration>>> GetGeminiKeysAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>> GetCodexKeysAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>> GetClaudeKeysAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>> GetVertexKeysAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementOpenAiCompatibilityEntry>>> GetOpenAiCompatibilityAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> ReplaceGeminiKeysAsync(
        IReadOnlyList<ManagementGeminiKeyConfiguration> configurations,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UpdateGeminiKeyAsync(
        int index,
        ManagementGeminiKeyConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> DeleteGeminiKeyAsync(
        string apiKey,
        string? baseUrl = null,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> ReplaceCodexKeysAsync(
        IReadOnlyList<ManagementProviderKeyConfiguration> configurations,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UpdateCodexKeyAsync(
        int index,
        ManagementProviderKeyConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> DeleteCodexKeyAsync(
        string apiKey,
        string? baseUrl = null,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> ReplaceClaudeKeysAsync(
        IReadOnlyList<ManagementProviderKeyConfiguration> configurations,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UpdateClaudeKeyAsync(
        int index,
        ManagementProviderKeyConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> DeleteClaudeKeyAsync(
        string apiKey,
        string? baseUrl = null,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> ReplaceVertexKeysAsync(
        IReadOnlyList<ManagementProviderKeyConfiguration> configurations,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UpdateVertexKeyAsync(
        int index,
        ManagementProviderKeyConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> DeleteVertexKeyAsync(
        string apiKey,
        string? baseUrl = null,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> ReplaceOpenAiCompatibilityAsync(
        IReadOnlyList<ManagementOpenAiCompatibilityEntry> providers,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UpdateOpenAiCompatibilityAsync(
        int index,
        ManagementOpenAiCompatibilityEntry provider,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> DeleteOpenAiCompatibilityAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementAmpCodeConfiguration>> GetAmpCodeAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UpdateAmpUpstreamUrlAsync(string? upstreamUrl, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UpdateAmpUpstreamApiKeyAsync(string? upstreamApiKey, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementAmpCodeUpstreamApiKeyMapping>>> GetAmpUpstreamApiKeysAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> ReplaceAmpUpstreamApiKeysAsync(
        IReadOnlyList<ManagementAmpCodeUpstreamApiKeyMapping> mappings,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementAmpCodeModelMapping>>> GetAmpModelMappingsAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> ReplaceAmpModelMappingsAsync(
        IReadOnlyList<ManagementAmpCodeModelMapping> mappings,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> SetAmpForceModelMappingsAsync(bool enabled, CancellationToken cancellationToken = default);
}

public interface IManagementAuthFilesService
{
    Task<ManagementApiResponse<IReadOnlyList<ManagementAuthFileItem>>> GetAuthFilesAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UploadAuthFileAsync(string fileName, string jsonContent, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UploadAuthFilesAsync(
        IReadOnlyList<ManagementAuthFileUpload> files,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> DeleteAuthFileAsync(string name, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> DeleteAuthFilesAsync(IReadOnlyList<string> names, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> DeleteAllAuthFilesAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<string>> DownloadAuthFileAsync(string name, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> SetAuthFileDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> PatchAuthFileFieldsAsync(ManagementAuthFileFieldPatch patch, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementModelDescriptor>>> GetAuthFileModelsAsync(string name, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementModelDescriptor>>> GetModelDefinitionsAsync(string channel, CancellationToken cancellationToken = default);
}

public interface IManagementOAuthService
{
    Task<ManagementApiResponse<IReadOnlyDictionary<string, IReadOnlyList<string>>>> GetOAuthExcludedModelsAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> ReplaceOAuthExcludedModelsAsync(
        IReadOnlyDictionary<string, IReadOnlyList<string>> excludedModels,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UpdateOAuthExcludedModelsAsync(
        string provider,
        IReadOnlyList<string> models,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> DeleteOAuthExcludedModelsAsync(
        string provider,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyDictionary<string, IReadOnlyList<ManagementOAuthModelAliasEntry>>>> GetOAuthModelAliasesAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> ReplaceOAuthModelAliasesAsync(
        IReadOnlyDictionary<string, IReadOnlyList<ManagementOAuthModelAliasEntry>> aliases,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UpdateOAuthModelAliasAsync(
        string channel,
        IReadOnlyList<ManagementOAuthModelAliasEntry> aliases,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> DeleteOAuthModelAliasAsync(
        string channel,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOAuthStartResponse>> GetOAuthStartAsync(string provider, string? projectId = null, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOAuthStatus>> GetOAuthStatusAsync(string state, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> SubmitOAuthCallbackAsync(string provider, string redirectUrl, CancellationToken cancellationToken = default);
}

public interface IManagementQuotaService
{
    Task<ManagementApiResponse<ManagementConfigSnapshot>> GetQuotaSettingsAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> SetSwitchProjectAsync(bool enabled, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> SetSwitchPreviewModelAsync(bool enabled, CancellationToken cancellationToken = default);
}

public interface IManagementAuthService : IManagementProvidersService, IManagementAuthFilesService, IManagementOAuthService
{
    Task<ManagementApiResponse<IReadOnlyList<string>>> GetApiKeysAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> ReplaceApiKeysAsync(IReadOnlyList<string> apiKeys, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UpdateApiKeyAsync(int index, string value, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> DeleteApiKeyAsync(int index, CancellationToken cancellationToken = default);
}

public interface IManagementUsageService
{
    Task<ManagementApiResponse<ManagementUsageSnapshot>> GetUsageAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementUsageExportPayload>> ExportUsageAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementUsageImportResult>> ImportUsageAsync(ManagementUsageExportPayload payload, CancellationToken cancellationToken = default);
}

public interface IManagementLogsService
{
    Task<ManagementApiResponse<ManagementLogsSnapshot>> GetLogsAsync(long after = 0, int limit = 0, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> ClearLogsAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementErrorLogFile>>> GetRequestErrorLogsAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<string>> GetRequestLogByIdAsync(string id, CancellationToken cancellationToken = default);
}

public interface IManagementSystemService
{
    Task<ManagementApiResponse<ManagementLatestVersionInfo>> GetLatestVersionAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementModelDescriptor>>> GetAvailableModelsAsync(
        string? apiKey = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementApiCallResult>> ExecuteApiCallAsync(ManagementApiCallRequest request, CancellationToken cancellationToken = default);
}
