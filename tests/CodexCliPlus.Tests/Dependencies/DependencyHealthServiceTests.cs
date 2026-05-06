using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Abstractions.Updates;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Exceptions;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Dependencies;
using CodexCliPlus.Infrastructure.Platform;

namespace CodexCliPlus.Tests.Dependencies;

[Trait("Category", "LocalIntegration")]
public sealed class DependencyHealthServiceTests : IDisposable
{
    private static readonly string[] ManagedBackendFiles =
    [
        BackendExecutableNames.ManagedExecutableFileName,
        "LICENSE",
        "README.md",
        "README_CN.md",
        "config.example.yaml",
    ];

    private readonly List<string> _cleanupPaths = [];

    [Fact]
    public async Task EvaluateAsyncReturnsHealthyResultWhenManagedAssetsAreReady()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);

        var service = CreateService(
            pathService,
            new InMemoryCredentialStore(),
            new ReservedUpdateCheckService()
        );

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

        var service = CreateService(
            pathService,
            new InMemoryCredentialStore(),
            new ReservedUpdateCheckService()
        );

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.False(result.IsAvailable);
        Assert.True(result.RequiresRepairMode);
        Assert.Contains(
            result.Issues,
            issue => string.Equals(issue.Code, "backend-runtime", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task EvaluateAsyncReturnsRepairModeWhenBackendExecutableFailsIntegrityValidation()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);
        await File.WriteAllTextAsync(
            Path.Combine(
                pathService.Directories.BackendDirectory,
                BackendExecutableNames.ManagedExecutableFileName
            ),
            "tampered"
        );

        var service = CreateService(
            pathService,
            new InMemoryCredentialStore(),
            new ReservedUpdateCheckService()
        );

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        var issue = Assert.Single(
            result.Issues,
            issue => string.Equals(issue.Code, "backend-runtime", StringComparison.Ordinal)
        );
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
            frameworkDescription: ".NET 8.0.4"
        );

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(
            result.Issues,
            issue => string.Equals(issue.Code, "runtime-version", StringComparison.Ordinal)
        );
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
            new ReservedUpdateCheckService()
        );

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(
            result.Issues,
            issue => string.Equals(issue.Code, "credential-store", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task EvaluateAsyncReturnsPortAllocationIssueWhenBackendStatusReportsExhaustion()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);

        var service = CreateService(
            pathService,
            new InMemoryCredentialStore(),
            new ReservedUpdateCheckService()
        );

        var result = await service.EvaluateAsync(
            new BackendStatusSnapshot
            {
                State = BackendStateKind.Error,
                Message = "Backend failed to start.",
                LastError = FormattableString.Invariant(
                    $"CodexCliPlus backend port {AppConstants.DefaultBackendPort} is already in use. Stop the process using 127.0.0.1:{AppConstants.DefaultBackendPort} and start CodexCliPlus again."
                ),
            }
        );

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(
            result.Issues,
            issue => string.Equals(issue.Code, "port-allocation", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task EvaluateAsyncReturnsInitializationIssueWhenManagedStateIsPartial()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);
        File.Delete(pathService.Directories.SettingsFilePath);

        var service = CreateService(
            pathService,
            new InMemoryCredentialStore(),
            new ReservedUpdateCheckService()
        );

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(
            result.Issues,
            issue => string.Equals(issue.Code, "initialization", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task EvaluateAsyncReturnsUpdateComponentIssueWhenReservedProbeIsInvalid()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);

        var service = CreateService(
            pathService,
            new InMemoryCredentialStore(),
            new InvalidUpdateCheckService()
        );

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(
            result.Issues,
            issue => string.Equals(issue.Code, "update-component", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task EvaluateAsyncReturnsResourcePackIssueWhenManagedResourceFileIsMissing()
    {
        var rootDirectory = CreateRootDirectory();
        var pathService = new TestPathService(rootDirectory);
        SeedHealthyManagedState(pathService);
        File.Delete(Path.Combine(pathService.Directories.BackendDirectory, "README_CN.md"));

        var service = CreateService(
            pathService,
            new InMemoryCredentialStore(),
            new ReservedUpdateCheckService()
        );

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(
            result.Issues,
            issue => string.Equals(issue.Code, "resource-pack", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task EvaluateAsyncReturnsDirectoryAccessIssueWhenManagedPathsAreNotWritable()
    {
        var blockedPath = Path.Combine(
            Path.GetTempPath(),
            $"codexcliplus-dependency-blocked-{Guid.NewGuid():N}.txt"
        );
        File.WriteAllText(blockedPath, "occupied");
        _cleanupPaths.Add(blockedPath);

        var pathService = new TestPathService(blockedPath);
        var service = CreateService(
            pathService,
            new InMemoryCredentialStore(),
            new ReservedUpdateCheckService()
        );

        var result = await service.EvaluateAsync(new BackendStatusSnapshot());

        Assert.True(result.RequiresRepairMode);
        Assert.Contains(
            result.Issues,
            issue => string.Equals(issue.Code, "directory-access", StringComparison.Ordinal)
        );
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
            catch { }
        }
    }

    private string CreateRootDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"codexcliplus-dependency-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _cleanupPaths.Add(path);
        return path;
    }

    private static void SeedHealthyManagedState(TestPathService pathService)
    {
        pathService.EnsureCreatedAsync().GetAwaiter().GetResult();

        var expectedAssetRoot = GetExpectedAssetRoot(pathService);
        Directory.CreateDirectory(expectedAssetRoot);

        foreach (var fileName in ManagedBackendFiles)
        {
            var sourcePath = Path.Combine(expectedAssetRoot, fileName);
            WriteManagedBackendAsset(sourcePath, fileName);
            File.Copy(
                sourcePath,
                Path.Combine(pathService.Directories.BackendDirectory, fileName),
                overwrite: true
            );
        }

        File.WriteAllText(pathService.Directories.SettingsFilePath, "{ }");
        File.WriteAllText(
            pathService.Directories.BackendConfigFilePath,
            FormattableString.Invariant($"port: {AppConstants.DefaultBackendPort}")
        );

        var secretsDirectory = Path.Combine(pathService.Directories.ConfigDirectory, "secrets");
        Directory.CreateDirectory(secretsDirectory);
        File.WriteAllBytes(Path.Combine(secretsDirectory, "management-key.bin"), [1, 2, 3, 4]);
    }

    private static DependencyHealthService CreateService(
        TestPathService pathService,
        ISecureCredentialStore credentialStore,
        IUpdateCheckService updateCheckService,
        string frameworkDescription = ".NET 10.0.0"
    )
    {
        var expectedAssetRoot = GetExpectedAssetRoot(pathService);

        return new DependencyHealthService(
            pathService,
            new DirectoryAccessService(pathService),
            credentialStore,
            updateCheckService,
            () => frameworkDescription,
            () => Directory.Exists(expectedAssetRoot) ? expectedAssetRoot : null
        );
    }

    private static string GetExpectedAssetRoot(TestPathService pathService)
    {
        return Path.Combine(pathService.Directories.CacheDirectory, "expected-backend-assets");
    }

    private static void WriteManagedBackendAsset(string path, string fileName)
    {
        if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllBytes(path, [0x4d, 0x5a, 0x43, 0x50, 0x41, 0x44]);
            return;
        }

        File.WriteAllText(path, $"test {fileName}");
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

    private sealed class InMemoryCredentialStore : ISecureCredentialStore
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

        public Task SaveSecretAsync(
            string reference,
            string value,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            _secrets[reference] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadSecretAsync(
            string reference,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            _secrets.TryGetValue(reference, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task DeleteSecretAsync(
            string reference,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            _secrets.Remove(reference);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCredentialStore : ISecureCredentialStore
    {
        public Task SaveSecretAsync(
            string reference,
            string value,
            CancellationToken cancellationToken = default
        )
        {
            throw new SecureCredentialStoreException("DPAPI probe failed.");
        }

        public Task<string?> LoadSecretAsync(
            string reference,
            CancellationToken cancellationToken = default
        )
        {
            throw new SecureCredentialStoreException("DPAPI probe failed.");
        }

        public Task DeleteSecretAsync(
            string reference,
            CancellationToken cancellationToken = default
        )
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ReservedUpdateCheckService : IUpdateCheckService
    {
        public Task<UpdateCheckResult> CheckAsync(
            string currentVersion,
            UpdateChannel channel = UpdateChannel.Stable,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(
                new UpdateCheckResult
                {
                    Channel = channel,
                    Repository = "C4AL/CodexCliPlus",
                    ApiUrl = "https://api.github.com/repos/C4AL/CodexCliPlus/releases/latest",
                    CurrentVersion = currentVersion,
                    IsCheckSuccessful = true,
                    IsChannelReserved = true,
                    Status = "Beta reserved",
                    Detail = "Reserved for later packaging phases.",
                    ReleasePageUrl = "https://github.com/C4AL/CodexCliPlus/releases",
                }
            );
        }
    }

    private sealed class InvalidUpdateCheckService : IUpdateCheckService
    {
        public Task<UpdateCheckResult> CheckAsync(
            string currentVersion,
            UpdateChannel channel = UpdateChannel.Stable,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(
                new UpdateCheckResult
                {
                    Channel = channel,
                    Repository = "C4AL/CodexCliPlus",
                    CurrentVersion = currentVersion,
                    IsCheckSuccessful = false,
                    IsChannelReserved = true,
                    Status = "Metadata missing",
                    Detail = "Release metadata was incomplete.",
                }
            );
        }
    }
}
