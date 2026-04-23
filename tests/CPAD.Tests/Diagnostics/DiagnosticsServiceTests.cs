using System.IO.Compression;

using CPAD.Core.Abstractions.Build;
using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Models;
using CPAD.Infrastructure.Diagnostics;

namespace CPAD.Tests.Diagnostics;

public sealed class DiagnosticsServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"cpad-diagnostics-{Guid.NewGuid():N}");

    [Fact]
    public void CreateErrorSnapshotWritesSnapshotFile()
    {
        var pathService = new TestPathService(_rootDirectory);
        var service = new DiagnosticsService(pathService, new TestBuildInfo());

        var snapshotPath = service.CreateErrorSnapshot(
            "Desktop startup failed",
            "The backend binary could not be launched during bootstrap. token=abc123",
            new InvalidOperationException("boom secret-key=phase6-secret"),
            new BackendStatusSnapshot
            {
                Message = "Backend failed to start."
            },
            new CodexStatusSnapshot
            {
                AuthenticationState = "unknown"
            },
            new DependencyCheckResult
            {
                Summary = "WebView2 Runtime is available."
            });

        var content = File.ReadAllText(snapshotPath);

        Assert.True(File.Exists(snapshotPath));
        Assert.Contains("Desktop startup failed", content, StringComparison.Ordinal);
        Assert.Contains("The backend binary could not be launched during bootstrap.", content, StringComparison.Ordinal);
        Assert.Contains("boom", content, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", content, StringComparison.Ordinal);
        Assert.DoesNotContain("phase6-secret", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportPackageWritesRedactedDiagnosticArchive()
    {
        var pathService = new TestPathService(_rootDirectory);
        pathService.EnsureCreatedAsync().GetAwaiter().GetResult();
        Directory.CreateDirectory(pathService.Directories.LogsDirectory);

        File.WriteAllText(
            Path.Combine(pathService.Directories.LogsDirectory, "desktop.log"),
            "authorization: Bearer test-token");
        File.WriteAllText(
            pathService.Directories.SettingsFilePath,
            "{ \"managementKeyReference\": \"management-key\" }");
        File.WriteAllText(
            pathService.Directories.BackendConfigFilePath,
            "remote-management:\n  secret-key: \"phase6-secret\"");

        var service = new DiagnosticsService(pathService, new TestBuildInfo());
        var packagePath = service.ExportPackage(
            new BackendStatusSnapshot { Message = "Running" },
            new CodexStatusSnapshot { AuthenticationState = "signed-in" },
            new DependencyCheckResult { Summary = "Dependency OK" });

        Assert.True(File.Exists(packagePath));

        using var archive = ZipFile.OpenRead(packagePath);
        var report = ReadEntryText(archive, "report.txt");
        var log = ReadEntryText(archive, "desktop.log");
        var backendConfig = ReadEntryText(archive, "cliproxyapi.yaml");

        Assert.Contains("Dependency OK", report, StringComparison.Ordinal);
        Assert.DoesNotContain("test-token", log, StringComparison.Ordinal);
        Assert.Contains("***", log, StringComparison.Ordinal);
        Assert.DoesNotContain("phase6-secret", backendConfig, StringComparison.Ordinal);
        Assert.Contains("***", backendConfig, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private sealed class TestBuildInfo : IBuildInfo
    {
        public string ApplicationVersion => "0.1.0";

        public string InformationalVersion => "0.1.0-test";
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
            Directory.CreateDirectory(Directories.DiagnosticsDirectory);
            Directory.CreateDirectory(Directories.RuntimeDirectory);

            return Task.CompletedTask;
        }
    }

    private static string ReadEntryText(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var stream = entry!.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
