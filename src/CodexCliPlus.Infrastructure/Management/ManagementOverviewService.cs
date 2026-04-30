using System.Net;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.Infrastructure.Management;

public sealed class ManagementOverviewService : IManagementOverviewService
{
    private static readonly TimeSpan LightweightCacheTtl = TimeSpan.FromSeconds(8);

    private readonly IManagementConnectionProvider _connectionProvider;
    private readonly IManagementConfigurationService _configurationService;
    private readonly IManagementAuthService _authService;
    private readonly IManagementUsageService _usageService;
    private readonly IManagementSystemService _systemService;
    private readonly object _gate = new();

    private CacheEntry<ManagementApiResponse<ManagementShellStatusSnapshot>>? _shellStatusCache;
    private CacheEntry<
        ManagementApiResponse<ManagementSettingsSummarySnapshot>
    >? _settingsSummaryCache;
    private Task<ManagementApiResponse<ManagementShellStatusSnapshot>>? _shellStatusInFlight;
    private Task<
        ManagementApiResponse<ManagementSettingsSummarySnapshot>
    >? _settingsSummaryInFlight;
    private Task<ManagementApiResponse<ManagementOverviewSnapshot>>? _overviewInFlight;

    public ManagementOverviewService(
        IManagementConnectionProvider connectionProvider,
        IManagementConfigurationService configurationService,
        IManagementAuthService authService,
        IManagementUsageService usageService,
        IManagementSystemService systemService
    )
    {
        _connectionProvider = connectionProvider;
        _configurationService = configurationService;
        _authService = authService;
        _usageService = usageService;
        _systemService = systemService;
    }

