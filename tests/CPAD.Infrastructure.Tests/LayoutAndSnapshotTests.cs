using CPAD.Infrastructure.Layout;
using CPAD.Infrastructure.Status;

namespace CPAD.Infrastructure.Tests;

public sealed class LayoutAndSnapshotTests
{
    [Fact]
    public void Resolve_UsesRepositoryIsolatedInstallRoot_WhenRunningFromRepository()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var scope = new EnvironmentScope(repositoryRoot);

        var layout = new CpadLayoutService().Resolve();

        Assert.Equal(Path.Combine(repositoryRoot, "tmp", "cpad-dev-install"), layout.InstallRoot);
    }

    [Fact]
    public async Task GetSnapshotAsync_UsesReferenceBundles_WhenStateFilesAreMissing()
    {
        var repositoryRoot = FindRepositoryRoot();
        var installRoot = Path.Combine(Path.GetTempPath(), "cpad-snapshot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);

        try
        {
            using var scope = new EnvironmentScope(repositoryRoot, installRoot);
            var snapshotService = new HostSnapshotService(new CpadLayoutService());

            var snapshot = await snapshotService.GetSnapshotAsync();

            Assert.Equal(installRoot, snapshot.InstallRoot);
            Assert.Equal("reference-bundle", snapshot.PluginMarket.CatalogSource);
            Assert.Contains(snapshot.PluginMarket.Plugins, plugin => plugin.Id == "edge-control" && plugin.SourceExists);
            Assert.Contains(snapshot.UpdateCenter.Sources, source => source.Id == "official-core-baseline" && source.Available);
            Assert.Contains(snapshot.UpdateCenter.Sources, source => source.Id == "cpa-source" && source.Available);
            Assert.DoesNotContain(snapshot.UpdateCenter.Sources, source => source.Id == "official-panel-baseline");
        }
        finally
        {
            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, true);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CPAD.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the CPAD repository root for tests.");
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly string? _originalRepositoryRoot = Environment.GetEnvironmentVariable("CPAD_REPO_ROOT");
        private readonly string? _originalInstallRoot = Environment.GetEnvironmentVariable("CPAD_INSTALL_ROOT");
        private readonly string _originalCurrentDirectory = Environment.CurrentDirectory;

        public EnvironmentScope(string repositoryRoot, string? installRoot = null)
        {
            Environment.SetEnvironmentVariable("CPAD_REPO_ROOT", repositoryRoot);
            Environment.SetEnvironmentVariable("CPAD_INSTALL_ROOT", installRoot);
            Environment.CurrentDirectory = repositoryRoot;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("CPAD_REPO_ROOT", _originalRepositoryRoot);
            Environment.SetEnvironmentVariable("CPAD_INSTALL_ROOT", _originalInstallRoot);
            Environment.CurrentDirectory = _originalCurrentDirectory;
        }
    }
}
