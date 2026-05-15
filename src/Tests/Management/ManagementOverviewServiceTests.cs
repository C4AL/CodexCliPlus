using System.Net;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Infrastructure.Management;

namespace CodexCliPlus.Tests.Management;

[Trait("Category", "Fast")]
public sealed class ManagementOverviewServiceTests
{
    [Fact]
    public async Task GetShellStatusAsyncUsesOnlyConnectionAndConfig()
    {
        var services = CreateServices();

        var response = await services.Overview.GetShellStatusAsync();

        Assert.True(response.Value.IsConnected);
        Assert.Equal("http://127.0.0.1:1327/v0/management", response.Value.ManagementApiBaseUrl);
        Assert.Equal("9.9.9", response.Value.ServerVersion);
        Assert.Equal(1, services.Connection.Calls);
        Assert.Equal(1, services.Configuration.GetConfigCalls);
        Assert.Equal(0, services.Auth.GetApiKeysCalls);
        Assert.Equal(0, services.Auth.GetAuthFilesCalls);
        Assert.Equal(0, services.Auth.ProviderCalls);
    }

    [Fact]
    public async Task GetShellStatusAsyncStartsConnectionAndConfigConcurrently()
    {
        var connection = new BlockingConnectionProvider();
        var configuration = new BlockingConfigurationService();
        var overview = new ManagementOverviewService(connection, configuration, new FakeAuthService());

        var responseTask = overview.GetShellStatusAsync();

        try
        {
            await Task.WhenAll(connection.WaitForStartAsync(), configuration.WaitForStartAsync())
                .WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            connection.Complete();
            configuration.Complete();
        }

        var response = await responseTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(response.Value.IsConnected);
        Assert.Equal(1, connection.Calls);
        Assert.Equal(1, configuration.GetConfigCalls);
    }

    [Fact]
    public async Task GetShellStatusAsyncPropagatesPreCanceledTokenBeforeStartingRefresh()
    {
        var connection = new BlockingConnectionProvider();
        var configuration = new BlockingConfigurationService();
        var overview = new ManagementOverviewService(connection, configuration, new FakeAuthService());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Exception? exception;
        try
        {
            exception = await Record.ExceptionAsync(async () =>
                await overview.GetShellStatusAsync(cancellationToken: cancellation.Token)
            );
        }
        finally
        {
            connection.Complete();
            configuration.Complete();
        }

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(0, connection.Calls);
        Assert.Equal(0, configuration.GetConfigCalls);
    }

    [Fact]
    public async Task GetSettingsSummaryAsyncUsesConfigApiKeysAndSkipsProviderFanOut()
    {
        var services = CreateServices();

        var response = await services.Overview.GetSettingsSummaryAsync();

        Assert.Equal(2, response.Value.ApiKeyCount);
        Assert.Equal(1, response.Value.AuthFileCount);
        Assert.Equal(1, response.Value.GeminiKeyCount);
        Assert.Equal(1, response.Value.CodexKeyCount);
        Assert.Equal(1, response.Value.ClaudeKeyCount);
        Assert.Equal(1, response.Value.VertexKeyCount);
        Assert.Equal(1, response.Value.OpenAiCompatibilityCount);
        Assert.Equal(0, services.Auth.GetApiKeysCalls);
        Assert.Equal(1, services.Auth.GetAuthFilesCalls);
        Assert.Equal(0, services.Auth.ProviderCalls);
    }

    [Fact]
    public async Task GetSettingsSummaryAsyncPropagatesPreCanceledTokenBeforeStartingRefresh()
    {
        var connection = new BlockingConnectionProvider();
        var configuration = new BlockingConfigurationService();
        var auth = new FakeAuthService();
        var overview = new ManagementOverviewService(connection, configuration, auth);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Exception? exception;
        try
        {
            exception = await Record.ExceptionAsync(async () =>
                await overview.GetSettingsSummaryAsync(cancellationToken: cancellation.Token)
            );
        }
        finally
        {
            connection.Complete();
            configuration.Complete();
        }

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(0, connection.Calls);
        Assert.Equal(0, configuration.GetConfigCalls);
        Assert.Equal(0, auth.GetAuthFilesCalls);
    }

