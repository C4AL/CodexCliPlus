using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.Security;
using CodexCliPlus.Infrastructure.Logging;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Tests.Security;

[Trait("Category", "LocalIntegration")]
public sealed class SecretBrokerServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-secret-broker-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task BrokerOnlyReturnsActiveSecretsWithSessionToken()
    {
        var pathService = new TestPathService(_rootDirectory);
        var vault = new DpapiSecretVault(pathService);
        var logger = new FileAppLogger(pathService);
        using var broker = new SecretBrokerService(vault, logger);
        var record = await vault.SaveSecretAsync(SecretKind.ApiKey, "broker-secret", "unit-test");

        var session = await broker.StartAsync();
        using var client = new HttpClient();

        var unauthorized = await client.GetAsync($"{session.BaseUrl}/v1/secrets/{record.SecretId}");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{session.BaseUrl}/v1/secrets/{record.SecretId}"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        var authorized = await client.SendAsync(request);
        var payload = await authorized.Content.ReadAsStringAsync();

        await vault.SetSecretStatusAsync(record.SecretId, SecretStatus.Revoked);
        using var revokedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{session.BaseUrl}/v1/secrets/{record.SecretId}"
        );
        revokedRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            session.Token
        );
        var revoked = await client.SendAsync(revokedRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal("broker-secret", document.RootElement.GetProperty("value").GetString());
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
    }

    [Fact]
    public async Task BrokerKeepsServingAfterStartupCancellationTokenIsCanceled()
    {
        var pathService = new TestPathService(_rootDirectory);
        var vault = new DpapiSecretVault(pathService);
        var logger = new FileAppLogger(pathService);
        using var broker = new SecretBrokerService(vault, logger);
        var record = await vault.SaveSecretAsync(SecretKind.ApiKey, "broker-secret", "unit-test");
        using var startupCancellation = new CancellationTokenSource();

        var session = await broker.StartAsync(startupCancellation.Token);
        startupCancellation.Cancel();
        await Task.Delay(100);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{session.BaseUrl}/v1/secrets/{record.SecretId}"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal("broker-secret", document.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public async Task StartAsyncDisposesListenerWhenListenerStartFails()
    {
        var pathService = new TestPathService(_rootDirectory);
        var vault = new DpapiSecretVault(pathService);
        var logger = new FileAppLogger(pathService);
        var failure = new InvalidOperationException("start failed");
        var listener = new ThrowingSecretBrokerListener(failure);
        using var broker = new SecretBrokerService(vault, logger, () => listener);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            broker.StartAsync()
        );

        Assert.Same(failure, exception);
        Assert.True(listener.IsDisposed);
        Assert.Null(broker.CurrentSession);
    }

    [Fact]
    public async Task BrokerCanSaveSecretAndRevealReturnedReference()
    {
        var pathService = new TestPathService(_rootDirectory);
        var vault = new DpapiSecretVault(pathService);
        var logger = new FileAppLogger(pathService);
        using var broker = new SecretBrokerService(vault, logger);
        var session = await broker.StartAsync();
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{session.BaseUrl}/v1/secrets")
        {
            Content = new StringContent(
                """
                {"value":"desktop-secret","kind":"ApiKey","source":"unit-test","metadata":{"path":"api-keys[0]"}}
                """,
                Encoding.UTF8,
                "application/json"
            ),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

        var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        var uri = document.RootElement.GetProperty("uri").GetString();
        Assert.StartsWith("ccp-secret://", uri);
        Assert.Equal(
            "desktop-secret",
            await vault.RevealSecretAsync(
                uri!.Replace("ccp-secret://", string.Empty, StringComparison.Ordinal)
            )
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private sealed class TestPathService : IPathService
    {
        public TestPathService(string rootDirectory)
        {
            Directories = new AppDirectories(
                rootDirectory,
                Path.Combine(rootDirectory, "logs"),
                Path.Combine(rootDirectory, "config"),
                Path.Combine(rootDirectory, "backend"),
                Path.Combine(rootDirectory, "cache"),
                Path.Combine(rootDirectory, "config", "appsettings.json"),
                Path.Combine(rootDirectory, "config", "backend.yaml")
            );
        }

        public AppDirectories Directories { get; }

        public Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Directories.RootDirectory);
            Directory.CreateDirectory(Directories.LogsDirectory);
            Directory.CreateDirectory(Directories.ConfigDirectory);
            Directory.CreateDirectory(Directories.BackendDirectory);
            Directory.CreateDirectory(Directories.CacheDirectory);
            Directory.CreateDirectory(Directories.DiagnosticsDirectory);
            Directory.CreateDirectory(Directories.RuntimeDirectory);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSecretBrokerListener(Exception exception) : ISecretBrokerListener
    {
        public ICollection<string> Prefixes { get; } = [];

        public bool IsListening => false;

        public bool IsDisposed { get; private set; }

        public void Start()
        {
            throw exception;
        }

        public void Stop() { }

        public void Close() { }

        public Task<HttpListenerContext> GetContextAsync()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
