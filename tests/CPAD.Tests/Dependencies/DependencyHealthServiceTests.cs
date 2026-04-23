using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Abstractions.Security;
using CPAD.Core.Abstractions.Updates;
using CPAD.Core.Enums;
using CPAD.Core.Exceptions;
using CPAD.Core.Models;
using CPAD.Infrastructure.Dependencies;
using CPAD.Infrastructure.Platform;

namespace CPAD.Tests.Dependencies;

public sealed class DependencyHealthServiceTests : IDisposable
{
    private readonly List<string> _cleanupPaths = [];

    [Fact]
    public async Task EvaluateAsyncReturnsHealthyResultWhenManagedAssetsAreReady()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);

        var service = CreateService(pathService, new InMemoryCredentialStore(), new ReservedUpdateCheckService());

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.IsAvailable);
        Assert.False(result.RequiresRepairMode);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task EvaluateAsyncReturnsRepairModeWhenBackendExecutableIsMissing()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        Directory.CreateDirectory(pathService.Directories.BackendDirectory);

        var service = CreateService(pathService, new InMemoryCredentialStore(), new ReservedUpdateCheckService());

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.False(result.IsAvailable);
        Assert.True(result.RequiresRepairMode);
        Assert.Contains(result.Issues, issue => string.Equals(issue.Code, "backend-runtime", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsyncReturnsRepairModeWhenBackendExecutableFailsIntegrityValidation()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);
        await File.WriteAllTextAsync(
            Path.Combine(pathService.Directories.BackendDirectory, "cli-proxy-api.exe"),
            "tampered");

        var service = CreateService(pathService, new InMemoryCredentialStore(), new ReservedUpdateCheckService());

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        var issue = Assert.Single(result.Issues, issue => string.Equals(issue.Code, "backend-runtime", StringComparison.Ordinal));
        Assert.Contains("integrity validation", issue.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsyncReturnsRuntimeVersionIssueWhenDesktopRuntimeIsBelowMinimum()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);

        var service = CreateService(
            pathService,
            new InMemoryCredentialStore(),
            new ReservedUpdateCheckService(),
            frameworkDescription: ".NET 8.0.4");

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(result.Issues, issue => string.Equals(issue.Code, "runtime-version", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsyncReturnsCredentialStoreIssueWhenProbeFails()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);

        var service = CreateService(
            pathService,
            new ThrowingCredentialStore(),
            new ReservedUpdateCheckService());

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(result.Issues, issue => string.Equals(issue.Code, "credential-store", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsyncReturnsPortAllocationIssueWhenBackendStatusReportsExhaustion()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);

        var service = CreateService(pathService, new InMemoryCredentialStore(), new ReservedUpdateCheckService());

        var result = await service.EvaluateAsync(new BackendStatusSnapshot
        {
            State = BackendStateKind.Error,
            Message = "Backend failed to start.",
            LastError = "No available loopback port was found starting from 8317."
        });

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(result.Issues, issue => string.Equals(issue.Code, "port-allocation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsyncReturnsInitializationIssueWhenManagedStateIsPartial()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);
        Directory.Delete(Path.Combine(pathService.Directories.ConfigDirectory, "secrets"), recursive: true);

        var service = CreateService(pathService, new InMemoryCredentialStore(), new ReservedUpdateCheckService());

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(result.Issues, issue => string.Equals(issue.Code, "initialization", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsyncReturnsUpdateComponentIssueWhenReservedProbeIsInvalid()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);

        var service = CreateService(pathService, new InMemoryCredentialStore(), new InvalidUpdateCheckService());

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(result.Issues, issue => string.Equals(issue.Code, "update-component", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsyncReturnsResourcePackIssueWhenManagedResourceFileIsMissing()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);
        File.Delete(Path.Combine(pathService.Directories.BackendDirectory, "README_CN.md"));

        var service = CreateService(pathService, new InMemoryCredentialStore(), new ReservedUpdateCheckService());

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(result.Issues, issue => string.Equals(issue.Code, "resource-pack", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsyncReturnsDirectoryAccessIssueWhenManagedPathsAreNotWritable()
    {
        var blockedPath = Path.Combine(Path.GetTempPath(), $"cpad-dependency-blocked-{Guid.NewGuid():N}.txt");
        File.WriteAllText(blockedPath, "occupied");
        _cleanupPaths.Add(blockedPath);

        var pathService = new TestPathService(blockedPath);
        var service = CreateService(pathService, new InMemoryCredentialStore(), new ReservedUpdateCheckService());

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(result.Issues, issue => string.Equals(issue.Code, "directory-access", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        foreach (var path in _cleanupPaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    private string CreateRootDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cpad-dependency-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _cleanupPaths.Add(path);
        return path;
    }

    private static void SeedHealthyManagedState(TestPathService pathService)
    {
        pathService.EnsureCreatedAsync().GetAwaiter().GetResult();

        var repositoryAssets = FindRepositoryAssetsDirectory();
        foreach (var fileName in new[] { "cli-proxy-api.exe", "LICENSE", "README.md", "README_CN.md", "config.example.yaml" })
        {
            File.Copy(
                Path.Combine(repositoryAssets, fileName),
                Path.Combine(pathService.Directories.BackendDirectory, fileName),
                overwrite: true);
        }

        File.WriteAllText(pathService.Directories.SettingsFilePath, "{ }");
        File.WriteAllText(pathService.Directories.BackendConfigFilePath, "port: 8317");

        var secretsDirectory = Path.Combine(pathService.Directories.ConfigDirectory, "secrets");
        Directory.CreateDirectory(secretsDirectory);
        File.WriteAllBytes(Path.Combine(secretsDirectory, "management-key.bin"), [1, 2, 3, 4]);
    }

    private static DependencyHealthService CreateService(
        TestPathService pathService,
        ISecureCredentialStore credentialStore,
        IUpdateCheckService updateCheckService,
        string frameworkDescription = ".NET 10.0.0")
    {
        return new DependencyHealthService(
            pathService,
            new DirectoryAccessService(pathService),
            credentialStore,
            updateCheckService,
            () => frameworkDescription);
    }

    private static string FindRepositoryAssetsDirectory()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "CliProxyApiDesktop.sln")))
            {
                return Path.Combine(currentDirectory.FullName, "resources", "backend", "windows-x64");
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository backend assets.");
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

    private sealed class InMemoryCredentialStore : ISecureCredentialStore
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

        public Task SaveSecretAsync(string reference, string value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _secrets[reference] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadSecretAsync(string reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _secrets.TryGetValue(reference, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task DeleteSecretAsync(string reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _secrets.Remove(reference);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCredentialStore : ISecureCredentialStore
    {
        public Task SaveSecretAsync(string reference, string value, CancellationToken cancellationToken = default)
        {
            throw new SecureCredentialStoreException("DPAPI probe failed.");
        }

        public Task<string?> LoadSecretAsync(string reference, CancellationToken cancellationToken = default)
        {
            throw new SecureCredentialStoreException("DPAPI probe failed.");
        }

        public Task DeleteSecretAsync(string reference, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ReservedUpdateCheckService : IUpdateCheckService
    {
        public Task<UpdateCheckResult> CheckAsync(
            string currentVersion,
            UpdateChannel channel = UpdateChannel.Stable,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new UpdateCheckResult
            {
                Channel = channel,
                Repository = "Blackblock-inc/Cli-Proxy-API-Desktop",
                ApiUrl = "https://api.github.com/repos/Blackblock-inc/Cli-Proxy-API-Desktop/releases/latest",
                CurrentVersion = currentVersion,
                IsCheckSuccessful = true,
                IsChannelReserved = true,
                Status = "Beta reserved",
                Detail = "Reserved for later packaging phases.",
                ReleasePageUrl = "https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/releases"
            });
        }
    }

    private sealed class InvalidUpdateCheckService : IUpdateCheckService
    {
        public Task<UpdateCheckResult> CheckAsync(
            string currentVersion,
            UpdateChannel channel = UpdateChannel.Stable,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new UpdateCheckResult
            {
                Channel = channel,
                Repository = "Blackblock-inc/Cli-Proxy-API-Desktop",
                CurrentVersion = currentVersion,
                IsCheckSuccessful = false,
                IsChannelReserved = true,
                Status = "Metadata missing",
                Detail = "Release metadata was incomplete."
            });
        }
    }
}
