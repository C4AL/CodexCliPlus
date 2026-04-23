using CPAD.Core.Abstractions.Management;
using CPAD.Core.Models.Management;

namespace CPAD.Infrastructure.Management;

public sealed class ManagementOverviewService : IManagementOverviewService
{
    private readonly IManagementConnectionProvider _connectionProvider;
    private readonly IManagementConfigurationService _configurationService;
    private readonly IManagementAuthService _authService;
    private readonly IManagementSystemService _systemService;

    public ManagementOverviewService(
        IManagementConnectionProvider connectionProvider,
        IManagementConfigurationService configurationService,
        IManagementAuthService authService,
        IManagementSystemService systemService)
    {
        _connectionProvider = connectionProvider;
        _configurationService = configurationService;
        _authService = authService;
        _systemService = systemService;
    }

    public async Task<ManagementApiResponse<ManagementOverviewSnapshot>> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var connectionTask = _connectionProvider.GetConnectionAsync(cancellationToken);
        var configTask = _configurationService.GetConfigAsync(cancellationToken);
        var apiKeysTask = _authService.GetApiKeysAsync(cancellationToken);
        var authFilesTask = _authService.GetAuthFilesAsync(cancellationToken);
        var geminiTask = _authService.GetGeminiKeysAsync(cancellationToken);
        var codexTask = _authService.GetCodexKeysAsync(cancellationToken);
        var claudeTask = _authService.GetClaudeKeysAsync(cancellationToken);
        var vertexTask = _authService.GetVertexKeysAsync(cancellationToken);
        var openAiTask = _authService.GetOpenAiCompatibilityAsync(cancellationToken);

        await Task.WhenAll(
            connectionTask,
            configTask,
            apiKeysTask,
            authFilesTask,
            geminiTask,
            codexTask,
            claudeTask,
            vertexTask,
            openAiTask);

        var connection = await connectionTask;
        var config = await configTask;
        var apiKeys = await apiKeysTask;
        var authFiles = await authFilesTask;
        var geminiKeys = await geminiTask;
        var codexKeys = await codexTask;
        var claudeKeys = await claudeTask;
        var vertexKeys = await vertexTask;
        var openAiProviders = await openAiTask;

        int? availableModelCount = null;
        string? availableModelsError = null;
        var primaryApiKey = config.Value.ApiKeys.FirstOrDefault() ?? apiKeys.Value.FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(primaryApiKey))
        {
            try
            {
                var models = await _systemService.GetAvailableModelsAsync(primaryApiKey, cancellationToken: cancellationToken);
                availableModelCount = models.Value.Count;
            }
            catch (Exception exception)
            {
                availableModelsError = exception.Message;
            }
        }

        return ManagementResponseFactory.Map(
            config,
            new ManagementOverviewSnapshot
            {
                ManagementApiBaseUrl = connection.ManagementApiBaseUrl,
                ApiKeyCount = apiKeys.Value.Count,
                AuthFileCount = authFiles.Value.Count,
                GeminiKeyCount = geminiKeys.Value.Count,
                CodexKeyCount = codexKeys.Value.Count,
                ClaudeKeyCount = claudeKeys.Value.Count,
                VertexKeyCount = vertexKeys.Value.Count,
                OpenAiCompatibilityCount = openAiProviders.Value.Count,
                AvailableModelCount = availableModelCount,
                AvailableModelsError = availableModelsError,
                Config = config.Value
            });
    }
}
