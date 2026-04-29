using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Infrastructure.Configuration;

namespace CodexCliPlus.Tests.Backend;

[Collection("BackendProcessManager")]
public sealed class BackendConfigWriterTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-backend-config-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task WriteAsyncGeneratesLoopbackOnlyConfigAndManagementKey()
    {
        var pathService = new TestPathService(_rootDirectory);
        var configurationService = new JsonAppConfigurationService(pathService);
        var writer = new BackendConfigWriter(configurationService, pathService);
        var settings = new AppSettings
        {
            BackendPort = 9317,
            PreferredCodexSource = CodexSourceKind.Official,
        };

        var runtime = await writer.WriteAsync(settings);
        var yaml = await File.ReadAllTextAsync(pathService.Directories.BackendConfigFilePath);
        var desktopJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);
        var secondRuntime = await writer.WriteAsync(settings);
        var secondYaml = await File.ReadAllTextAsync(pathService.Directories.BackendConfigFilePath);

        Assert.Equal(AppConstants.DefaultBackendPort, runtime.RequestedPort);
        Assert.Equal(AppConstants.DefaultBackendPort, runtime.Port);
        Assert.False(runtime.PortWasAdjusted);
        Assert.Equal($"http://127.0.0.1:{AppConstants.DefaultBackendPort}", runtime.BaseUrl);
        Assert.Equal(
            $"http://127.0.0.1:{AppConstants.DefaultBackendPort}/healthz",
            runtime.HealthUrl
        );
        Assert.False(string.IsNullOrWhiteSpace(runtime.ManagementKey));
        Assert.DoesNotContain(runtime.ManagementKey, yaml, StringComparison.Ordinal);
        Assert.Contains("host: \"127.0.0.1\"", yaml, StringComparison.Ordinal);
        Assert.Contains($"port: {AppConstants.DefaultBackendPort}", yaml, StringComparison.Ordinal);
        Assert.Contains("allow-remote: false", yaml, StringComparison.Ordinal);
        Assert.Contains("secret-key:", yaml, StringComparison.Ordinal);
        Assert.Contains("disable-control-panel: true", yaml, StringComparison.Ordinal);
        Assert.Contains(
            $"auth-dir: \"{EscapeYaml(Path.Combine(_rootDirectory, "backend", "auth"))}\"",
            yaml,
            StringComparison.Ordinal
        );
        Assert.Contains("\"backendPort\": 1327", desktopJson, StringComparison.Ordinal);
        Assert.DoesNotContain("9317", desktopJson, StringComparison.Ordinal);
        Assert.DoesNotContain(".cli-proxy-api", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("panel-github-repository", yaml, StringComparison.Ordinal);

        var match = Regex.Match(yaml, "secret-key: \"(?<hash>.+)\"");
        var secondMatch = Regex.Match(secondYaml, "secret-key: \"(?<hash>.+)\"");
        Assert.True(match.Success);
        Assert.True(secondMatch.Success);
        Assert.True(BCrypt.Net.BCrypt.Verify(runtime.ManagementKey, match.Groups["hash"].Value));
        Assert.Equal(match.Groups["hash"].Value, secondMatch.Groups["hash"].Value);
        Assert.Equal(runtime.ManagementKey, secondRuntime.ManagementKey);
    }

    [Fact]
    public async Task WriteAsyncBlocksWhenFixedPortIsOccupied()
    {
        var pathService = new TestPathService(_rootDirectory);
        var configurationService = new JsonAppConfigurationService(pathService);
        var writer = new BackendConfigWriter(configurationService, pathService);
        using var listener = TryListenOnDefaultPort();
        if (listener is null)
        {
            Assert.Skip(
                $"CodexCliPlus backend port {AppConstants.DefaultBackendPort} is already occupied."
            );
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.WriteAsync(new AppSettings { BackendPort = 9327 })
        );
        var yaml = await File.ReadAllTextAsync(pathService.Directories.BackendConfigFilePath);

        Assert.Contains(
            "CodexCliPlus backend port 1327 is already in use",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains($"port: {AppConstants.DefaultBackendPort}", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("9327", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsyncRejectsWrongExistingManagementKeyWithoutRotatingHash()
    {
        var pathService = new TestPathService(_rootDirectory);
        var configurationService = new JsonAppConfigurationService(pathService);
        var writer = new BackendConfigWriter(configurationService, pathService);
        var initialSettings = new AppSettings
        {
            ManagementKey = "correct-management-key",
            SecurityKeyOnboardingCompleted = false,
        };

        await writer.WriteAsync(
            initialSettings,
            new BackendConfigWriteOptions { AllowManagementKeyRotation = true }
        );
        var originalYaml = await File.ReadAllTextAsync(
            pathService.Directories.BackendConfigFilePath
        );
        var originalHash = Regex
            .Match(originalYaml, "secret-key: \"(?<hash>.+)\"")
            .Groups["hash"]
            .Value;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.WriteAsync(
                new AppSettings
                {
                    ManagementKey = "wrong-management-key",
                    SecurityKeyOnboardingCompleted = true,
                }
            )
        );
        var finalYaml = await File.ReadAllTextAsync(pathService.Directories.BackendConfigFilePath);
        var finalHash = Regex.Match(finalYaml, "secret-key: \"(?<hash>.+)\"").Groups["hash"].Value;

        Assert.Contains("不会被重写", exception.Message, StringComparison.Ordinal);
        Assert.Equal(originalHash, finalHash);
        Assert.True(writer.HasExistingManagementKeyHash());
        Assert.True(writer.VerifyManagementKey("correct-management-key"));
        Assert.False(writer.VerifyManagementKey("wrong-management-key"));
    }

    [Fact]
    public async Task WriteAsyncRejectsCompletedSettingsWithoutExistingHash()
    {
        var pathService = new TestPathService(_rootDirectory);
        var configurationService = new JsonAppConfigurationService(pathService);
        var writer = new BackendConfigWriter(configurationService, pathService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.WriteAsync(
                new AppSettings
                {
                    ManagementKey = "remembered-key",
                    SecurityKeyOnboardingCompleted = true,
                }
            )
        );

        Assert.Contains(
            "普通登录不会生成或重置安全密钥",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(File.Exists(pathService.Directories.BackendConfigFilePath));
    }

    private static TcpListener? TryListenOnDefaultPort()
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, AppConstants.DefaultBackendPort);
            listener.Start();
            return listener;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static string EscapeYaml(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
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
}
