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
    public async Task GetSettingsSummaryAsyncSkipsUsageModelsAndProviderFanOut()
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
        Assert.Equal(1, services.Auth.GetApiKeysCalls);
        Assert.Equal(1, services.Auth.GetAuthFilesCalls);
        Assert.Equal(0, services.Auth.ProviderCalls);
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

    private static ManagementApiResponse<T> Response<T>(T value)
    {
        return new ManagementApiResponse<T>
        {
            Value = value,
            Metadata = new ManagementServerMetadata { Version = "9.9.9" },
            StatusCode = HttpStatusCode.OK,
        };
    }
}
