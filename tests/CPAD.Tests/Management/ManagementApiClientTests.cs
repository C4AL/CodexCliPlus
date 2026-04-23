using System.Net;
using System.Text;

using CPAD.Core.Abstractions.Management;
using CPAD.Core.Exceptions;
using CPAD.Core.Models.Management;
using CPAD.Infrastructure.Management;

using Microsoft.Extensions.DependencyInjection;

namespace CPAD.Tests.Management;

public sealed class ManagementApiClientTests
{
    [Fact]
    public async Task SendManagementAsyncAddsBearerTokenAndReadsServerHeaders()
    {
        using var factory = new FixedHttpClientFactory(async request =>
        {
            Assert.Equal("Bearer secret", request.Headers.Authorization?.ToString());
            Assert.Equal("http://127.0.0.1:6060/v0/management/config", request.RequestUri?.ToString());

            await Task.CompletedTask;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"debug\":true}", Encoding.UTF8, "application/json")
            };
            response.Headers.Add("X-CPA-VERSION", "v1.2.3");
            response.Headers.Add("X-CPA-COMMIT", "abc123");
            response.Headers.Add("X-CPA-BUILD-DATE", "2026-04-23T00:00:00Z");
            return response;
        });

        var client = new ManagementApiClient(
            new StaticConnectionProvider(),
            factory);

        var response = await client.SendManagementAsync(HttpMethod.Get, "config");

        Assert.Contains("\"debug\":true", response.Value, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("v1.2.3", response.Metadata.Version);
        Assert.Equal("abc123", response.Metadata.Commit);
        Assert.Equal("2026-04-23T00:00:00Z", response.Metadata.BuildDate);
    }

    [Fact]
    public async Task SendManagementAsyncRetriesTransientStatusOnce()
    {
        var calls = 0;
        using var factory = new FixedHttpClientFactory(request =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{\"error\":\"starting\"}", Encoding.UTF8, "application/json")
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\"}", Encoding.UTF8, "application/json")
                });
        });

        var client = new ManagementApiClient(
            new StaticConnectionProvider(),
            factory);

        var response = await client.SendManagementAsync(HttpMethod.Get, "usage");

        Assert.Equal(2, calls);
        Assert.Contains("\"status\":\"ok\"", response.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendManagementAsyncThrowsStructuredApiException()
    {
        using var factory = new FixedHttpClientFactory(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"invalid management key\",\"message\":\"denied\"}", Encoding.UTF8, "application/json")
        }));

        var client = new ManagementApiClient(
            new StaticConnectionProvider(),
            factory);

        var exception = await Assert.ThrowsAsync<ManagementApiException>(
            () => client.SendManagementAsync(HttpMethod.Get, "config"));

        Assert.Equal(401, exception.StatusCode);
        Assert.Equal("invalid management key", exception.ErrorCode);
        Assert.Equal("denied", exception.Message);
        Assert.Contains("invalid management key", exception.ResponseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendManagementMultipartAsyncAddsBearerTokenAndMultipartFiles()
    {
        using var factory = new FixedHttpClientFactory(async request =>
        {
            Assert.Equal("Bearer secret", request.Headers.Authorization?.ToString());
            Assert.Equal("http://127.0.0.1:6060/v0/management/auth-files", request.RequestUri?.ToString());

            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            var parts = multipart.ToArray();
            Assert.Equal(2, parts.Length);

            var filePart = Assert.Single(parts, part => part.Headers.ContentDisposition?.FileName?.Contains("alpha.json", StringComparison.Ordinal) == true);
            var fieldPart = Assert.Single(parts, part => part.Headers.ContentDisposition?.FileName is null);

            Assert.Equal("file", filePart.Headers.ContentDisposition?.Name?.Trim('"'));
            Assert.Equal("meta", fieldPart.Headers.ContentDisposition?.Name?.Trim('"'));
            Assert.Equal("demo", await fieldPart.ReadAsStringAsync());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\",\"uploaded\":1}", Encoding.UTF8, "application/json")
            };
        });

        var client = new ManagementApiClient(
            new StaticConnectionProvider(),
            factory);

        var response = await client.SendManagementMultipartAsync(
            HttpMethod.Post,
            "auth-files",
            [
                new ManagementMultipartFile
                {
                    FieldName = "file",
                    FileName = "alpha.json",
                    Content = Encoding.UTF8.GetBytes("{\"type\":\"codex\"}"),
                    ContentType = "application/json"
                }
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["meta"] = "demo"
            });

        Assert.Contains("\"uploaded\":1", response.Value, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class StaticConnectionProvider : IManagementConnectionProvider
    {
        public Task<ManagementConnectionInfo> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ManagementConnectionInfo
            {
                BaseUrl = "http://127.0.0.1:6060",
                ManagementApiBaseUrl = "http://127.0.0.1:6060/v0/management",
                ManagementKey = "secret"
            });
        }
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient _client;

        public FixedHttpClientFactory(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _client = new HttpClient(new DelegatingHandler(handler));
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        private sealed class DelegatingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

            public DelegatingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _handler(request);
            }
        }
    }
}
