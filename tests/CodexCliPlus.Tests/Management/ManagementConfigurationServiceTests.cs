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