    public async Task<ManagementApiResponse<ManagementShellStatusSnapshot>> GetShellStatusAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default
    )
    {
        var task = GetOrCreateLightweightTask(
            forceRefresh,
            _shellStatusCache,
            () => _shellStatusInFlight,
            task => _shellStatusInFlight = task,
            BuildShellStatusAsync
        );

        return await task.WaitAsync(cancellationToken);
    }

    public async Task<
        ManagementApiResponse<ManagementSettingsSummarySnapshot>
    > GetSettingsSummaryAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default
    )
    {
        var task = GetOrCreateLightweightTask(
            forceRefresh,
            _settingsSummaryCache,
            () => _settingsSummaryInFlight,
            task => _settingsSummaryInFlight = task,
            BuildSettingsSummaryAsync
        );

        return await task.WaitAsync(cancellationToken);
    }

    public async Task<ManagementApiResponse<ManagementOverviewSnapshot>> GetOverviewAsync(
        CancellationToken cancellationToken = default
    )
    {
        Task<ManagementApiResponse<ManagementOverviewSnapshot>> task;
        lock (_gate)
        {
            if (_overviewInFlight is not null)
            {
                task = _overviewInFlight;
            }
            else
            {
                task = BuildOverviewAsync();
                _overviewInFlight = task;
                _ = ClearInFlightWhenCompleteAsync(task, () => _overviewInFlight = null);
            }
        }

        return await task.WaitAsync(cancellationToken);
    }

    private Task<T> GetOrCreateLightweightTask<T>(
        bool forceRefresh,
        CacheEntry<T>? cache,
        Func<Task<T>?> getInFlight,
        Action<Task<T>?> setInFlight,
        Func<Task<T>> factory
    )
    {
        lock (_gate)
        {
            if (!forceRefresh && cache is not null && cache.IsFresh(LightweightCacheTtl))
            {
                return Task.FromResult(cache.Value);
            }

            if (!forceRefresh && getInFlight() is { } existingTask)
            {
                return existingTask;
            }

            var task = factory();
            setInFlight(task);
            _ = ClearInFlightWhenCompleteAsync(task, () => setInFlight(null));
            return task;
        }
    }

    private async Task<ManagementApiResponse<ManagementShellStatusSnapshot>> BuildShellStatusAsync()
    {
        try
        {
            var connection = await _connectionProvider.GetConnectionAsync();
            var config = await _configurationService.GetConfigAsync();
            var response = ManagementResponseFactory.Map(
                config,
                new ManagementShellStatusSnapshot
                {
                    ManagementApiBaseUrl = connection.ManagementApiBaseUrl,
                    ServerVersion = config.Metadata.Version,
                    IsConnected = true,
                }
            );

            StoreShellStatusCache(response);
            return response;
        }
        catch (Exception exception)
        {
            var response = new ManagementApiResponse<ManagementShellStatusSnapshot>
            {
                Value = new ManagementShellStatusSnapshot
                {
                    IsConnected = false,
                    Error = exception.Message,
                },
                Metadata = new ManagementServerMetadata(),
                StatusCode = HttpStatusCode.ServiceUnavailable,
            };
            StoreShellStatusCache(response);
            return response;
        }
    }

    private async Task<
        ManagementApiResponse<ManagementSettingsSummarySnapshot>
    > BuildSettingsSummaryAsync()
    {
        var connectionTask = _connectionProvider.GetConnectionAsync();
        var configTask = _configurationService.GetConfigAsync();
        var apiKeysTask = _authService.GetApiKeysAsync();
        var authFilesTask = _authService.GetAuthFilesAsync();

        await Task.WhenAll(connectionTask, configTask, apiKeysTask, authFilesTask);

        var connection = await connectionTask;
        var config = await configTask;
        var apiKeys = await apiKeysTask;
        var authFiles = await authFilesTask;

        var response = ManagementResponseFactory.Map(
            config,
            new ManagementSettingsSummarySnapshot
            {
                ManagementApiBaseUrl = connection.ManagementApiBaseUrl,
                ServerVersion = config.Metadata.Version,
                ApiKeyCount = apiKeys.Value.Count,
                AuthFileCount = authFiles.Value.Count,
                GeminiKeyCount = config.Value.GeminiApiKeys.Count,
                CodexKeyCount = config.Value.CodexApiKeys.Count,
                ClaudeKeyCount = config.Value.ClaudeApiKeys.Count,
                VertexKeyCount = config.Value.VertexApiKeys.Count,
                OpenAiCompatibilityCount = config.Value.OpenAiCompatibility.Count,
                Config = config.Value,
            }
        );

        StoreSettingsSummaryCache(response);
        return response;
    }

    private async Task<ManagementApiResponse<ManagementOverviewSnapshot>> BuildOverviewAsync()
    {
        var cancellationToken = CancellationToken.None;
        var connectionTask = _connectionProvider.GetConnectionAsync(cancellationToken);
        var configTask = _configurationService.GetConfigAsync(cancellationToken);
        var usageTask = _usageService.GetUsageAsync(cancellationToken);
        var apiKeysTask = _authService.GetApiKeysAsync(cancellationToken);
        var authFilesTask = _authService.GetAuthFilesAsync(cancellationToken);

        await Task.WhenAll(
            connectionTask,
            configTask,
            usageTask,
            apiKeysTask,
            authFilesTask
        );

        var connection = await connectionTask;
        var config = await configTask;
        var usage = await usageTask;
        var apiKeys = await apiKeysTask;
        var authFiles = await authFilesTask;

        string? latestVersion = null;
        string? latestVersionError = null;
        try
        {
            var latestVersionResponse = await _systemService.GetLatestVersionAsync(
                cancellationToken
            );
            latestVersion = latestVersionResponse.Value.LatestVersion;
        }
        catch (Exception exception)
        {
            latestVersionError = exception.Message;
        }

        int? availableModelCount = null;
        string? availableModelsError = null;
        var primaryApiKey =
            config.Value.ApiKeys.Count > 0 ? config.Value.ApiKeys[0]
            : apiKeys.Value.Count > 0 ? apiKeys.Value[0]
            : null;

        if (!string.IsNullOrWhiteSpace(primaryApiKey))
        {
            try
            {
                var models = await _systemService.GetAvailableModelsAsync(
                    primaryApiKey,
                    cancellationToken: cancellationToken
                );
                availableModelCount = models.Value.Count;
            }
            catch (Exception exception)
            {
                availableModelsError = exception.Message;
            }
        }

        var response = ManagementResponseFactory.Map(
            config,
            new ManagementOverviewSnapshot
            {
                ManagementApiBaseUrl = connection.ManagementApiBaseUrl,
                ServerVersion = config.Metadata.Version,
                LatestVersion = latestVersion,
                LatestVersionError = latestVersionError,
                ApiKeyCount = apiKeys.Value.Count,
                AuthFileCount = authFiles.Value.Count,
                GeminiKeyCount = config.Value.GeminiApiKeys.Count,
                CodexKeyCount = config.Value.CodexApiKeys.Count,
                ClaudeKeyCount = config.Value.ClaudeApiKeys.Count,
                VertexKeyCount = config.Value.VertexApiKeys.Count,
                OpenAiCompatibilityCount = config.Value.OpenAiCompatibility.Count,
                AvailableModelCount = availableModelCount,
                AvailableModelsError = availableModelsError,
                Config = config.Value,
                Usage = usage.Value,
            }
        );

        StoreShellStatusCache(
            ManagementResponseFactory.Map(
                config,
                new ManagementShellStatusSnapshot
                {
                    ManagementApiBaseUrl = connection.ManagementApiBaseUrl,
                    ServerVersion = config.Metadata.Version,
                    IsConnected = true,
                }
            )
        );
        StoreSettingsSummaryCache(
            ManagementResponseFactory.Map(
                config,
                new ManagementSettingsSummarySnapshot
                {
                    ManagementApiBaseUrl = connection.ManagementApiBaseUrl,
                    ServerVersion = config.Metadata.Version,
                    ApiKeyCount = apiKeys.Value.Count,
                    AuthFileCount = authFiles.Value.Count,
                    GeminiKeyCount = config.Value.GeminiApiKeys.Count,
                    CodexKeyCount = config.Value.CodexApiKeys.Count,
                    ClaudeKeyCount = config.Value.ClaudeApiKeys.Count,
                    VertexKeyCount = config.Value.VertexApiKeys.Count,
                    OpenAiCompatibilityCount = config.Value.OpenAiCompatibility.Count,
                    Config = config.Value,
                }
            )
        );

        return response;
    }

    private async Task ClearInFlightWhenCompleteAsync<T>(Task<T> task, Action clear)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch { }
        finally
        {
            lock (_gate)
            {
                clear();
            }
        }
    }

    private void StoreShellStatusCache(
        ManagementApiResponse<ManagementShellStatusSnapshot> response
    )
    {
        lock (_gate)
        {
            _shellStatusCache = CacheEntry<
                ManagementApiResponse<ManagementShellStatusSnapshot>
            >.Create(response);
        }
    }

    private void StoreSettingsSummaryCache(
        ManagementApiResponse<ManagementSettingsSummarySnapshot> response
    )
    {
        lock (_gate)
        {
            _settingsSummaryCache = CacheEntry<
                ManagementApiResponse<ManagementSettingsSummarySnapshot>
            >.Create(response);
        }
    }

    private sealed record CacheEntry<T>(T Value, DateTimeOffset CachedAt)
    {
        public bool IsFresh(TimeSpan ttl) => DateTimeOffset.UtcNow - CachedAt < ttl;

        public static CacheEntry<T> Create(T value) => new(value, DateTimeOffset.UtcNow);
    }
}
