using System.Net;
using System.Text;

using CPAD.Core.Enums;
using CPAD.Infrastructure.Updates;

namespace CPAD.Tests.Updates;

public sealed class GitHubReleaseUpdateServiceTests
{
    [Fact]
    public async Task CheckAsyncReturnsUpdateAvailableWhenLatestStableReleaseIsNewer()
    {
        using var factory = new FixedHttpClientFactory(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "tag_name": "v1.2.3",
                  "html_url": "https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/releases/tag/v1.2.3",
                  "published_at": "2026-04-23T00:00:00Z",
                  "assets": [
                    {
                      "name": "CPAD.Setup.1.2.3.exe",
                      "browser_download_url": "https://example.test/CPAD.Setup.1.2.3.exe",
                      "size": 10485760,
                      "digest": "sha256:abc123"
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        }));

        var service = new GitHubReleaseUpdateService(factory);

        var result = await service.CheckAsync("1.0.0");

        Assert.True(result.IsCheckSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.False(result.IsNoReleasePublished);
        Assert.Equal("1.2.3", result.LatestVersion);
        Assert.Equal("Update available", result.Status);
        Assert.True(result.HasInstallableAsset);
        Assert.NotNull(result.InstallableAsset);
        Assert.Single(result.Assets);
        Assert.Equal("CPAD.Setup.1.2.3.exe", result.Assets[0].Name);
        Assert.Equal("CPAD.Setup.1.2.3.exe", result.InstallableAsset!.Name);
    }

    [Fact]
    public async Task CheckAsyncTreatsGithub404AsNoStableRelease()
    {
        using var factory = new FixedHttpClientFactory(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"message\":\"Not Found\"}", Encoding.UTF8, "application/json")
        }));

        var service = new GitHubReleaseUpdateService(factory);

        var result = await service.CheckAsync("1.0.0");

        Assert.True(result.IsCheckSuccessful);
        Assert.True(result.IsNoReleasePublished);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("No stable release", result.Status);
        Assert.Null(result.LatestVersion);
        Assert.False(result.HasInstallableAsset);
        Assert.Null(result.InstallableAsset);
    }

    [Fact]
    public async Task CheckAsyncReturnsReservedResultForBetaChannelWithoutCallingGithub()
    {
        var calls = 0;
        using var factory = new FixedHttpClientFactory(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var service = new GitHubReleaseUpdateService(factory);

        var result = await service.CheckAsync("1.0.0", UpdateChannel.Beta);

        Assert.True(result.IsCheckSuccessful);
        Assert.True(result.IsChannelReserved);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("Beta reserved", result.Status);
        Assert.False(result.HasInstallableAsset);
        Assert.Null(result.InstallableAsset);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CheckAsyncDoesNotMarkPortableAssetAsDirectInstaller()
    {
        using var factory = new FixedHttpClientFactory(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "tag_name": "v1.2.3",
                  "html_url": "https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/releases/tag/v1.2.3",
                  "published_at": "2026-04-23T00:00:00Z",
                  "assets": [
                    {
                      "name": "CPAD.Portable.1.2.3.zip",
                      "browser_download_url": "https://example.test/CPAD.Portable.1.2.3.zip",
                      "size": 2048
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        }));

        var service = new GitHubReleaseUpdateService(factory);

        var result = await service.CheckAsync("1.0.0");

        Assert.True(result.IsCheckSuccessful);
        Assert.False(result.HasInstallableAsset);
        Assert.Null(result.InstallableAsset);
        Assert.Single(result.Assets);
    }

    [Fact]
    public async Task CheckAsyncDoesNotMarkInstallerWithoutDownloadUrlAsDirectInstaller()
    {
        using var factory = new FixedHttpClientFactory(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "tag_name": "v1.2.3",
                  "html_url": "https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/releases/tag/v1.2.3",
                  "published_at": "2026-04-23T00:00:00Z",
                  "assets": [
                    {
                      "name": "CPAD.Setup.1.2.3.exe",
                      "size": 2048
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        }));

        var service = new GitHubReleaseUpdateService(factory);

        var result = await service.CheckAsync("1.0.0");

        Assert.True(result.IsCheckSuccessful);
        Assert.False(result.HasInstallableAsset);
        Assert.Null(result.InstallableAsset);
        Assert.Single(result.Assets);
        Assert.Equal("CPAD.Setup.1.2.3.exe", result.Assets[0].Name);
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
