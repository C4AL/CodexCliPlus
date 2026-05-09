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
    private readonly object _gate = new();

    private CacheEntry<ManagementApiResponse<ManagementShellStatusSnapshot>>? _shellStatusCache;
    private CacheEntry<
        ManagementApiResponse<ManagementSettingsSummarySnapshot>
    >? _settingsSummaryCache;
    private Task<ManagementApiResponse<ManagementShellStatusSnapshot>>? _shellStatusInFlight;
    private Task<
        ManagementApiResponse<ManagementSettingsSummarySnapshot>
    >? _settingsSummaryInFlight;
    private long _shellStatusGeneration;
    private long _settingsSummaryGeneration;

    public ManagementOverviewService(
        IManagementConnectionProvider connectionProvider,
        IManagementConfigurationService configurationService,
        IManagementAuthService authService
    )
    {
        _connectionProvider = connectionProvider;
        _configurationService = configurationService;
        _authService = authService;
    }

    public async Task<ManagementApiResponse<ManagementShellStatusSnapshot>> GetShellStatusAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default
    )
    {
        var task = GetOrCreateLightweightTask(
            forceRefresh,
            () => _shellStatusCache,
            () => _shellStatusInFlight,
            task => _shellStatusInFlight = task,
            BuildShellStatusAsync,
            () => ++_shellStatusGeneration,
            () => _shellStatusGeneration,
            StoreShellStatusCache
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
            () => _settingsSummaryCache,
            () => _settingsSummaryInFlight,
            task => _settingsSummaryInFlight = task,
            BuildSettingsSummaryAsync,
            () => ++_settingsSummaryGeneration,
            () => _settingsSummaryGeneration,
            StoreSettingsSummaryCache
        );

        return await task.WaitAsync(cancellationToken);
    }

    private Task<T> GetOrCreateLightweightTask<T>(
        bool forceRefresh,
        Func<CacheEntry<T>?> getCache,
        Func<Task<T>?> getInFlight,
        Action<Task<T>?> setInFlight,
        Func<Task<T>> factory,
        Func<long> nextGeneration,
        Func<long> getGeneration,
        Action<T> storeCache
    )
    {
        lock (_gate)
        {
            var cache = getCache();
            if (!forceRefresh && cache is not null && cache.IsFresh(LightweightCacheTtl))
            {
                return Task.FromResult(cache.Value);
            }

            if (!forceRefresh && getInFlight() is { } existingTask)
            {
                return existingTask;
            }

            var generation = nextGeneration();
            var task = RunLightweightTaskAsync(factory, generation, getGeneration, storeCache);
            setInFlight(task);
            _ = ClearInFlightWhenCompleteAsync(task, getInFlight, setInFlight);
            return task;
        }
    }

    private async Task<T> RunLightweightTaskAsync<T>(
        Func<Task<T>> factory,
        long generation,
        Func<long> getGeneration,
        Action<T> storeCache
    )
    {
        var value = await factory().ConfigureAwait(false);
        lock (_gate)
        {
            if (getGeneration() == generation)
            {
                storeCache(value);
            }
        }

        return value;
    }

    private async Task<ManagementApiResponse<ManagementShellStatusSnapshot>> BuildShellStatusAsync()
    {
        try
        {
            var connectionTask = _connectionProvider.GetConnectionAsync();
            var configTask = _configurationService.GetConfigAsync();

            await Task.WhenAll(connectionTask, configTask).ConfigureAwait(false);

            var connection = await connectionTask.ConfigureAwait(false);
            var config = await configTask.ConfigureAwait(false);
            var response = ManagementResponseFactory.Map(
                config,
                new ManagementShellStatusSnapshot
                {
                    ManagementApiBaseUrl = connection.ManagementApiBaseUrl,
                    ServerVersion = config.Metadata.Version,
                    IsConnected = true,
                }
            );

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
            return response;
        }
    }

    private async Task<
        ManagementApiResponse<ManagementSettingsSummarySnapshot>
    > BuildSettingsSummaryAsync()
    {
        var connectionTask = _connectionProvider.GetConnectionAsync();
        var configTask = _configurationService.GetConfigAsync();
        var authFilesTask = _authService.GetAuthFilesAsync();

        await Task.WhenAll(connectionTask, configTask, authFilesTask);

        var connection = await connectionTask;
        var config = await configTask;
        var authFiles = await authFilesTask;

        var response = ManagementResponseFactory.Map(
            config,
            new ManagementSettingsSummarySnapshot
            {
                ManagementApiBaseUrl = connection.ManagementApiBaseUrl,
                ServerVersion = config.Metadata.Version,
                ApiKeyCount = config.Value.ApiKeys.Count,
                AuthFileCount = authFiles.Value.Count,
                GeminiKeyCount = config.Value.GeminiApiKeys.Count,
                CodexKeyCount = config.Value.CodexApiKeys.Count,
                ClaudeKeyCount = config.Value.ClaudeApiKeys.Count,
                VertexKeyCount = config.Value.VertexApiKeys.Count,
                OpenAiCompatibilityCount = config.Value.OpenAiCompatibility.Count,
                Config = config.Value,
            }
        );

        return response;
    }

    private async Task ClearInFlightWhenCompleteAsync<T>(
        Task<T> task,
        Func<Task<T>?> getInFlight,
        Action<Task<T>?> setInFlight
    )
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
                if (ReferenceEquals(getInFlight(), task))
                {
                    setInFlight(null);
                }
            }
        }
    }

    private void StoreShellStatusCache(
        ManagementApiResponse<ManagementShellStatusSnapshot> response
    )
    {
        _shellStatusCache = CacheEntry<
            ManagementApiResponse<ManagementShellStatusSnapshot>
        >.Create(response);
    }

    private void StoreSettingsSummaryCache(
        ManagementApiResponse<ManagementSettingsSummarySnapshot> response
    )
    {
        _settingsSummaryCache = CacheEntry<
            ManagementApiResponse<ManagementSettingsSummarySnapshot>
        >.Create(response);
    }

    private sealed record CacheEntry<T>(T Value, DateTimeOffset CachedAt)
    {
        public bool IsFresh(TimeSpan ttl) => DateTimeOffset.UtcNow - CachedAt < ttl;

        public static CacheEntry<T> Create(T value) => new(value, DateTimeOffset.UtcNow);
    }
}
