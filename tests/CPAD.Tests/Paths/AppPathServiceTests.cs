using CPAD.Core.Constants;
using CPAD.Core.Enums;
using CPAD.Infrastructure.Paths;

namespace CPAD.Tests.Paths;

public sealed class AppPathServiceTests
{
    [Fact]
    public void DirectoriesUseExpectedLocalApplicationDataLayout()
    {
        var service = new AppPathService();

        Assert.Equal(AppDataMode.Installed, service.Directories.DataMode);
        Assert.Contains(AppConstants.ProductKey, service.Directories.RootDirectory);
        Assert.EndsWith(AppConstants.AppSettingsFileName, service.Directories.SettingsFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(AppConstants.BackendConfigFileName, service.Directories.BackendConfigFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("logs", service.Directories.LogsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagnostics", service.Directories.DiagnosticsDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectoriesUseOverrideRootWhenEnvironmentVariableIsSet()
    {
        var originalRoot = Environment.GetEnvironmentVariable("CPAD_APP_ROOT");
        var overrideRoot = Path.Combine(Path.GetTempPath(), $"cpad-path-override-{Guid.NewGuid():N}");

        try
        {
            Environment.SetEnvironmentVariable("CPAD_APP_ROOT", overrideRoot);
            var service = new AppPathService();

            Assert.Equal(Path.GetFullPath(overrideRoot), service.Directories.RootDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "logs"), service.Directories.LogsDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CPAD_APP_ROOT", originalRoot);
        }
    }

    [Fact]
    public void DirectoriesUseDevelopmentModeWhenRequested()
    {
        var originalMode = Environment.GetEnvironmentVariable("CPAD_APP_MODE");

        try
        {
            Environment.SetEnvironmentVariable("CPAD_APP_MODE", "development");
            var service = new AppPathService();

            Assert.Equal(AppDataMode.Development, service.Directories.DataMode);
            Assert.Contains(Path.Combine("artifacts", "dev-data"), service.Directories.RootDirectory, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CPAD_APP_MODE", originalMode);
        }
    }
}
