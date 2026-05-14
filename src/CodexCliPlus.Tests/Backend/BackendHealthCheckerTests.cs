using System.Net;
using CodexCliPlus.Infrastructure.Backend;

namespace CodexCliPlus.Tests.Backend;

[Trait("Category", "Fast")]
public sealed class BackendHealthCheckerTests
{
    [Fact]
    public async Task WaitUntilHealthyAsyncPropagatesCancellationBeforeTimeoutShortCircuit()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var httpClientFactory = new CountingHttpClientFactory();
        var checker = new BackendHealthChecker(httpClientFactory);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            checker.WaitUntilHealthyAsync(
                "http://127.0.0.1:1327/healthz",
                TimeSpan.Zero,
                cancellation.Token
            )
        );

        Assert.Equal(0, httpClientFactory.CreateClientCalls);
    }

    private sealed class CountingHttpClientFactory : IHttpClientFactory
    {
        public int CreateClientCalls { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreateClientCalls++;
            return new HttpClient(new ThrowingHandler());
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            throw new InvalidOperationException("No HTTP request should be sent.");
        }
    }
}
