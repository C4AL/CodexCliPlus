using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Configuration;
using CodexCliPlus.Infrastructure.Paths;
using CodexCliPlus.Tests.Paths;

namespace CodexCliPlus.Tests.Configuration;

[Collection("AppPathServiceEnvironment")]
public sealed class JsonAppConfigurationServicePathModeTests
{
    [Theory]
    [InlineData(null, AppDataMode.Installed)]
    [InlineData("development", AppDataMode.Development)]
    public async Task SaveAndLoadAsyncRoundTripsSettingsWhenModeIsResolved(
        string? modeOverride,
        AppDataMode expectedMode)
    {
        using var scope = new AppPathServiceEnvironmentScope();
        var overrideRoot = scope.CreateTemporaryRoot("codexcliplus-config-path");
        AppPathServiceEnvironmentScope.SetModeOverride(modeOverride);
        AppPathServiceEnvironmentScope.SetRootOverride(overrideRoot);

        var pathService = new AppPathService();
        var service = new JsonAppConfigurationService(pathService);
        var expected = new AppSettings
        {
            BackendPort = 8517,
            ManagementKey = "test-key",
            ManagementKeyReference = "managed-by-marker",
            RememberManagementKey = true,
            PreferredCodexSource = CodexSourceKind.Cpa,
            ThemeMode = AppThemeMode.Dark,
            CheckForUpdatesOnStartup = false,
            UseBetaChannel = true,
            SecurityKeyOnboardingCompleted = true,
            LastSeenApplicationVersion = "2.0.0"
        };

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();
        var persistedJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);

        Assert.Equal(expectedMode, pathService.Directories.DataMode);
        Assert.Equal(Path.GetFullPath(overrideRoot), pathService.Directories.RootDirectory);
        Assert.Equal(CodexCliPlus.Core.Constants.AppConstants.DefaultBackendPort, actual.BackendPort);
        Assert.Equal(expected.ManagementKey, actual.ManagementKey);
        Assert.Equal(expected.ManagementKeyReference, actual.ManagementKeyReference);
        Assert.Equal(expected.PreferredCodexSource, actual.PreferredCodexSource);
        Assert.Equal(expected.ThemeMode, actual.ThemeMode);
        Assert.False(actual.CheckForUpdatesOnStartup);
        Assert.True(actual.UseBetaChannel);
        Assert.True(actual.SecurityKeyOnboardingCompleted);
        Assert.Equal("2.0.0", actual.LastSeenApplicationVersion);
        Assert.StartsWith(Path.GetFullPath(overrideRoot), pathService.Directories.SettingsFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(expected.ManagementKey, persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"backendPort\": 1327", persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("8517", persistedJson, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(pathService.Directories.ConfigDirectory, "secrets", "managed-by-marker.bin")));
    }

    [Fact]
    public async Task SaveAsyncDeletesStoredManagementKeyWhenRememberIsDisabled()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        var overrideRoot = scope.CreateTemporaryRoot("codexcliplus-config-remember");
        AppPathServiceEnvironmentScope.SetRootOverride(overrideRoot);

        var pathService = new AppPathService();
        var service = new JsonAppConfigurationService(pathService);
        var settings = new AppSettings
        {
            ManagementKey = "remembered-key",
            ManagementKeyReference = "remember-test",
            RememberManagementKey = true
        };

        await service.SaveAsync(settings);
        var secretPath = Path.Combine(pathService.Directories.ConfigDirectory, "secrets", "remember-test.bin");
        Assert.True(File.Exists(secretPath));

        settings.ManagementKey = "session-only-key";
        settings.RememberManagementKey = false;
        await service.SaveAsync(settings);

        var loadedInProcess = await service.LoadAsync();
        var loadedAfterRestart = await new JsonAppConfigurationService(pathService).LoadAsync();
        var persistedJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);

        Assert.False(File.Exists(secretPath));
        Assert.Equal("session-only-key", loadedInProcess.ManagementKey);
        Assert.Empty(loadedAfterRestart.ManagementKey);
        Assert.Contains("\"rememberManagementKey\": false", persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("session-only-key", persistedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsyncMigratesLegacyDpapiManagementKeyToRememberedState()
    {
        using var scope = new AppPathServiceEnvironmentScope();
        var overrideRoot = scope.CreateTemporaryRoot("codexcliplus-config-legacy-secret");
        AppPathServiceEnvironmentScope.SetRootOverride(overrideRoot);

        var pathService = new AppPathService();
        var store = new CodexCliPlus.Infrastructure.Security.DpapiCredentialStore(pathService);
        await pathService.EnsureCreatedAsync();
        await store.SaveSecretAsync("legacy-key", "legacy-secret");
        await File.WriteAllTextAsync(
            pathService.Directories.SettingsFilePath,
            """
            {
              "backendPort": 8317,
              "managementKeyReference": "legacy-key"
            }
            """);

        var settings = await new JsonAppConfigurationService(pathService).LoadAsync();
        var persistedJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);

        Assert.True(settings.RememberManagementKey);
        Assert.Equal("legacy-secret", settings.ManagementKey);
        Assert.True(settings.SecurityKeyOnboardingCompleted);
        Assert.Contains("\"rememberManagementKey\": true", persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"backendPort\": 1327", persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("8317", persistedJson, StringComparison.Ordinal);
    }
}
