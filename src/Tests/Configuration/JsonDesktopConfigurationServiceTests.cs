using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Exceptions;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Configuration;

namespace CodexCliPlus.Tests.Configuration;

[Trait("Category", "LocalIntegration")]
public sealed class JsonAppConfigurationServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-config-test-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task SaveAndLoadAsyncStoresManagementKeyOutsideDesktopJson()
    {
        var pathService = new TestPathService(_rootDirectory);
        var service = new JsonAppConfigurationService(pathService);
        var expected = new AppSettings
        {
            BackendPort = 9417,
            ManagementKey = "test-key",
            ManagementKeyReference = "desktop-management-key",
            RememberPassword = true,
            AutoLogin = false,
            PreferredCodexSource = CodexSourceKind.Cpa,
            ThemeMode = AppThemeMode.Dark,
            MinimumLogLevel = AppLogLevel.Warning,
            EnableDebugTools = true,
            EnableLocalRepairDebugReport = true,
            LastRepositoryPath = @"C:\repo",
            SecurityKeyOnboardingCompleted = true,
            LastSeenApplicationVersion = "1.2.3",
        };

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();
        var persistedJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);

        Assert.Equal(AppConstants.DefaultBackendPort, actual.BackendPort);
        Assert.Equal("test-key", actual.ManagementKey);
        Assert.Equal("desktop-management-key", actual.ManagementKeyReference);
        Assert.True(actual.RememberPassword);
        Assert.False(actual.AutoLogin);
        Assert.Equal(CodexSourceKind.Cpa, actual.PreferredCodexSource);
        Assert.Equal(AppThemeMode.Dark, actual.ThemeMode);
        Assert.Equal(AppLogLevel.Warning, actual.MinimumLogLevel);
        Assert.True(actual.EnableDebugTools);
        Assert.True(actual.EnableLocalRepairDebugReport);
        Assert.Equal(@"C:\repo", actual.LastRepositoryPath);
        Assert.True(actual.SecurityKeyOnboardingCompleted);
        Assert.Equal("1.2.3", actual.LastSeenApplicationVersion);
        Assert.DoesNotContain("test-key", persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"backendPort\": 1327", persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("9417", persistedJson, StringComparison.Ordinal);
        Assert.Contains("managementKeyReference", persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"rememberPassword\": true", persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"autoLogin\": false", persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"enableLocalRepairDebugReport\": true", persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("rememberManagementKey", persistedJson, StringComparison.Ordinal);
        Assert.Contains(
            "\"securityKeyOnboardingCompleted\": true",
            persistedJson,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "\"lastSeenApplicationVersion\": \"1.2.3\"",
            persistedJson,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task LoadAsyncMigratesLegacyPlaintextManagementKey()
    {
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();

        await File.WriteAllTextAsync(
            pathService.Directories.SettingsFilePath,
            """
            {
              "backendPort": 9527,
              "managementKey": "legacy-secret",
              "themeMode": "Light"
            }
            """
        );

        var service = new JsonAppConfigurationService(pathService);
        var settings = await service.LoadAsync();
        var persistedJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);

        Assert.Equal(AppConstants.DefaultBackendPort, settings.BackendPort);
        Assert.Equal("legacy-secret", settings.ManagementKey);
        Assert.Equal(AppConstants.DefaultManagementKeyReference, settings.ManagementKeyReference);
        Assert.Equal(AppThemeMode.White, settings.ThemeMode);
        Assert.True(settings.RememberPassword);
        Assert.True(settings.AutoLogin);
        Assert.True(settings.SecurityKeyOnboardingCompleted);
        Assert.DoesNotContain("legacy-secret", persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"backendPort\": 1327", persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("9527", persistedJson, StringComparison.Ordinal);
        Assert.Contains("managementKeyReference", persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"rememberPassword\": true", persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"autoLogin\": true", persistedJson, StringComparison.Ordinal);
        Assert.True(
            File.Exists(
                Path.Combine(
                    pathService.Directories.ConfigDirectory,
                    "secrets",
                    "management-key.bin"
                )
            )
        );
    }

    [Fact]
    public async Task SaveAndLoadAsyncPreservesShellAndUpdatePreferences()
    {
        var pathService = new TestPathService(_rootDirectory);
        var service = new JsonAppConfigurationService(pathService);
        var expected = new AppSettings
        {
            BackendPort = AppConstants.DefaultBackendPort,
            StartWithWindows = true,
            MinimizeToTrayOnClose = false,
            EnableTrayIcon = false,
            CheckForUpdatesOnStartup = false,
            UseBetaChannel = true,
            ThemeMode = AppThemeMode.White,
            MinimumLogLevel = AppLogLevel.Error,
            EnableDebugTools = true,
            EnableLocalRepairDebugReport = true,
        };

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();

        Assert.True(actual.StartWithWindows);
        Assert.False(actual.MinimizeToTrayOnClose);
        Assert.False(actual.EnableTrayIcon);
        Assert.False(actual.CheckForUpdatesOnStartup);
        Assert.True(actual.UseBetaChannel);
        Assert.Equal(AppThemeMode.White, actual.ThemeMode);
        Assert.Equal(AppLogLevel.Error, actual.MinimumLogLevel);
        Assert.True(actual.EnableDebugTools);
        Assert.True(actual.EnableLocalRepairDebugReport);
    }

    [Fact]
    public async Task SaveAsyncNormalizesAutoLoginWhenRememberPasswordIsDisabled()
    {
        var pathService = new TestPathService(_rootDirectory);
        var service = new JsonAppConfigurationService(pathService);
        var settings = new AppSettings
        {
            ManagementKey = "session-only-key",
            RememberPassword = false,
            AutoLogin = true,
        };

        await service.SaveAsync(settings);

        var actual = await new JsonAppConfigurationService(pathService).LoadAsync();
        var persistedJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);

        Assert.False(actual.RememberPassword);
        Assert.False(actual.AutoLogin);
        Assert.Empty(actual.ManagementKey);
        Assert.Contains("\"rememberPassword\": false", persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"autoLogin\": false", persistedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsyncDeletesStoredManagementKeyWithoutCallerCancellationToken()
    {
        var pathService = new TestPathService(_rootDirectory);
        var credentialStore = new CapturingCredentialStore();
        var service = new JsonAppConfigurationService(pathService, credentialStore);
        using var cancellation = new CancellationTokenSource();
        var settings = new AppSettings
        {
            ManagementKey = "session-only-key",
            ManagementKeyReference = "stored-management-key",
            RememberPassword = false,
            AutoLogin = true,
        };

        await service.SaveAsync(settings, cancellation.Token);

        var deleteToken = Assert.Single(credentialStore.DeleteTokens);
        Assert.False(deleteToken.CanBeCanceled);
    }

    [Fact]
    public async Task SaveAsyncKeepsRememberedSettingsWhenStoredKeyDeleteFails()
    {
        var pathService = new TestPathService(_rootDirectory);
        var credentialStore = new CapturingCredentialStore();
        var service = new JsonAppConfigurationService(pathService, credentialStore);
        const string reference = "delete-failure-key";
        await service.SaveAsync(
            new AppSettings
            {
                ManagementKey = "remembered-key",
                ManagementKeyReference = reference,
                RememberPassword = true,
                AutoLogin = true,
            }
        );
        credentialStore.FailDelete = true;

        var exception = await Assert.ThrowsAsync<SecureCredentialStoreException>(() =>
            service.SaveAsync(
                new AppSettings
                {
                    ManagementKey = "session-only-key",
                    ManagementKeyReference = reference,
                    RememberPassword = false,
                    AutoLogin = true,
                }
            )
        );
        var persistedJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);

        Assert.Contains("delete failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"rememberPassword\": true", persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"autoLogin\": true", persistedJson, StringComparison.Ordinal);
        Assert.Equal("remembered-key", await credentialStore.LoadSecretAsync(reference));
    }

    [Fact]
    public async Task SaveAsyncKeepsExistingSettingsWhenReplacementFails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pathService = new TestPathService(_rootDirectory);
        var service = new JsonAppConfigurationService(pathService);
        await service.SaveAsync(
            new AppSettings
            {
                CheckForUpdatesOnStartup = true,
                MinimizeToTrayOnClose = true,
                SecurityKeyOnboardingCompleted = true,
            }
        );
        var originalJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);
        await using var lockedSettings = new FileStream(
            pathService.Directories.SettingsFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );

        var exception = await Record.ExceptionAsync(() =>
            service.SaveAsync(
                new AppSettings
                {
                    ManagementKey = "session-only-key",
                    RememberPassword = false,
                    AutoLogin = true,
                    CheckForUpdatesOnStartup = false,
                    MinimizeToTrayOnClose = false,
                    SecurityKeyOnboardingCompleted = true,
                }
            )
        );

        Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
        Assert.Equal(
            originalJson,
            await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath)
        );
        Assert.Empty(
            Directory.EnumerateFiles(
                pathService.Directories.ConfigDirectory,
                "appsettings.json.*.tmp"
            )
        );
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, false)]
    public async Task LoadAsyncMigratesLegacyRememberManagementKey(
        bool legacyRememberManagementKey,
        bool hasSecret,
        bool expectedRememberPassword,
        bool expectedAutoLogin
    )
    {
        var pathService = new TestPathService(_rootDirectory);
        var service = new JsonAppConfigurationService(pathService);
        await pathService.EnsureCreatedAsync();
        if (hasSecret)
        {
            var seed = new AppSettings
            {
                ManagementKey = "legacy-secret",
                ManagementKeyReference = "legacy-key",
                RememberPassword = true,
                AutoLogin = true,
            };
            await service.SaveAsync(seed);
        }

        await File.WriteAllTextAsync(
            pathService.Directories.SettingsFilePath,
            $$"""
            {
              "backendPort": 1327,
              "managementKeyReference": "legacy-key",
              "rememberManagementKey": {{legacyRememberManagementKey.ToString().ToLowerInvariant()}}
            }
            """
        );

        var settings = await new JsonAppConfigurationService(pathService).LoadAsync();
        var persistedJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);

        Assert.Equal(expectedRememberPassword, settings.RememberPassword);
        Assert.Equal(expectedAutoLogin, settings.AutoLogin);
        Assert.Equal(expectedRememberPassword ? "legacy-secret" : string.Empty, settings.ManagementKey);
        Assert.Contains(
            $"\"rememberPassword\": {expectedRememberPassword.ToString().ToLowerInvariant()}",
            persistedJson,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"\"autoLogin\": {expectedAutoLogin.ToString().ToLowerInvariant()}",
            persistedJson,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("rememberManagementKey", persistedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsyncUsesFixedDefaultBackendPortWhenSettingsFileIsMissing()
    {
        var pathService = new TestPathService(_rootDirectory);
        var service = new JsonAppConfigurationService(pathService);

        var actual = await service.LoadAsync();

        Assert.Equal(1327, AppConstants.DefaultBackendPort);
        Assert.Equal(AppConstants.DefaultBackendPort, actual.BackendPort);
        Assert.False(actual.SecurityKeyOnboardingCompleted);
        Assert.Null(actual.LastSeenApplicationVersion);
    }

    [Fact]
    public async Task LoadAsyncIsolatesInvalidSettingsJsonAndUsesDefaults()
    {
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        await File.WriteAllTextAsync(pathService.Directories.SettingsFilePath, "{ not-json");
        var service = new JsonAppConfigurationService(pathService);

        var actual = await service.LoadAsync();

        Assert.Equal(AppConstants.DefaultBackendPort, actual.BackendPort);
        Assert.True(actual.CheckForUpdatesOnStartup);
        Assert.False(actual.SecurityKeyOnboardingCompleted);
        Assert.False(File.Exists(pathService.Directories.SettingsFilePath));
        Assert.Single(
            Directory.EnumerateFiles(
                pathService.Directories.ConfigDirectory,
                "appsettings.json.invalid.*"
            )
        );

        var secondLoad = await service.LoadAsync();

        Assert.Equal(AppConstants.DefaultBackendPort, secondLoad.BackendPort);
        Assert.Single(
            Directory.EnumerateFiles(
                pathService.Directories.ConfigDirectory,
                "appsettings.json.invalid.*"
            )
        );
    }

    [Fact]
    public async Task LoadAsyncTreatsLegacySettingsWithoutOnboardingFieldAsCompleted()
    {
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        await File.WriteAllTextAsync(
            pathService.Directories.SettingsFilePath,
            """
            {
              "backendPort": 1327,
              "themeMode": "Light"
            }
            """
        );

        var service = new JsonAppConfigurationService(pathService);
        var settings = await service.LoadAsync();

        Assert.True(settings.SecurityKeyOnboardingCompleted);
        Assert.Equal(AppThemeMode.White, settings.ThemeMode);
        Assert.Null(settings.LastSeenApplicationVersion);
    }

    [Fact]
    public async Task LoadAndSaveAsyncNormalizePersistedFallbackBackendPort()
    {
        var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        await File.WriteAllTextAsync(
            pathService.Directories.SettingsFilePath,
            """
            {
              "backendPort": 1328,
              "themeMode": "Light"
            }
            """
        );

        var service = new JsonAppConfigurationService(pathService);
        var settings = await service.LoadAsync();
        await service.SaveAsync(settings);
        var persistedJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);

        Assert.Equal(AppConstants.DefaultBackendPort, settings.BackendPort);
        Assert.Equal(AppThemeMode.White, settings.ThemeMode);
        Assert.Contains("\"backendPort\": 1327", persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"themeMode\": \"White\"", persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("1328", persistedJson, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private sealed class CapturingCredentialStore : ISecureCredentialStore
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

        public List<CancellationToken> DeleteTokens { get; } = [];

        public bool FailDelete { get; set; }

        public Task SaveSecretAsync(
            string reference,
            string value,
            CancellationToken cancellationToken = default
        )
        {
            _secrets[reference] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadSecretAsync(
            string reference,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(
                _secrets.TryGetValue(reference, out var value) ? value : null
            );
        }

        public Task DeleteSecretAsync(
            string reference,
            CancellationToken cancellationToken = default
        )
        {
            DeleteTokens.Add(cancellationToken);
            if (FailDelete)
            {
                throw new SecureCredentialStoreException("delete failed");
            }

            _secrets.Remove(reference);
            return Task.CompletedTask;
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