    [Fact]
    public async Task GetSettingsSummaryAsyncKeepsNewerInFlightRequestAfterOlderFailure()
    {
        var connection = new FakeConnectionProvider();
        var configuration = new SequencedConfigurationService();
        var auth = new FakeAuthService();
        var overview = new ManagementOverviewService(connection, configuration, auth);

        var first = overview.GetSettingsSummaryAsync();
        await configuration.WaitForCallCountAsync(1);

        var refresh = overview.GetSettingsSummaryAsync(forceRefresh: true);
        await configuration.WaitForCallCountAsync(2);

        configuration.FailCall(0, new InvalidOperationException("stale config failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await Task.Delay(50);

        var coalesced = overview.GetSettingsSummaryAsync();
        await Task.Delay(50);

        Assert.Equal(2, configuration.GetConfigCalls);

        configuration.CompleteCall(1);
        var refreshResult = await refresh;
        var coalescedResult = await coalesced;

        Assert.Equal(2, configuration.GetConfigCalls);
        Assert.Equal(refreshResult.Value.ApiKeyCount, coalescedResult.Value.ApiKeyCount);
    }

    [Fact]
    public async Task GetSettingsSummaryAsyncKeepsNewerCacheWhenOlderRequestCompletesLast()
    {
        var connection = new FakeConnectionProvider();
        var configuration = new SequencedConfigurationService();
        var auth = new FakeAuthService();
        var overview = new ManagementOverviewService(connection, configuration, auth);

        var first = overview.GetSettingsSummaryAsync();
        await configuration.WaitForCallCountAsync(1);

        var refresh = overview.GetSettingsSummaryAsync(forceRefresh: true);
        await configuration.WaitForCallCountAsync(2);

        configuration.CompleteCall(1, "fresh-version");
        var refreshResult = await refresh;

        configuration.CompleteCall(0, "stale-version");
        var firstResult = await first;

        var cached = await overview.GetSettingsSummaryAsync();

        Assert.Equal("fresh-version", refreshResult.Value.ServerVersion);
        Assert.Equal("stale-version", firstResult.Value.ServerVersion);
        Assert.Equal("fresh-version", cached.Value.ServerVersion);
        Assert.Equal(2, configuration.GetConfigCalls);
    }

    private static TestServices CreateServices()
    {
        var connection = new FakeConnectionProvider();
        var configuration = new FakeConfigurationService();
        var auth = new FakeAuthService();
        var overview = new ManagementOverviewService(connection, configuration, auth);

        return new TestServices(connection, configuration, auth, overview);
    }

    private sealed record TestServices(
        FakeConnectionProvider Connection,
        FakeConfigurationService Configuration,
        FakeAuthService Auth,
        ManagementOverviewService Overview
    );

    private sealed class FakeConnectionProvider : IManagementConnectionProvider
    {
        public int Calls { get; private set; }

        public Task<ManagementConnectionInfo> GetConnectionAsync(
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            return Task.FromResult(
                new ManagementConnectionInfo
                {
                    BaseUrl = "http://127.0.0.1:1327",
                    ManagementApiBaseUrl = "http://127.0.0.1:1327/v0/management",
                    ManagementKey = "secret",
                }
            );
        }
    }

    private sealed class BlockingConnectionProvider : IManagementConnectionProvider
    {
        private readonly TaskCompletionSource<object?> _started = CreateSignal();
        private readonly TaskCompletionSource<object?> _complete = CreateSignal();

        public int Calls { get; private set; }

        public Task<object?> WaitForStartAsync() => _started.Task;

        public void Complete() => _complete.TrySetResult(null);

        public async Task<ManagementConnectionInfo> GetConnectionAsync(
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            _started.TrySetResult(null);
            await _complete.Task.WaitAsync(cancellationToken);
            return new ManagementConnectionInfo
            {
                BaseUrl = "http://127.0.0.1:1327",
                ManagementApiBaseUrl = "http://127.0.0.1:1327/v0/management",
                ManagementKey = "secret",
            };
        }
    }

    private sealed class FakeConfigurationService : IManagementConfigurationService
    {
        public int GetConfigCalls { get; private set; }

        public Task<ManagementApiResponse<ManagementConfigSnapshot>> GetConfigAsync(
            CancellationToken cancellationToken = default
        )
        {
            GetConfigCalls++;
            return Task.FromResult(
                Response(
                    new ManagementConfigSnapshot
                    {
                        ApiKeys = ["sk-a", "sk-b"],
                        GeminiApiKeys = [new ManagementGeminiKeyConfiguration()],
                        CodexApiKeys = [new ManagementProviderKeyConfiguration()],
                        ClaudeApiKeys = [new ManagementProviderKeyConfiguration()],
                        VertexApiKeys = [new ManagementProviderKeyConfiguration()],
                        OpenAiCompatibility = [new ManagementOpenAiCompatibilityEntry()],
                    }
                )
            );
        }

        public Task<ManagementApiResponse<string>> GetConfigYamlAsync(
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> PutConfigYamlAsync(
            string yamlContent,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateBooleanSettingAsync(
            string path,
            bool value,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateIntegerSettingAsync(
            string path,
            int value,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateStringSettingAsync(
            string path,
            string value,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> DeleteSettingAsync(
            string path,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class BlockingConfigurationService : IManagementConfigurationService
    {
        private readonly TaskCompletionSource<object?> _started = CreateSignal();
        private readonly TaskCompletionSource<object?> _complete = CreateSignal();

        public int GetConfigCalls { get; private set; }

        public Task<object?> WaitForStartAsync() => _started.Task;

        public void Complete() => _complete.TrySetResult(null);

        public async Task<ManagementApiResponse<ManagementConfigSnapshot>> GetConfigAsync(
            CancellationToken cancellationToken = default
        )
        {
            GetConfigCalls++;
            _started.TrySetResult(null);
            await _complete.Task.WaitAsync(cancellationToken);
            return Response(CreateConfigSnapshot());
        }

        public Task<ManagementApiResponse<string>> GetConfigYamlAsync(
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> PutConfigYamlAsync(
            string yamlContent,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateBooleanSettingAsync(
            string path,
            bool value,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateIntegerSettingAsync(
            string path,
            int value,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateStringSettingAsync(
            string path,
            string value,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> DeleteSettingAsync(
            string path,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class SequencedConfigurationService : IManagementConfigurationService
    {
        private readonly object _gate = new();
        private readonly List<
            TaskCompletionSource<ManagementApiResponse<ManagementConfigSnapshot>>
        > _calls = [];
        private TaskCompletionSource<object?> _callArrived = CreateSignal();

        public int GetConfigCalls
        {
            get
            {
                lock (_gate)
                {
                    return _calls.Count;
                }
            }
        }

        public Task<ManagementApiResponse<ManagementConfigSnapshot>> GetConfigAsync(
            CancellationToken cancellationToken = default
        )
        {
            lock (_gate)
            {
                var call = new TaskCompletionSource<
                    ManagementApiResponse<ManagementConfigSnapshot>
                >(TaskCreationOptions.RunContinuationsAsynchronously);
                _calls.Add(call);
                _callArrived.TrySetResult(null);
                _callArrived = CreateSignal();
                return call.Task.WaitAsync(cancellationToken);
            }
        }

        public async Task WaitForCallCountAsync(int expectedCallCount)
        {
            while (true)
            {
                Task waitTask;
                lock (_gate)
                {
                    if (_calls.Count >= expectedCallCount)
                    {
                        return;
                    }

                    waitTask = _callArrived.Task;
                }

                await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
        }

        public void CompleteCall(int index, string version = "9.9.9")
        {
            GetCall(index).SetResult(Response(CreateConfigSnapshot(), version));
        }

        public void FailCall(int index, Exception exception)
        {
            GetCall(index).SetException(exception);
        }

        private TaskCompletionSource<ManagementApiResponse<ManagementConfigSnapshot>> GetCall(
            int index
        )
        {
            lock (_gate)
            {
                return _calls[index];
            }
        }

        public Task<ManagementApiResponse<string>> GetConfigYamlAsync(
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> PutConfigYamlAsync(
            string yamlContent,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateBooleanSettingAsync(
            string path,
            bool value,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateIntegerSettingAsync(
            string path,
            int value,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateStringSettingAsync(
            string path,
            string value,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> DeleteSettingAsync(
            string path,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakeAuthService : IManagementAuthService
    {
        public int GetApiKeysCalls { get; private set; }
        public int GetAuthFilesCalls { get; private set; }
        public int ProviderCalls { get; private set; }

        public Task<ManagementApiResponse<IReadOnlyList<string>>> GetApiKeysAsync(
            CancellationToken cancellationToken = default
        )
        {
            GetApiKeysCalls++;
            return Task.FromResult(Response<IReadOnlyList<string>>(["sk-a", "sk-b"]));
        }

        public Task<ManagementApiResponse<IReadOnlyList<ManagementAuthFileItem>>> GetAuthFilesAsync(
            CancellationToken cancellationToken = default
        )
        {
            GetAuthFilesCalls++;
            return Task.FromResult(
                Response<IReadOnlyList<ManagementAuthFileItem>>([new ManagementAuthFileItem()])
            );
        }

        public Task<
            ManagementApiResponse<IReadOnlyList<ManagementGeminiKeyConfiguration>>
        > GetGeminiKeysAsync(CancellationToken cancellationToken = default)
        {
            ProviderCalls++;
            return Task.FromResult(
                Response<IReadOnlyList<ManagementGeminiKeyConfiguration>>([
                    new ManagementGeminiKeyConfiguration(),
                ])
            );
        }

        public Task<
            ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>
        > GetCodexKeysAsync(CancellationToken cancellationToken = default)
        {
            ProviderCalls++;
            return Task.FromResult(
                Response<IReadOnlyList<ManagementProviderKeyConfiguration>>([
                    new ManagementProviderKeyConfiguration(),
                ])
            );
        }

        public Task<
            ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>
        > GetClaudeKeysAsync(CancellationToken cancellationToken = default)
        {
            ProviderCalls++;
            return Task.FromResult(
                Response<IReadOnlyList<ManagementProviderKeyConfiguration>>([
                    new ManagementProviderKeyConfiguration(),
                ])
            );
        }

        public Task<
            ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>
        > GetVertexKeysAsync(CancellationToken cancellationToken = default)
        {
            ProviderCalls++;
            return Task.FromResult(
                Response<IReadOnlyList<ManagementProviderKeyConfiguration>>([
                    new ManagementProviderKeyConfiguration(),
                ])
            );
        }

        public Task<
            ManagementApiResponse<IReadOnlyList<ManagementOpenAiCompatibilityEntry>>
        > GetOpenAiCompatibilityAsync(CancellationToken cancellationToken = default)
        {
            ProviderCalls++;
            return Task.FromResult(
                Response<IReadOnlyList<ManagementOpenAiCompatibilityEntry>>([
                    new ManagementOpenAiCompatibilityEntry(),
                ])
            );
        }

        public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceApiKeysAsync(
            IReadOnlyList<string> apiKeys,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateApiKeyAsync(
            int index,
            string value,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> DeleteApiKeyAsync(
            int index,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UploadAuthFileAsync(
            string fileName,
            string jsonContent,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UploadAuthFilesAsync(
            IReadOnlyList<ManagementAuthFileUpload> files,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> DeleteAuthFileAsync(
            string name,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> DeleteAuthFilesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> DeleteAllAuthFilesAsync(
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<string>> DownloadAuthFileAsync(
            string name,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> SetAuthFileDisabledAsync(
            string name,
            bool disabled,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> PatchAuthFileFieldsAsync(
            ManagementAuthFileFieldPatch patch,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<
            ManagementApiResponse<IReadOnlyList<ManagementModelDescriptor>>
        > GetAuthFileModelsAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<
            ManagementApiResponse<IReadOnlyList<ManagementModelDescriptor>>
        > GetModelDefinitionsAsync(string channel, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceGeminiKeysAsync(
            IReadOnlyList<ManagementGeminiKeyConfiguration> configurations,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateGeminiKeyAsync(
            int index,
            ManagementGeminiKeyConfiguration configuration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> DeleteGeminiKeyAsync(
            string apiKey,
            string? baseUrl = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceCodexKeysAsync(
            IReadOnlyList<ManagementProviderKeyConfiguration> configurations,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateCodexKeyAsync(
            int index,
            ManagementProviderKeyConfiguration configuration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> DeleteCodexKeyAsync(
            string apiKey,
            string? baseUrl = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceClaudeKeysAsync(
            IReadOnlyList<ManagementProviderKeyConfiguration> configurations,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateClaudeKeyAsync(
            int index,
            ManagementProviderKeyConfiguration configuration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> DeleteClaudeKeyAsync(
            string apiKey,
            string? baseUrl = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceVertexKeysAsync(
            IReadOnlyList<ManagementProviderKeyConfiguration> configurations,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateVertexKeyAsync(
            int index,
            ManagementProviderKeyConfiguration configuration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> DeleteVertexKeyAsync(
            string apiKey,
            string? baseUrl = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<
            ManagementApiResponse<ManagementOperationResult>
        > ReplaceOpenAiCompatibilityAsync(
            IReadOnlyList<ManagementOpenAiCompatibilityEntry> providers,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<
            ManagementApiResponse<ManagementOperationResult>
        > UpdateOpenAiCompatibilityAsync(
            int index,
            ManagementOpenAiCompatibilityEntry provider,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<
            ManagementApiResponse<ManagementOperationResult>
        > DeleteOpenAiCompatibilityAsync(
            string name,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementAmpCodeConfiguration>> GetAmpCodeAsync(
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateAmpUpstreamUrlAsync(
            string? upstreamUrl,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateAmpUpstreamApiKeyAsync(
            string? upstreamApiKey,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<
            ManagementApiResponse<IReadOnlyList<ManagementAmpCodeUpstreamApiKeyMapping>>
        > GetAmpUpstreamApiKeysAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<
            ManagementApiResponse<ManagementOperationResult>
        > ReplaceAmpUpstreamApiKeysAsync(
            IReadOnlyList<ManagementAmpCodeUpstreamApiKeyMapping> mappings,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<
            ManagementApiResponse<IReadOnlyList<ManagementAmpCodeModelMapping>>
        > GetAmpModelMappingsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceAmpModelMappingsAsync(
            IReadOnlyList<ManagementAmpCodeModelMapping> mappings,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> SetAmpForceModelMappingsAsync(
            bool enabled,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<
            ManagementApiResponse<IReadOnlyDictionary<string, IReadOnlyList<string>>>
        > GetOAuthExcludedModelsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<
            ManagementApiResponse<ManagementOperationResult>
        > ReplaceOAuthExcludedModelsAsync(
            IReadOnlyDictionary<string, IReadOnlyList<string>> excludedModels,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<
            ManagementApiResponse<ManagementOperationResult>
        > UpdateOAuthExcludedModelsAsync(
            string provider,
            IReadOnlyList<string> models,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<
            ManagementApiResponse<ManagementOperationResult>
        > DeleteOAuthExcludedModelsAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<
            ManagementApiResponse<
                IReadOnlyDictionary<string, IReadOnlyList<ManagementOAuthModelAliasEntry>>
            >
        > GetOAuthModelAliasesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceOAuthModelAliasesAsync(
            IReadOnlyDictionary<string, IReadOnlyList<ManagementOAuthModelAliasEntry>> aliases,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateOAuthModelAliasAsync(
            string channel,
            IReadOnlyList<ManagementOAuthModelAliasEntry> aliases,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> DeleteOAuthModelAliasAsync(
            string channel,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOAuthStartResponse>> GetOAuthStartAsync(
            string provider,
            string? projectId = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOAuthStatus>> GetOAuthStatusAsync(
            string state,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<ManagementOperationResult>> SubmitOAuthCallbackAsync(
            string provider,
            string redirectUrl,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private static ManagementConfigSnapshot CreateConfigSnapshot()
    {
        return new ManagementConfigSnapshot
        {
            ApiKeys = ["sk-a", "sk-b"],
            GeminiApiKeys = [new ManagementGeminiKeyConfiguration()],
            CodexApiKeys = [new ManagementProviderKeyConfiguration()],
            ClaudeApiKeys = [new ManagementProviderKeyConfiguration()],
            VertexApiKeys = [new ManagementProviderKeyConfiguration()],
            OpenAiCompatibility = [new ManagementOpenAiCompatibilityEntry()],
        };
    }

    private static TaskCompletionSource<object?> CreateSignal()
    {
        return new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
    }

    private static ManagementApiResponse<T> Response<T>(T value, string version = "9.9.9")
    {
        return new ManagementApiResponse<T>
        {
            Value = value,
            Metadata = new ManagementServerMetadata { Version = version },
            StatusCode = HttpStatusCode.OK,
        };
    }
}
