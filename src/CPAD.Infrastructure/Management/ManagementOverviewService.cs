using CPAD.Core.Abstractions.Management;
using CPAD.Core.Models.Management;

namespace CPAD.Infrastructure.Management;

public sealed class ManagementOverviewService : IManagementOverviewService
{
    private readonly IManagementConnectionProvider _connectionProvider;
    private readonly IManagementConfigurationService _configurationService;
    private readonly IManagementAuthService _authService;
    private readonly IManagementUsageService _usageService;
    private readonly IManagementSystemService _systemService;

    public ManagementOverviewService(
        IManagementConnectionProvider connectionProvider,
        IManagementConfigurationService configurationService,
        IManagementAuthService authService,
        IManagementUsageService usageService,
        IManagementSystemService systemService)
    {
        _connectionProvider = connectionProvider;
        _configurationService = configurationService;
        _authService = authService;
        _usageService = usageService;
        _systemService = systemService;
    }

    public async Task<ManagementApiResponse<ManagementOverviewSnapshot>> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var connectionTask = _connectionProvider.GetConnectionAsync(cancellationToken);
        var configTask = _configurationService.GetConfigAsync(cancellationToken);
        var usageTask = _usageService.GetUsageAsync(cancellationToken);
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
            usageTask,
            apiKeysTask,
            authFilesTask,
            geminiTask,
            codexTask,
            claudeTask,
            vertexTask,
            openAiTask);

        var connection = await connectionTask;
        var config = await configTask;
        var usage = await usageTask;
        var apiKeys = await apiKeysTask;
        var authFiles = await authFilesTask;
        var geminiKeys = await geminiTask;
        var codexKeys = await codexTask;
        var claudeKeys = await claudeTask;
        var vertexKeys = await vertexTask;
        var openAiProviders = await openAiTask;

        string? latestVersion = null;
        string? latestVersionError = null;
        try
        {
            var latestVersionResponse = await _systemService.GetLatestVersionAsync(cancellationToken);
            latestVersion = latestVersionResponse.Value.LatestVersion;
        }
        catch (Exception exception)
        {
            latestVersionError = exception.Message;
        }

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
                ServerVersion = config.Metadata.Version,
                LatestVersion = latestVersion,
                LatestVersionError = latestVersionError,
                ApiKeyCount = apiKeys.Value.Count,
                AuthFileCount = authFiles.Value.Count,
                GeminiKeyCount = geminiKeys.Value.Count,
                CodexKeyCount = codexKeys.Value.Count,
                ClaudeKeyCount = claudeKeys.Value.Count,
                VertexKeyCount = vertexKeys.Value.Count,
                OpenAiCompatibilityCount = openAiProviders.Value.Count,
                AvailableModelCount = availableModelCount,
                AvailableModelsError = availableModelsError,
                Config = config.Value,
                Usage = usage.Value
            });
    }
}
