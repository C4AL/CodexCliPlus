using CPAD.Core.Models.Management;

namespace CPAD.Core.Abstractions.Management;

public interface IManagementOverviewService
{
    Task<ManagementApiResponse<ManagementOverviewSnapshot>> GetOverviewAsync(CancellationToken cancellationToken = default);
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

public interface IManagementAuthService
{
    Task<ManagementApiResponse<IReadOnlyList<string>>> GetApiKeysAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> ReplaceApiKeysAsync(IReadOnlyList<string> apiKeys, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementAuthFileItem>>> GetAuthFilesAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> UploadAuthFileAsync(string fileName, string jsonContent, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> DeleteAuthFileAsync(string name, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> SetAuthFileDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementGeminiKeyConfiguration>>> GetGeminiKeysAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>> GetCodexKeysAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>> GetClaudeKeysAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>> GetVertexKeysAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementOpenAiCompatibilityEntry>>> GetOpenAiCompatibilityAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyDictionary<string, IReadOnlyList<string>>>> GetOAuthExcludedModelsAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyDictionary<string, IReadOnlyList<ManagementOAuthModelAliasEntry>>>> GetOAuthModelAliasesAsync(CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementModelDescriptor>>> GetAuthFileModelsAsync(string name, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<IReadOnlyList<ManagementModelDescriptor>>> GetModelDefinitionsAsync(string channel, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOAuthStartResponse>> GetOAuthStartAsync(string provider, string? projectId = null, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOAuthStatus>> GetOAuthStatusAsync(string state, CancellationToken cancellationToken = default);

    Task<ManagementApiResponse<ManagementOperationResult>> SubmitOAuthCallbackAsync(string provider, string redirectUrl, CancellationToken cancellationToken = default);
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
