using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Logging;

namespace CodexCliPlus.Tests.Logging;

public sealed class FileAppLoggerTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"codexcliplus-logger-{Guid.NewGuid():N}");

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
                Path.Combine(rootDirectory, "config", "appsettings.json"),
                Path.Combine(rootDirectory, "config", "backend.yaml"));
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
