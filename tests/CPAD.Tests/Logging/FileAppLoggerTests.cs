using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Models;
using CPAD.Infrastructure.Logging;

namespace CPAD.Tests.Logging;

public sealed class FileAppLoggerTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"cpad-logger-{Guid.NewGuid():N}");

    [Fact]
    public void LogErrorRedactsSensitiveTokens()
    {
        var pathService = new TestPathService(_rootDirectory);
        var logger = new FileAppLogger(pathService);

        logger.LogError("authorization: Bearer token-123 secret-key=\"phase6-secret\"");
        var content = File.ReadAllText(logger.LogFilePath);

        Assert.DoesNotContain("token-123", content, StringComparison.Ordinal);
        Assert.DoesNotContain("phase6-secret", content, StringComparison.Ordinal);
        Assert.Contains("***", content, StringComparison.Ordinal);
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
}
