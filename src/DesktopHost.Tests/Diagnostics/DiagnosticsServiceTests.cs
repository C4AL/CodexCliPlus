using DesktopHost.Core.Abstractions.Build;
using DesktopHost.Core.Abstractions.Paths;
using DesktopHost.Core.Models;
using DesktopHost.Infrastructure.Diagnostics;

namespace DesktopHost.Tests.Diagnostics;

public sealed class DiagnosticsServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"cpad-diagnostics-{Guid.NewGuid():N}");

    [Fact]
    public void CreateErrorSnapshotWritesSnapshotFile()
    {
        var pathService = new TestPathService(_rootDirectory);
        var service = new DiagnosticsService(pathService, new TestBuildInfo());

        var snapshotPath = service.CreateErrorSnapshot(
            "测试错误",
            "这里是详细信息。",
            new InvalidOperationException("boom"),
            new BackendStatusSnapshot
            {
                Message = "后端异常"
            },
            new CodexStatusSnapshot
            {
                AuthenticationState = "unknown"
            },
            new DependencyCheckResult
            {
                Summary = "已安装"
            });

        var content = File.ReadAllText(snapshotPath);

        Assert.True(File.Exists(snapshotPath));
        Assert.Contains("测试错误", content, StringComparison.Ordinal);
        Assert.Contains("这里是详细信息。", content, StringComparison.Ordinal);
        Assert.Contains("boom", content, StringComparison.Ordinal);
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

            return Task.CompletedTask;
        }
    }
}
