using System.Net;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Infrastructure.Management;

namespace CodexCliPlus.Tests.Management;

public sealed class ManagementUsageServiceTests
{
    [Fact]
    public async Task GetApiKeyUsageAsyncMapsProviderKeyUsageBuckets()
    {
        var client = new RecordingManagementApiClient(
            CreateResponse(
                """{"codex":{"https://chatgpt.com/backend-api/codex|sk-a":{"success":5,"failed":2,"recent_requests":[{"time":"10:00-10:10","success":3,"failed":1}]}}}"""
            )
        );
        var service = new ManagementUsageService(client);

        var response = await service.GetApiKeyUsageAsync();

        Assert.Equal(HttpMethod.Get, client.LastMethod);
        Assert.Equal("api-key-usage", client.LastPath);
        var provider = Assert.Single(response.Value.Providers);
        Assert.Equal("codex", provider.Key);
        var usage = Assert.Single(provider.Value);
        Assert.Equal("https://chatgpt.com/backend-api/codex|sk-a", usage.Key);
        Assert.Equal(5, usage.Value.Success);
        Assert.Equal(2, usage.Value.Failed);
        var bucket = Assert.Single(usage.Value.RecentRequests);
        Assert.Equal("10:00-10:10", bucket.Time);
        Assert.Equal(3, bucket.Success);
        Assert.Equal(1, bucket.Failed);
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
