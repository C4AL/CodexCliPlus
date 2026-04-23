using System.Globalization;
using System.Net;

using CPAD.Core.Abstractions.Management;
using CPAD.Core.Models.Management;
using CPAD.Infrastructure.Management;

namespace CPAD.Tests.Management;

public sealed class ManagementAuthServiceTests
{
    [Fact]
    public async Task ReplaceApiKeysAsyncNormalizesDistinctKeysAndUsesPutApiKeys()
    {
        var client = new RecordingManagementApiClient(
            CreateResponse("""{"status":"ok"}"""));
        var service = new ManagementAuthService(client);

        var response = await service.ReplaceApiKeysAsync([" key-a ", "key-b", "key-a", " "]);

        Assert.Equal(HttpMethod.Put, client.LastMethod);
        Assert.Equal("api-keys", client.LastPath);
        Assert.Equal("""["key-a","key-b"]""", client.LastBody);
        Assert.Equal("application/json", client.LastContentType);
        Assert.Equal("ok", response.Value.Status);
    }

    [Fact]
    public async Task UploadAuthFileAsyncAppendsJsonExtensionAndUsesRawJsonUpload()
    {
        var client = new RecordingManagementApiClient(
            CreateResponse("""{"status":"ok"}"""));
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
            CreateResponse("""{"url":"https://example.test/oauth","state":"oauth-state"}"""));
        var service = new ManagementAuthService(client);

        var response = await service.GetOAuthStartAsync("gemini-cli", "project-42");

        Assert.Equal(HttpMethod.Get, client.LastMethod);
        Assert.Equal("gemini-cli-auth-url?is_webui=true&project_id=project-42", client.LastPath);
        Assert.Equal("https://example.test/oauth", response.Value.Url);
        Assert.Equal("oauth-state", response.Value.State);
    }

    [Fact]
    public async Task SubmitOAuthCallbackAsyncNormalizesGeminiCliProviderToGemini()
    {
        var client = new RecordingManagementApiClient(
            CreateResponse("""{"status":"ok"}"""));
        var service = new ManagementAuthService(client);

        var response = await service.SubmitOAuthCallbackAsync("gemini-cli", "http://127.0.0.1/callback?code=1");

        Assert.Equal(HttpMethod.Post, client.LastMethod);
        Assert.Equal("oauth-callback", client.LastPath);
        Assert.Equal("""{"provider":"gemini","redirect_url":"http://127.0.0.1/callback?code=1"}""", client.LastBody);
        Assert.Equal("ok", response.Value.Status);
    }

    [Fact]
    public async Task GetAuthFilesAsyncMapsReturnedAuthFileFields()
    {
        var client = new RecordingManagementApiClient(
            CreateResponse("""{"files":[{"name":"alpha.json","type":"codex","email":"alpha@example.com","disabled":true,"path":"C:\\auths\\alpha.json","updated_at":"2026-04-23T01:02:03Z"}]}"""));
        var service = new ManagementAuthService(client);

        var response = await service.GetAuthFilesAsync();

        Assert.Equal(HttpMethod.Get, client.LastMethod);
        Assert.Equal("auth-files", client.LastPath);
        var authFile = Assert.Single(response.Value);
        Assert.Equal("alpha.json", authFile.Name);
        Assert.Equal("codex", authFile.Type);
        Assert.Equal("alpha@example.com", authFile.Email);
        Assert.True(authFile.Disabled);
        Assert.Equal(@"C:\auths\alpha.json", authFile.Path);
        Assert.Equal(DateTimeOffset.Parse("2026-04-23T01:02:03Z", CultureInfo.InvariantCulture), authFile.UpdatedAt);
    }

    private static ManagementApiResponse<string> CreateResponse(string body)
    {
        return new ManagementApiResponse<string>
        {
            Value = body,
            Metadata = new ManagementServerMetadata(),
            StatusCode = HttpStatusCode.OK
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
            CancellationToken cancellationToken = default)
        {
            LastMethod = method;
            LastPath = path;
            LastBody = body;
            LastContentType = contentType;
            LastAccept = accept;
            return Task.FromResult(_response);
        }

        public Task<ManagementApiResponse<string>> GetBackendAsync(
            string path,
            IReadOnlyDictionary<string, string>? headers = null,
            string? accept = "application/json",
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
