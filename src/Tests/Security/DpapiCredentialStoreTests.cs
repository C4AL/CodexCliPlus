using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Exceptions;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Tests.Security;

[Trait("Category", "LocalIntegration")]
public sealed class DpapiCredentialStoreTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-secure-store-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task SaveLoadAndDeleteAsyncRoundTripsSecretWithoutPlaintextFile()
    {
        var pathService = new TestPathService(_rootDirectory);
        var store = new DpapiCredentialStore(pathService);

        await store.SaveSecretAsync("phase6-test", "super-secret");
        var loaded = await store.LoadSecretAsync("phase6-test");
        var secretPath = Path.Combine(
            pathService.Directories.ConfigDirectory,
            "secrets",
            "phase6-test.bin"
        );
        var rawBytes = await File.ReadAllBytesAsync(secretPath);

        Assert.Equal("super-secret", loaded);
        Assert.True(File.Exists(secretPath));
        Assert.NotEqual("super-secret"u8.ToArray(), rawBytes);

        await store.DeleteSecretAsync("phase6-test");
        Assert.False(File.Exists(secretPath));
    }

    [Fact]
    public async Task SaveSecretAsyncKeepsExistingSecretWhenReplacementFails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pathService = new TestPathService(_rootDirectory);
        var store = new DpapiCredentialStore(pathService);
        const string reference = "locked-secret";
        await store.SaveSecretAsync(reference, "old-secret");
        var secretPath = Path.Combine(
            pathService.Directories.ConfigDirectory,
            "secrets",
            $"{reference}.bin"
        );
        var originalBytes = await File.ReadAllBytesAsync(secretPath);
        await using var lockedSecret = new FileStream(
            secretPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );

        var exception = await Record.ExceptionAsync(() =>
            store.SaveSecretAsync(reference, "new-secret")
        );

        var storeException = Assert.IsType<SecureCredentialStoreException>(exception);
        Assert.True(
            storeException.InnerException is IOException or UnauthorizedAccessException,
            storeException.ToString()
        );
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(secretPath));
        Assert.Equal("old-secret", await store.LoadSecretAsync(reference));
        Assert.Empty(
            Directory.EnumerateFiles(
                Path.GetDirectoryName(secretPath)!,
                $"{Path.GetFileName(secretPath)}.*.tmp"
            )
        );
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
                Path.Combine(rootDirectory, "config", "backend.yaml")
            );
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
