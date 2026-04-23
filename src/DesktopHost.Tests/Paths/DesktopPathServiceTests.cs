using DesktopHost.Core.Constants;
using DesktopHost.Infrastructure.Paths;

namespace DesktopHost.Tests.Paths;

public sealed class DesktopPathServiceTests
{
    [Fact]
    public void DirectoriesUseExpectedLocalApplicationDataLayout()
    {
        var service = new DesktopPathService();

        Assert.Contains(AppConstants.ProductName, service.Directories.RootDirectory);
        Assert.EndsWith(AppConstants.DesktopSettingsFileName, service.Directories.DesktopConfigFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(AppConstants.BackendConfigFileName, service.Directories.BackendConfigFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("logs", service.Directories.LogsDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectoriesUseOverrideRootWhenEnvironmentVariableIsSet()
    {
        var originalRoot = Environment.GetEnvironmentVariable("CPAD_APP_ROOT");
        var overrideRoot = Path.Combine(Path.GetTempPath(), $"cpad-path-override-{Guid.NewGuid():N}");

        try
        {
            Environment.SetEnvironmentVariable("CPAD_APP_ROOT", overrideRoot);
            var service = new DesktopPathService();

            Assert.Equal(Path.GetFullPath(overrideRoot), service.Directories.RootDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "logs"), service.Directories.LogsDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CPAD_APP_ROOT", originalRoot);
        }
    }
}
