using System.Globalization;
using System.Net;
using System.Text;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Infrastructure.Management;

namespace CodexCliPlus.Tests.Management;

[Trait("Category", "Fast")]
public sealed class ManagementAuthServiceTests
{
    [Fact]
    public async Task ReplaceApiKeysAsyncNormalizesDistinctKeysAndUsesPutApiKeys()
    {
        var client = new RecordingManagementApiClient(CreateResponse("""{"status":"ok"}"""));
        var service = new ManagementAuthService(client);

        var response = await service.ReplaceApiKeysAsync([" key-a ", "key-b", "key-a", " "]);

        Assert.Equal(HttpMethod.Put, client.LastMethod);
        Assert.Equal("api-keys", client.LastPath);
        Assert.Equal("""["key-a","key-b"]""", client.LastBody);
        Assert.Equal("application/json", client.LastContentType);
        Assert.Equal("ok", response.Value.Status);
    }

    [Fact]
    public async Task UpdateApiKeyAsyncNormalizesValueAndRejectsBlank()
    {
        var client = new RecordingManagementApiClient(CreateResponse("""{"status":"ok"}"""));
        var service = new ManagementAuthService(client);

        var response = await service.UpdateApiKeyAsync(1, " sk-updated ");

        Assert.Equal(HttpMethod.Patch, client.LastMethod);
        Assert.Equal("api-keys", client.LastPath);
        Assert.Equal("""{"index":1,"value":"sk-updated"}""", client.LastBody);
        Assert.Equal("application/json", client.LastContentType);
        Assert.Equal("ok", response.Value.Status);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateApiKeyAsync(1, " ")
        );
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public async Task UploadAuthFileAsyncAppendsJsonExtensionAndUsesRawJsonUpload()
    {
        var client = new RecordingManagementApiClient(CreateResponse("""{"status":"ok"}"""));
        var service = new ManagementAuthService(client);
        const string json = """{"type":"codex","metadata":{"cookie":"sid=1"}}""";

        var response = await service.UploadAuthFileAsync(@"nested\cookie-auth", json);

        Assert.Equal(HttpMethod.Post, client.LastMethod);
        Assert.Equal("auth-files?name=cookie-auth.json", client.LastPath);
        Assert.Equal(json, client.LastBody);
        Assert.Equal("application/json", client.LastContentType);
        Assert.Equal("ok", response.Value.Status);
    }

    [Fact]
    public async Task GetOAuthStartAsyncUsesAuditedWebUiAndProjectQueryForGeminiCli()
    {
        var client = new RecordingManagementApiClient(
            CreateResponse("""{"url":"https://example.test/oauth","state":"oauth-state"}""")
        );
        var service = new ManagementAuthService(client);

        var response = await service.GetOAuthStartAsync("gemini-cli", " project-42 ");

        Assert.Equal(HttpMethod.Get, client.LastMethod);
        Assert.Equal("gemini-cli-auth-url?is_webui=true&project_id=project-42", client.LastPath);
        Assert.Equal("https://example.test/oauth", response.Value.Url);
        Assert.Equal("oauth-state", response.Value.State);
    }

    [Fact]
    public async Task SubmitOAuthCallbackAsyncNormalizesGeminiCliProviderToGemini()
    {
        var client = new RecordingManagementApiClient(CreateResponse("""{"status":"ok"}"""));
        var service = new ManagementAuthService(client);

        var response = await service.SubmitOAuthCallbackAsync(
            "gemini-cli",
            "http://127.0.0.1/callback?code=1"
        );

        Assert.Equal(HttpMethod.Post, client.LastMethod);
        Assert.Equal("oauth-callback", client.LastPath);
        Assert.Equal(
            """{"provider":"gemini","redirect_url":"http://127.0.0.1/callback?code=1"}""",
            client.LastBody
        );
        Assert.Equal("ok", response.Value.Status);
    }

    [Fact]
    public async Task ReplaceOAuthExcludedModelsAsyncMergesEquivalentProviderKeys()
    {
        var client = new RecordingManagementApiClient(CreateResponse("""{"status":"ok"}"""));
        var service = new ManagementAuthService(client);

        await service.ReplaceOAuthExcludedModelsAsync(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [" Gemini "] = [" gemini-1.5-pro ", "GEMINI-1.5-PRO"],
                ["gemini"] = ["gemini-2.0-flash"],
                [" "] = ["ignored"],
            }
        );

