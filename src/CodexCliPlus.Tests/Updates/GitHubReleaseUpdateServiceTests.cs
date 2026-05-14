using System.Net;
using System.Text;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Infrastructure.Updates;

namespace CodexCliPlus.Tests.Updates;

[Trait("Category", "Fast")]
public sealed class GitHubReleaseUpdateServiceTests
{
    [Fact]
    public async Task CheckAsyncReturnsUpdateAvailableWhenLatestStableReleaseIsNewer()
    {
        using var factory = new FixedHttpClientFactory(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "tag_name": "v1.2.3",
                          "html_url": "https://github.com/C4AL/CodexCliPlus/releases/tag/v1.2.3",
                          "published_at": "2026-04-23T00:00:00Z",
                          "assets": [
                            {
                              "name": "CodexCliPlus.Update.1.2.3.win-x64.zip",
                              "browser_download_url": "https://example.test/CodexCliPlus.Update.1.2.3.win-x64.zip",
                              "size": 10485760,
                              "digest": "sha256:abc123"
                            }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            )
        );

        var service = new GitHubReleaseUpdateService(factory);

        var result = await service.CheckAsync("1.0.0");

        Assert.True(result.IsCheckSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.False(result.IsNoReleasePublished);
        Assert.Equal("1.2.3", result.LatestVersion);
        Assert.Equal("Update available", result.Status);
        Assert.Equal("C4AL/CodexCliPlus", result.Repository);
        Assert.Equal(
            "https://api.github.com/repos/C4AL/CodexCliPlus/releases/latest",
            result.ApiUrl
        );
        Assert.Equal(
            "https://github.com/C4AL/CodexCliPlus/releases/tag/v1.2.3",
            result.ReleasePageUrl
        );
        Assert.True(result.HasInstallableAsset);
        Assert.NotNull(result.InstallableAsset);
        Assert.Single(result.Assets);
        Assert.Equal("CodexCliPlus.Update.1.2.3.win-x64.zip", result.Assets[0].Name);
        Assert.Equal("CodexCliPlus.Update.1.2.3.win-x64.zip", result.InstallableAsset!.Name);
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3-beta.1", true, "Update available")]
    [InlineData("v1.2.3-beta.2", "1.2.3-beta.10", false, "Up to date")]
    [InlineData("v1.2.3+build.2", "1.2.3+build.1", false, "Up to date")]
    public async Task CheckAsyncComparesSemVerReleaseVersions(
        string latestTag,
        string currentVersion,
        bool expectedUpdateAvailable,
        string expectedStatus
    )
    {
        using var factory = new FixedHttpClientFactory(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""
                        {
                          "tag_name": "{{latestTag}}",
                          "html_url": "https://github.com/C4AL/CodexCliPlus/releases/tag/{{latestTag}}",
                          "published_at": "2026-04-23T00:00:00Z",
                          "assets": []
                        }
                        """,
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            )
        );

        var service = new GitHubReleaseUpdateService(factory);

        var result = await service.CheckAsync(currentVersion);

        Assert.True(result.IsCheckSuccessful);
        Assert.Equal(latestTag.TrimStart('v', 'V'), result.LatestVersion);
        Assert.Equal(expectedUpdateAvailable, result.IsUpdateAvailable);
        Assert.Equal(expectedStatus, result.Status);
    }

    [Fact]
    public async Task CheckAsyncTreatsGithub404AsNoStableRelease()
    {
        using var factory = new FixedHttpClientFactory(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(
                        "{\"message\":\"Not Found\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            )
        );

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
    public async Task CheckAsyncReturnsFailedResultForMalformedReleaseJson()
    {
        using var factory = new FixedHttpClientFactory(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{ not-json",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            )
        );

        var service = new GitHubReleaseUpdateService(factory);

        var result = await service.CheckAsync("1.0.0");

        Assert.False(result.IsCheckSuccessful);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("Check failed", result.Status);
        Assert.Equal("GitHub 返回的更新信息不是有效 JSON。", result.Detail);
        Assert.Equal("无法解析 GitHub 更新信息。", result.ErrorMessage);
        Assert.Equal(
            "https://github.com/C4AL/CodexCliPlus/releases",
            result.ReleasePageUrl
        );
        Assert.Empty(result.Assets);
    }

    [Fact]
    public async Task CheckAsyncReturnsChineseFailureForGithubHttpError()
    {
        var content = new TrackingHttpContent("{\"message\":\"server exploded\"}");
        using var factory = new FixedHttpClientFactory(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = content,
                }
            )
        );

        var service = new GitHubReleaseUpdateService(factory);

        var result = await service.CheckAsync("1.0.0");

        Assert.False(result.IsCheckSuccessful);
        Assert.Equal("Check failed", result.Status);
        Assert.Equal("GitHub 最新版本接口返回 HTTP 500。", result.Detail);
        Assert.Equal("GitHub 更新检查失败：HTTP 500。", result.ErrorMessage);
        Assert.DoesNotContain("server exploded", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("server exploded", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(0, content.SerializeCount);
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
    public async Task CheckAsyncPropagatesPreCanceledTokenBeforeCreatingHttpClient()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new GitHubReleaseUpdateService(new ThrowingHttpClientFactory());

        var exception = await Record.ExceptionAsync(async () =>
            await service.CheckAsync("1.0.0", UpdateChannel.Stable, cancellation.Token)
        );

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task CheckAsyncDoesNotMarkUnrelatedZipArchiveAsInstallable()
    {
        using var factory = new FixedHttpClientFactory(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "tag_name": "v1.2.3",
                          "html_url": "https://github.com/C4AL/CodexCliPlus/releases/tag/v1.2.3",
                          "published_at": "2026-04-23T00:00:00Z",
                          "assets": [
                            {
                              "name": "CodexCliPlus.Source.1.2.3.zip",
                              "browser_download_url": "https://example.test/CodexCliPlus.Source.1.2.3.zip",
                              "size": 2048
                            }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            )
        );

        var service = new GitHubReleaseUpdateService(factory);

        var result = await service.CheckAsync("1.0.0");

        Assert.True(result.IsCheckSuccessful);
        Assert.False(result.HasInstallableAsset);
        Assert.Null(result.InstallableAsset);
        Assert.Single(result.Assets);
    }

    [Fact]
    public async Task CheckAsyncDoesNotMarkUpdatePackageWithoutDownloadUrlAsInstallable()
    {
        using var factory = new FixedHttpClientFactory(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "tag_name": "v1.2.3",
                          "html_url": "https://github.com/C4AL/CodexCliPlus/releases/tag/v1.2.3",
                          "published_at": "2026-04-23T00:00:00Z",
                          "assets": [
                            {
                              "name": "CodexCliPlus.Update.1.2.3.win-x64.zip",
                              "size": 2048
                            }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            )
        );

        var service = new GitHubReleaseUpdateService(factory);

        var result = await service.CheckAsync("1.0.0");

        Assert.True(result.IsCheckSuccessful);
        Assert.False(result.HasInstallableAsset);
        Assert.Null(result.InstallableAsset);
        Assert.Single(result.Assets);
        Assert.Equal("CodexCliPlus.Update.1.2.3.win-x64.zip", result.Assets[0].Name);
    }

    [Fact]
    public async Task CheckAsyncDoesNotMarkOfflineInstallerAsInstallable()
    {
        using var factory = new FixedHttpClientFactory(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "tag_name": "v1.2.3",
                          "html_url": "https://github.com/C4AL/CodexCliPlus/releases/tag/v1.2.3",
                          "published_at": "2026-04-23T00:00:00Z",
                          "assets": [
                            {
                              "name": "CodexCliPlus.Setup.Offline.1.2.3.exe",
                              "browser_download_url": "https://example.test/CodexCliPlus.Setup.Offline.1.2.3.exe",
                              "size": 2048
                            }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            )
        );

        var service = new GitHubReleaseUpdateService(factory);

        var result = await service.CheckAsync("1.0.0");

        Assert.True(result.IsCheckSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.False(result.HasInstallableAsset);
        Assert.Null(result.InstallableAsset);
        Assert.Single(result.Assets);
        Assert.Equal("CodexCliPlus.Setup.Offline.1.2.3.exe", result.Assets[0].Name);
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

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                return _handler(request);
            }
        }
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            throw new InvalidOperationException("测试应在创建 HTTP 客户端前停止。");
        }
    }

    private sealed class TrackingHttpContent : HttpContent
    {
        private readonly byte[] _body;

        public TrackingHttpContent(string body)
        {
            _body = Encoding.UTF8.GetBytes(body);
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/json"
            );
        }

        public int SerializeCount { get; private set; }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context
        )
        {
            SerializeCount++;
            await stream.WriteAsync(_body);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _body.Length;
            return true;
        }
    }
}
