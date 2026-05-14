using System.Net;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Infrastructure.Management;

namespace CodexCliPlus.Tests.Management;

[Trait("Category", "Fast")]
public sealed class ManagementConfigurationServiceTests
{
    [Fact]
    public async Task GetConfigAsyncMapsRedisUsageQueueRetentionSeconds()
    {
        var client = new FixedManagementApiClient(
            """{"usage-statistics-enabled":true,"redis-usage-queue-retention-seconds":120}"""
        );
        var service = new ManagementConfigurationService(client);

        var response = await service.GetConfigAsync();

        Assert.Equal(HttpMethod.Get, client.LastMethod);
        Assert.Equal("config", client.LastPath);
        Assert.True(response.Value.UsageStatisticsEnabled);
        Assert.Equal(120, response.Value.RedisUsageQueueRetentionSeconds);
    }

    [Fact]
    public async Task GetConfigAsyncMapsProviderListsFromCamelCasePayload()
    {
        var client = new FixedManagementApiClient(
            """
            {
              "geminiApiKeys":[{"apiKey":"gemini-key","baseUrl":"https://gemini.example"}],
              "codexApiKeys":[{"apiKey":"codex-key","models":[{"name":"gpt-5","alias":"gpt-5-chat"}]}],
              "claudeApiKeys":[{"apiKey":"claude-key"}],
              "vertexApiKeys":[{"apiKey":"vertex-key"}],
              "openaiCompatibility":[{"name":"compat","baseUrl":"https://compat.example","apiKeyEntries":[{"apiKey":"compat-key","proxyUrl":"http://proxy.example"}]}]
            }
            """
        );
        var service = new ManagementConfigurationService(client);

        var response = await service.GetConfigAsync();

        Assert.Equal("gemini-key", Assert.Single(response.Value.GeminiApiKeys).ApiKey);

        var codex = Assert.Single(response.Value.CodexApiKeys);
        Assert.Equal("codex-key", codex.ApiKey);
        var model = Assert.Single(codex.Models);
        Assert.Equal("gpt-5", model.Name);
        Assert.Equal("gpt-5-chat", model.Alias);

        Assert.Equal("claude-key", Assert.Single(response.Value.ClaudeApiKeys).ApiKey);
        Assert.Equal("vertex-key", Assert.Single(response.Value.VertexApiKeys).ApiKey);

        var compatibility = Assert.Single(response.Value.OpenAiCompatibility);
        Assert.Equal("compat", compatibility.Name);
        Assert.Equal("https://compat.example", compatibility.BaseUrl);
        var compatibilityKey = Assert.Single(compatibility.ApiKeyEntries);
        Assert.Equal("compat-key", compatibilityKey.ApiKey);
        Assert.Equal("http://proxy.example", compatibilityKey.ProxyUrl);
    }

    [Fact]
    public async Task GetConfigAsyncMapsOpenAiCompatibilityApiKeysFallback()
    {
        var client = new FixedManagementApiClient(
            """
            {
              "openai-compatibility":[
                {"name":"legacy","base-url":"https://legacy.example","api-keys":["sk-one"," ","sk-two"]},
                {"name":"compact","base-url":"https://compact.example","api-key-entries":["sk-three"]}
              ]
            }
            """
        );
        var service = new ManagementConfigurationService(client);

        var response = await service.GetConfigAsync();

        Assert.Collection(
            response.Value.OpenAiCompatibility,
            legacy =>
            {
                Assert.Equal("legacy", legacy.Name);
                Assert.Collection(
                    legacy.ApiKeyEntries,
                    first => Assert.Equal("sk-one", first.ApiKey),
                    second => Assert.Equal("sk-two", second.ApiKey)
                );
            },
            compact =>
            {
                Assert.Equal("compact", compact.Name);
                Assert.Equal("sk-three", Assert.Single(compact.ApiKeyEntries).ApiKey);
            }
        );
    }

    [Fact]
    public async Task GetConfigAsyncMapsNestedProviderSnakeCaseFields()
    {
        var client = new FixedManagementApiClient(
            """
            {
              "gemini-api-key":[
                {
                  "api-key":"gemini-key",
                  "base_url":"https://gemini.example",
                  "proxy_url":"http://proxy.example",
                  "auth_index":"gemini-auth"
                }
              ],
              "codex-api-key":[
                {
                  "api-key":"codex-key",
                  "excluded_models":["legacy-model"],
                  "cloak":{"strict_mode":true,"sensitive_words":["secret"]}
                }
              ],
              "openai-compatibility":[
                {
                  "id":"compat-id",
                  "base-url":"https://compat.example",
                  "auth_index":"compat-auth",
                  "api-key-entries":[{"api-key":"compat-key","auth_index":"entry-auth"}]
                }
              ]
            }
            """
        );
        var service = new ManagementConfigurationService(client);

        var response = await service.GetConfigAsync();

        var gemini = Assert.Single(response.Value.GeminiApiKeys);
        Assert.Equal("https://gemini.example", gemini.BaseUrl);
        Assert.Equal("http://proxy.example", gemini.ProxyUrl);
        Assert.Equal("gemini-auth", gemini.AuthIndex);

        var codex = Assert.Single(response.Value.CodexApiKeys);
        Assert.Equal(["legacy-model"], codex.ExcludedModels);
        Assert.NotNull(codex.Cloak);
        Assert.True(codex.Cloak.StrictMode);
        Assert.Equal(["secret"], codex.Cloak.SensitiveWords);

        var compatibility = Assert.Single(response.Value.OpenAiCompatibility);
        Assert.Equal("compat-id", compatibility.Name);
        Assert.Equal("compat-auth", compatibility.AuthIndex);
        Assert.Equal("entry-auth", Assert.Single(compatibility.ApiKeyEntries).AuthIndex);
    }

    private sealed class FixedManagementApiClient : IManagementApiClient
    {
        private readonly string _body;

        public FixedManagementApiClient(string body)
        {
            _body = body;
        }

        public HttpMethod? LastMethod { get; private set; }

        public string? LastPath { get; private set; }

        public Task<ManagementApiResponse<string>> SendManagementAsync(
            HttpMethod method,
            string path,
            string? body = null,
            string contentType = "application/json",
            string? accept = "application/json",
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        )
        {
            LastMethod = method;
            LastPath = path;
            return Task.FromResult(
                new ManagementApiResponse<string>
                {
                    Value = _body,
                    Metadata = new ManagementServerMetadata(),
                    StatusCode = HttpStatusCode.OK,
                }
            );
        }

        public Task<ManagementApiResponse<string>> SendManagementMultipartAsync(
            HttpMethod method,
            string path,
            IReadOnlyList<ManagementMultipartFile> files,
            IReadOnlyDictionary<string, string>? fields = null,
            string? accept = "application/json",
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ManagementApiResponse<string>> GetBackendAsync(
            string path,
            IReadOnlyDictionary<string, string>? headers = null,
            string? accept = "application/json",
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