        Assert.Equal(HttpMethod.Put, client.LastMethod);
        Assert.Equal("oauth-excluded-models", client.LastPath);
        Assert.Equal("""{"gemini":["gemini-1.5-pro","gemini-2.0-flash"]}""", client.LastBody);
    }

    [Fact]
    public async Task ReplaceOAuthModelAliasesAsyncMergesEquivalentProviderKeys()
    {
        var client = new RecordingManagementApiClient(CreateResponse("""{"status":"ok"}"""));
        var service = new ManagementAuthService(client);

        await service.ReplaceOAuthModelAliasesAsync(
            new Dictionary<
                string,
                IReadOnlyList<ManagementOAuthModelAliasEntry>
            >(StringComparer.Ordinal)
            {
                [" OpenAI "] =
                [
                    new ManagementOAuthModelAliasEntry
                    {
                        Name = " gpt-5 ",
                        Alias = " gpt-5-chat ",
                    },
                ],
                ["openai"] =
                [
                    new ManagementOAuthModelAliasEntry
                    {
                        Name = "gpt-5-mini",
                        Alias = "gpt-5-mini-chat",
                        Fork = true,
                    },
                ],
                [" "] =
                [
                    new ManagementOAuthModelAliasEntry
                    {
                        Name = "ignored",
                        Alias = "ignored",
                    },
                ],
            }
        );

        Assert.Equal(HttpMethod.Put, client.LastMethod);
        Assert.Equal("oauth-model-alias", client.LastPath);
        Assert.Equal(
            """{"openai":[{"name":"gpt-5","alias":"gpt-5-chat","fork":false},{"name":"gpt-5-mini","alias":"gpt-5-mini-chat","fork":true}]}""",
            client.LastBody
        );
    }

    [Fact]
    public async Task GetOAuthExcludedModelsAsyncMergesEquivalentProviderKeys()
    {
        var client = new RecordingManagementApiClient(
            CreateResponse(
                """{"oauth-excluded-models":{" Gemini ":[" gemini-1.5-pro ","GEMINI-1.5-PRO"],"gemini":["gemini-2.0-flash"]," ":["ignored"]}}"""
            )
        );
        var service = new ManagementAuthService(client);

        var response = await service.GetOAuthExcludedModelsAsync();

        Assert.Equal(HttpMethod.Get, client.LastMethod);
        Assert.Equal("oauth-excluded-models", client.LastPath);
        var entry = Assert.Single(response.Value);
        Assert.Equal("Gemini", entry.Key);
        Assert.Equal(["gemini-1.5-pro", "gemini-2.0-flash"], entry.Value);
    }

    [Fact]
    public async Task GetOAuthModelAliasesAsyncMergesEquivalentProviderKeys()
    {
        var client = new RecordingManagementApiClient(
            CreateResponse(
                """{"oauth-model-alias":{" OpenAI ":[{"name":" gpt-5 ","alias":" gpt-5-chat ","fork":false}],"openai":[{"name":"gpt-5-mini","alias":"gpt-5-mini-chat","fork":true}]," ":[{"name":"ignored","alias":"ignored"}]}}"""
            )
        );
        var service = new ManagementAuthService(client);

        var response = await service.GetOAuthModelAliasesAsync();

        Assert.Equal(HttpMethod.Get, client.LastMethod);
        Assert.Equal("oauth-model-alias", client.LastPath);
        var entry = Assert.Single(response.Value);
        Assert.Equal("OpenAI", entry.Key);
        Assert.Collection(
            entry.Value,
            first =>
            {
                Assert.Equal("gpt-5", first.Name);
                Assert.Equal("gpt-5-chat", first.Alias);
                Assert.False(first.Fork);
            },
            second =>
            {
                Assert.Equal("gpt-5-mini", second.Name);
                Assert.Equal("gpt-5-mini-chat", second.Alias);
                Assert.True(second.Fork);
            }
        );
    }

    [Fact]
    public async Task UpdateOAuthExcludedModelsAsyncNormalizesProviderAndModels()
    {
        var client = new RecordingManagementApiClient(CreateResponse("""{"status":"ok"}"""));
        var service = new ManagementAuthService(client);

        await service.UpdateOAuthExcludedModelsAsync(
            " Gemini ",
            [" gemini-1.5-pro ", "GEMINI-1.5-PRO", " "]
        );

        Assert.Equal(HttpMethod.Patch, client.LastMethod);
        Assert.Equal("oauth-excluded-models", client.LastPath);
        Assert.Equal("""{"provider":"gemini","models":["gemini-1.5-pro"]}""", client.LastBody);
    }

    [Fact]
    public async Task UpdateOAuthModelAliasAsyncNormalizesChannelAndAliases()
    {
        var client = new RecordingManagementApiClient(CreateResponse("""{"status":"ok"}"""));
        var service = new ManagementAuthService(client);

        await service.UpdateOAuthModelAliasAsync(
            " OpenAI ",
            [
                new ManagementOAuthModelAliasEntry
                {
                    Name = " gpt-5 ",
                    Alias = " gpt-5-chat ",
                },
                new ManagementOAuthModelAliasEntry { Name = " ", Alias = "ignored" },
            ]
        );

        Assert.Equal(HttpMethod.Patch, client.LastMethod);
        Assert.Equal("oauth-model-alias", client.LastPath);
        Assert.Equal(
            """{"channel":"openai","aliases":[{"name":"gpt-5","alias":"gpt-5-chat","fork":false}]}""",
            client.LastBody
        );
    }

    [Fact]
    public async Task GetAuthFilesAsyncMapsReturnedAuthFileFields()
    {
        var client = new RecordingManagementApiClient(
            CreateResponse(
                """{"files":[{"name":"alpha.json","type":"codex","email":"alpha@example.com","disabled":true,"path":"C:\\auths\\alpha.json","updated_at":"2026-04-23T01:02:03Z","success":3,"failed":1,"recent_requests":[{"time":"10:00-10:10","success":2,"failed":1}]}]}"""
            )
        );
        var service = new ManagementAuthService(client);

        var response = await service.GetAuthFilesAsync();

        Assert.Equal(HttpMethod.Get, client.LastMethod);
        Assert.Equal("auth-files", client.LastPath);
        var authFile = Assert.Single(response.Value);
        Assert.Equal("alpha.json", authFile.Name);
        Assert.Equal("codex", authFile.Type);
        Assert.Equal("alpha@example.com", authFile.Email);
        Assert.True(authFile.Disabled);
        Assert.Equal(3, authFile.Success);
        Assert.Equal(1, authFile.Failed);
        var recentRequest = Assert.Single(authFile.RecentRequests);
        Assert.Equal("10:00-10:10", recentRequest.Time);
        Assert.Equal(2, recentRequest.Success);
        Assert.Equal(1, recentRequest.Failed);
        Assert.Equal(@"C:\auths\alpha.json", authFile.Path);
        Assert.Equal(
            DateTimeOffset.Parse("2026-04-23T01:02:03Z", CultureInfo.InvariantCulture),
            authFile.UpdatedAt
        );
    }

    [Fact]
    public async Task UploadAuthFilesAsyncUsesMultipartEndpointAndNormalizesFileNames()
    {
        var client = new RecordingManagementApiClient(
            CreateResponse("""{"status":"ok","uploaded":1}""")
        );
        var service = new ManagementAuthService(client);

        var response = await service.UploadAuthFilesAsync([
            new ManagementAuthFileUpload
            {
                FileName = @"nested\alpha-auth",
                Content = Encoding.UTF8.GetBytes("""{"type":"codex"}"""),
            },
        ]);

        Assert.Equal(HttpMethod.Post, client.LastMethod);
        Assert.Equal("auth-files", client.LastPath);
        Assert.Equal("multipart/form-data", client.LastContentType);
        Assert.Equal("alpha-auth.json", client.LastBody);
        Assert.Equal(1, response.Value.Uploaded);
    }

    [Fact]
    public async Task AuthFileOperationsNormalizeFileNamesConsistently()
    {
        var client = new RecordingManagementApiClient(CreateResponse("""{"status":"ok"}"""));
        var service = new ManagementAuthService(client);

        await service.DeleteAuthFileAsync(@"nested\alpha-auth");

        Assert.Equal(HttpMethod.Delete, client.LastMethod);
        Assert.Equal("auth-files?name=alpha-auth.json", client.LastPath);

        await service.DeleteAuthFilesAsync(
            [@"nested\alpha-auth", " alpha-auth.json ", "beta-auth"]
        );

        Assert.Equal(HttpMethod.Delete, client.LastMethod);
        Assert.Equal("auth-files?name=alpha-auth.json&name=beta-auth.json", client.LastPath);

        await service.DownloadAuthFileAsync(@"nested\alpha-auth");

        Assert.Equal(HttpMethod.Get, client.LastMethod);
        Assert.Equal("auth-files/download?name=alpha-auth.json", client.LastPath);
        Assert.Equal("*/*", client.LastAccept);

        await service.SetAuthFileDisabledAsync(@"nested\alpha-auth", disabled: true);

        Assert.Equal(HttpMethod.Patch, client.LastMethod);
        Assert.Equal("auth-files/status", client.LastPath);
        Assert.Equal("""{"name":"alpha-auth.json","disabled":true}""", client.LastBody);

        await service.PatchAuthFileFieldsAsync(
            new ManagementAuthFileFieldPatch { Name = @"nested\alpha-auth" }
        );

        Assert.Equal(HttpMethod.Patch, client.LastMethod);
        Assert.Equal("auth-files/fields", client.LastPath);
        Assert.Equal("""{"name":"alpha-auth.json"}""", client.LastBody);
    }

    [Fact]
    public async Task ProviderKeyDeletesNormalizeApiKeyAndRejectBlank()
    {
        var client = new RecordingManagementApiClient(CreateResponse("""{"status":"ok"}"""));
        var service = new ManagementAuthService(client);

        await service.DeleteGeminiKeyAsync(" shared-key ", " https://a.example.com ");

        Assert.Equal(HttpMethod.Delete, client.LastMethod);
        Assert.Equal(
            "gemini-api-key?api-key=shared-key&base-url=https%3A%2F%2Fa.example.com",
            client.LastPath
        );

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.DeleteCodexKeyAsync(" ")
        );
        Assert.Equal("apiKey", exception.ParamName);
    }

    [Fact]
    public async Task DeleteOpenAiCompatibilityAsyncNormalizesNameAndRejectsBlank()
    {
        var client = new RecordingManagementApiClient(CreateResponse("""{"status":"ok"}"""));
        var service = new ManagementAuthService(client);

        await service.DeleteOpenAiCompatibilityAsync(" Provider One ");

        Assert.Equal(HttpMethod.Delete, client.LastMethod);
        Assert.Equal("openai-compatibility?name=Provider%20One", client.LastPath);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.DeleteOpenAiCompatibilityAsync(" ")
        );
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public async Task GetAmpCodeAsyncMapsSnakeCasePayload()
    {
        var client = new RecordingManagementApiClient(
            CreateResponse(
                """
                {
                  "ampcode":{
                    "upstream_url":"https://amp.example",
                    "upstream_api_key":"amp-upstream-key",
                    "force_model_mappings":true,
                    "model_mappings":[{"from":"amp-model","to":"local-model"}],
                    "upstream_api_keys":[{"upstream_api_key":"amp-route-key","api_keys":["sk-client-a","sk-client-b"]}]
                  }
                }
                """
            )
        );
        var service = new ManagementAuthService(client);

        var response = await service.GetAmpCodeAsync();

        Assert.Equal(HttpMethod.Get, client.LastMethod);
        Assert.Equal("ampcode", client.LastPath);
        Assert.Equal("https://amp.example", response.Value.UpstreamUrl);
        Assert.Equal("amp-upstream-key", response.Value.UpstreamApiKey);
        Assert.True(response.Value.ForceModelMappings);

        var modelMapping = Assert.Single(response.Value.ModelMappings);
        Assert.Equal("amp-model", modelMapping.From);
        Assert.Equal("local-model", modelMapping.To);

        var upstreamMapping = Assert.Single(response.Value.UpstreamApiKeys);
        Assert.Equal("amp-route-key", upstreamMapping.UpstreamApiKey);
        Assert.Equal(["sk-client-a", "sk-client-b"], upstreamMapping.ApiKeys);
    }

    [Fact]
    public async Task GetAmpDedicatedListsAsyncMapsSnakeCasePayloads()
    {
        var upstreamClient = new RecordingManagementApiClient(
            CreateResponse(
                """{"upstream_api_keys":[{"upstream_api_key":"amp-route-key","api_keys":["sk-client"]}]}"""
            )
        );
        var upstreamService = new ManagementAuthService(upstreamClient);

        var upstreamResponse = await upstreamService.GetAmpUpstreamApiKeysAsync();

        Assert.Equal(HttpMethod.Get, upstreamClient.LastMethod);
        Assert.Equal("ampcode/upstream-api-keys", upstreamClient.LastPath);
        var upstreamMapping = Assert.Single(upstreamResponse.Value);
        Assert.Equal("amp-route-key", upstreamMapping.UpstreamApiKey);
        Assert.Equal(["sk-client"], upstreamMapping.ApiKeys);

        var modelClient = new RecordingManagementApiClient(
            CreateResponse("""{"model_mappings":[{"from":"amp-model","to":"local-model"}]}""")
        );
        var modelService = new ManagementAuthService(modelClient);

        var modelResponse = await modelService.GetAmpModelMappingsAsync();

        Assert.Equal(HttpMethod.Get, modelClient.LastMethod);
        Assert.Equal("ampcode/model-mappings", modelClient.LastPath);
        var modelMapping = Assert.Single(modelResponse.Value);
        Assert.Equal("amp-model", modelMapping.From);
        Assert.Equal("local-model", modelMapping.To);
    }

    [Fact]
    public async Task GetAuthFileModelsAsyncKeepsRuntimeAuthIds()
    {
        var client = new RecordingManagementApiClient(CreateResponse("[]"));
        var service = new ManagementAuthService(client);

        await service.GetAuthFileModelsAsync(" runtime-auth ");

        Assert.Equal(HttpMethod.Get, client.LastMethod);
        Assert.Equal("auth-files/models?name=runtime-auth", client.LastPath);
    }

    [Fact]
    public async Task GetModelDefinitionsAsyncNormalizesChannelPathSegment()
    {
        var client = new RecordingManagementApiClient(CreateResponse("""{"models":[]}"""));
        var service = new ManagementAuthService(client);

        await service.GetModelDefinitionsAsync(" codex ");

        Assert.Equal(HttpMethod.Get, client.LastMethod);
        Assert.Equal("model-definitions/codex", client.LastPath);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetModelDefinitionsAsync(" ")
        );
        Assert.Equal("channel", exception.ParamName);
    }

    private static ManagementApiResponse<string> CreateResponse(string body)
    {
        return new ManagementApiResponse<string>
        {
            Value = body,
            Metadata = new ManagementServerMetadata(),
            StatusCode = HttpStatusCode.OK,
        };
    }

    private sealed class RecordingManagementApiClient : IManagementApiClient
    {
        private readonly ManagementApiResponse<string> _response;

        public RecordingManagementApiClient(ManagementApiResponse<string> response)
        {
            _response = response;
        }

        public HttpMethod? LastMethod { get; private set; }

        public string? LastPath { get; private set; }

        public string? LastBody { get; private set; }

        public string? LastContentType { get; private set; }

        public string? LastAccept { get; private set; }

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
            LastBody = body;
            LastContentType = contentType;
            LastAccept = accept;
            return Task.FromResult(_response);
        }

        public Task<ManagementApiResponse<string>> SendManagementMultipartAsync(
            HttpMethod method,
            string path,
            IReadOnlyList<ManagementMultipartFile> files,
            IReadOnlyDictionary<string, string>? fields = null,
            string? accept = "application/json",
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        )
        {
            LastMethod = method;
            LastPath = path;
            LastBody = string.Join(",", files.Select(file => file.FileName));
            LastContentType = "multipart/form-data";
            LastAccept = accept;
            return Task.FromResult(_response);
        }

        public Task<ManagementApiResponse<string>> GetBackendAsync(
            string path,
            IReadOnlyDictionary<string, string>? headers = null,
            string? accept = "application/json",
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }
    }
}
