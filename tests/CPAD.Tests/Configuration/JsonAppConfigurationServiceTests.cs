using CPAD.Core.Enums;
using CPAD.Core.Models;
using CPAD.Infrastructure.Configuration;
using CPAD.Infrastructure.Paths;
using CPAD.Tests.Paths;

namespace CPAD.Tests.Configuration;

[Collection("AppPathServiceEnvironment")]
public sealed class JsonAppConfigurationServicePathModeTests
{
    [Theory]
    [InlineData(AppPathServiceEnvironmentScope.PortableMarkerFileName, AppDataMode.Portable)]
    [InlineData(AppPathServiceEnvironmentScope.DevelopmentMarkerFileName, AppDataMode.Development)]
    public async Task SaveAndLoadAsyncRoundTripsSettingsWhenModeIsDetectedFromPackageMarker(
        string markerFileName,
        AppDataMode expectedMode)
    {
        using var scope = new AppPathServiceEnvironmentScope();
        var overrideRoot = scope.CreateTemporaryRoot("cpad-config-path");
        AppPathServiceEnvironmentScope.SetMarker(markerFileName);
        AppPathServiceEnvironmentScope.SetRootOverride(overrideRoot);

        var pathService = new AppPathService();
        var service = new JsonAppConfigurationService(pathService);
        var expected = new AppSettings
        {
            BackendPort = 8517,
            ManagementKey = "test-key",
            ManagementKeyReference = "managed-by-marker",
            PreferredCodexSource = CodexSourceKind.Cpa,
            ThemeMode = AppThemeMode.Dark,
            CheckForUpdatesOnStartup = false,
            UseBetaChannel = true
        };

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();
        var persistedJson = await File.ReadAllTextAsync(pathService.Directories.SettingsFilePath);

        Assert.Equal(expectedMode, pathService.Directories.DataMode);
        Assert.Equal(Path.GetFullPath(overrideRoot), pathService.Directories.RootDirectory);
        Assert.Equal(expected.BackendPort, actual.BackendPort);
        Assert.Equal(expected.ManagementKey, actual.ManagementKey);
        Assert.Equal(expected.ManagementKeyReference, actual.ManagementKeyReference);
        Assert.Equal(expected.PreferredCodexSource, actual.PreferredCodexSource);
        Assert.Equal(expected.ThemeMode, actual.ThemeMode);
        Assert.False(actual.CheckForUpdatesOnStartup);
        Assert.True(actual.UseBetaChannel);
        Assert.StartsWith(Path.GetFullPath(overrideRoot), pathService.Directories.SettingsFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(expected.ManagementKey, persistedJson, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(pathService.Directories.ConfigDirectory, "secrets", "managed-by-marker.bin")));
    }
}
