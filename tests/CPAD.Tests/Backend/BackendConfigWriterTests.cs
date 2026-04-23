using System.Net;
using System.Net.Sockets;

using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Enums;
using CPAD.Core.Models;
using CPAD.Infrastructure.Backend;
using CPAD.Infrastructure.Configuration;

namespace CPAD.Tests.Backend;

public sealed class BackendConfigWriterTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"cpad-backend-config-{Guid.NewGuid():N}");

    [Fact]
    public async Task WriteAsyncGeneratesLoopbackOnlyConfigAndManagementKey()
    {
        var pathService = new TestPathService(_rootDirectory);
        var configurationService = new JsonAppConfigurationService(pathService);
        var writer = new BackendConfigWriter(configurationService, pathService);
        var settings = new AppSettings
        {
            BackendPort = 9317,
            PreferredCodexSource = CodexSourceKind.Official
        };
        var assetLayout = new BackendAssetLayout(
            Path.Combine(_rootDirectory, "backend"),
            Path.Combine(_rootDirectory, "backend", "cli-proxy-api.exe"),
            Path.Combine(_rootDirectory, "backend", "static"),
            Path.Combine(_rootDirectory, "backend", "static", "management.html"));

        var runtime = await writer.WriteAsync(settings, assetLayout);
        var yaml = await File.ReadAllTextAsync(pathService.Directories.BackendConfigFilePath);

        Assert.Equal(9317, runtime.RequestedPort);
        Assert.Equal(9317, runtime.Port);
        Assert.False(runtime.PortWasAdjusted);
        Assert.Equal("http://127.0.0.1:9317", runtime.BaseUrl);
        Assert.Equal("http://127.0.0.1:9317/healthz", runtime.HealthUrl);
        Assert.False(string.IsNullOrWhiteSpace(runtime.ManagementKey));
        Assert.Contains("host: \"127.0.0.1\"", yaml, StringComparison.Ordinal);
        Assert.Contains("allow-remote: false", yaml, StringComparison.Ordinal);
        Assert.Contains("secret-key:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsyncUsesFallbackPortWhenPreferredPortIsOccupied()
    {
        var pathService = new TestPathService(_rootDirectory);
        var configurationService = new JsonAppConfigurationService(pathService);
        var writer = new BackendConfigWriter(configurationService, pathService);
        using var listener = new TcpListener(IPAddress.Loopback, 9327);
        listener.Start();

        var runtime = await writer.WriteAsync(
            new AppSettings { BackendPort = 9327 },
            new BackendAssetLayout(
                Path.Combine(_rootDirectory, "backend"),
                Path.Combine(_rootDirectory, "backend", "cli-proxy-api.exe"),
                Path.Combine(_rootDirectory, "backend", "static"),
                Path.Combine(_rootDirectory, "backend", "static", "management.html")));

        Assert.Equal(9327, runtime.RequestedPort);
        Assert.NotEqual(9327, runtime.Port);
        Assert.True(runtime.PortWasAdjusted);
        Assert.Contains("Preferred port 9327 was unavailable.", runtime.PortMessage, StringComparison.Ordinal);
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
                Path.Combine(rootDirectory, "config", "desktop.json"),
                Path.Combine(rootDirectory, "config", "cliproxyapi.yaml"));
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
            return Task.CompletedTask;
        }
    }
}
