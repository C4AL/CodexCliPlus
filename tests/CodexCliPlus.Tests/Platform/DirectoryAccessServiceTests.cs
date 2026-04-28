using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Platform;

namespace CodexCliPlus.Tests.Platform;

public sealed class DirectoryAccessServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"codexcliplus-dir-test-{Guid.NewGuid():N}");

    [Fact]
    public void GetWriteAccessErrorReturnsNullForWritableDirectory()
    {
        var service = CreateService();
        var writableDirectory = Path.Combine(_rootDirectory, "writable");

        var error = service.GetWriteAccessError(writableDirectory);

        Assert.Null(error);
        Assert.True(Directory.Exists(writableDirectory));
    }

    [Fact]
    public void GetWriteAccessErrorReturnsMessageForInvalidDirectoryTarget()
    {
        var service = CreateService();
        Directory.CreateDirectory(_rootDirectory);

        var filePath = Path.Combine(_rootDirectory, "occupied.txt");
        File.WriteAllText(filePath, "occupied");

        var error = service.GetWriteAccessError(filePath);

        Assert.NotNull(error);
        Assert.StartsWith("Directory is not writable: ", error, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private DirectoryAccessService CreateService()
    {
        return new DirectoryAccessService(new TestPathService(_rootDirectory));
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
