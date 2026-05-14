using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.Security;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Infrastructure.Configuration;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Tests.Backend;

[Collection("BackendProcessManager")]
[Trait("Category", "LocalIntegration")]
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
        Assert.Contains(
            "redis-usage-queue-retention-seconds: 60",
            yaml,
            StringComparison.Ordinal
        );

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

        var settings = new AppSettings { BackendPort = 9327 };
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.WriteAsync(settings)
        );

        Assert.Contains(
            "CodexCliPlus backend port 1327 is already in use",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(9327, settings.BackendPort);
        Assert.True(string.IsNullOrWhiteSpace(settings.ManagementKey));
        Assert.False(File.Exists(pathService.Directories.BackendConfigFilePath));
        Assert.False(File.Exists(pathService.Directories.SettingsFilePath));
        Assert.False(Directory.Exists(Path.Combine(pathService.Directories.BackendDirectory, "auth")));
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

    [Theory]
    [InlineData("")]
    [InlineData("\"")]
    [InlineData("'")]
    public async Task WriteAsyncAcceptsExistingManagementKeyHashWithYamlScalarStyles(
        string quote
    )
    {
        const string managementKey = "existing-management-key";
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        var hash = BCrypt.Net.BCrypt.HashPassword(managementKey);
        var scalar = string.IsNullOrEmpty(quote) ? hash : $"{quote}{hash}{quote}";
        await File.WriteAllTextAsync(
            pathService.Directories.BackendConfigFilePath,
            $"remote-management:{Environment.NewLine}  secret-key: {scalar}{Environment.NewLine}"
        );
        var configurationService = new JsonAppConfigurationService(pathService);
        var writer = new BackendConfigWriter(configurationService, pathService);

        var runtime = await writer.WriteAsync(
            new AppSettings
            {
                ManagementKey = managementKey,
                SecurityKeyOnboardingCompleted = true,
            },
            new BackendConfigWriteOptions { ValidatePort = false }
        );

        var yaml = await File.ReadAllTextAsync(pathService.Directories.BackendConfigFilePath);
        Assert.Equal(managementKey, runtime.ManagementKey);
        Assert.True(writer.HasExistingManagementKeyHash());
        Assert.True(writer.VerifyManagementKey(managementKey));
        Assert.Contains($"secret-key: \"{hash}\"", yaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteAsyncAcceptsExistingManagementKeySecretReference(
        bool storeHashInVault
    )
    {
        const string managementKey = "existing-management-key";
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        using var vault = new DpapiSecretVault(pathService);
        var storedSecret = storeHashInVault
            ? BCrypt.Net.BCrypt.HashPassword(managementKey)
            : managementKey;
        var record = await vault.SaveSecretAsync(
            SecretKind.ManagementKey,
            storedSecret,
            "backend.yaml",
            metadata: null
        );
        await File.WriteAllTextAsync(
            pathService.Directories.BackendConfigFilePath,
            $"remote-management:{Environment.NewLine}  secret-key: \"{new SecretRef(record.SecretId).Uri}\"{Environment.NewLine}"
        );
        var configurationService = new JsonAppConfigurationService(pathService);
        var writer = new BackendConfigWriter(configurationService, pathService, vault);

        var runtime = await writer.WriteAsync(
            new AppSettings
            {
                ManagementKey = managementKey,
                SecurityKeyOnboardingCompleted = true,
            },
            new BackendConfigWriteOptions { ValidatePort = false }
        );

        var yaml = await File.ReadAllTextAsync(pathService.Directories.BackendConfigFilePath);
        var finalHash = Regex.Match(yaml, "secret-key: \"(?<hash>.+)\"").Groups["hash"].Value;
        Assert.Equal(managementKey, runtime.ManagementKey);
        Assert.True(writer.HasExistingManagementKeyHash());
        Assert.True(writer.VerifyManagementKey(managementKey));
        Assert.True(BCrypt.Net.BCrypt.Verify(managementKey, finalHash));
        Assert.DoesNotContain("ccp-secret://", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain(managementKey, yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsyncDoesNotRewriteUnchangedConfigOrSettings()
    {
        var pathService = new TestPathService(_rootDirectory);
        var configurationService = new JsonAppConfigurationService(pathService);
        var writer = new BackendConfigWriter(configurationService, pathService);
        var settings = new AppSettings
        {
            ManagementKey = "stable-management-key",
            SecurityKeyOnboardingCompleted = false,
        };
        var options = new BackendConfigWriteOptions
        {
            AllowManagementKeyRotation = true,
            ValidatePort = false,
        };

        await writer.WriteAsync(settings, options);
        var yamlTimestamp = DateTime.UtcNow.AddMinutes(-10);
        var settingsTimestamp = DateTime.UtcNow.AddMinutes(-9);
        File.SetLastWriteTimeUtc(pathService.Directories.BackendConfigFilePath, yamlTimestamp);
        File.SetLastWriteTimeUtc(pathService.Directories.SettingsFilePath, settingsTimestamp);
        var expectedYamlTimestamp = File.GetLastWriteTimeUtc(
            pathService.Directories.BackendConfigFilePath
        );
        var expectedSettingsTimestamp = File.GetLastWriteTimeUtc(
            pathService.Directories.SettingsFilePath
        );

        await writer.WriteAsync(settings, options);

        Assert.Equal(
            expectedYamlTimestamp,
            File.GetLastWriteTimeUtc(pathService.Directories.BackendConfigFilePath)
        );
        Assert.Equal(
            expectedSettingsTimestamp,
            File.GetLastWriteTimeUtc(pathService.Directories.SettingsFilePath)
        );
    }

    [Fact]
    public async Task WriteAsyncKeepsExistingConfigWhenReplacementFails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string managementKey = "existing-management-key";
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        var hash = BCrypt.Net.BCrypt.HashPassword(managementKey);
        var originalYaml =
            $"remote-management:{Environment.NewLine}  secret-key: \"{hash}\"{Environment.NewLine}";
        await File.WriteAllTextAsync(pathService.Directories.BackendConfigFilePath, originalYaml);
        var configurationService = new JsonAppConfigurationService(pathService);
        var writer = new BackendConfigWriter(configurationService, pathService);
        await using var lockedConfig = new FileStream(
            pathService.Directories.BackendConfigFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );

        var exception = await Record.ExceptionAsync(() =>
            writer.WriteAsync(
                new AppSettings
                {
                    ManagementKey = managementKey,
                    SecurityKeyOnboardingCompleted = true,
                },
                new BackendConfigWriteOptions { ValidatePort = false }
            )
        );

        Assert.True(exception is IOException or UnauthorizedAccessException);
        Assert.Equal(
            originalYaml,
            await File.ReadAllTextAsync(pathService.Directories.BackendConfigFilePath)
        );
        Assert.Empty(
            Directory.EnumerateFiles(
                pathService.Directories.ConfigDirectory,
                "backend.yaml.*.tmp"
            )
        );
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
