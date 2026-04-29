using System.Globalization;
using System.Net;
using System.Text;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Infrastructure.Persistence;

namespace CodexCliPlus.Tests.Persistence;

public sealed class ManagementPersistenceServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-persistence-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task SyncUsageSnapshotWritesSnapshotAndStatus()
    {
        using var pathService = new TestPathService(_rootDirectory);
        var usageService = new FakeUsageService
        {
            ExportPayload = new ManagementUsageExportPayload
            {
                Version = 1,
                ExportedAt = DateTimeOffset.UtcNow,
                Usage = new ManagementUsageSnapshot { TotalRequests = 42, TotalTokens = 128 },
            },
        };
        var service = new ManagementPersistenceService(
            pathService,
            usageService,
            new FakeLogsService()
        );

        await service.SyncUsageSnapshotAsync();

        var snapshotPath = Path.Combine(
            pathService.Directories.PersistenceDirectory,
            "usage-snapshot.json"
        );
        Assert.True(File.Exists(snapshotPath));
        var json = await File.ReadAllTextAsync(snapshotPath);
        Assert.Contains("\"totalRequests\": 42", json, StringComparison.Ordinal);
        Assert.NotNull(service.GetStatus().LastUsageSnapshotAt);
    }

    [Fact]
    public async Task ImportNewerUsageSnapshotImportsOnlyWhenLocalSnapshotIsNewer()
    {
        using var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        var localSnapshotPath = Path.Combine(
            pathService.Directories.PersistenceDirectory,
            "usage-snapshot.json"
        );
        await File.WriteAllTextAsync(
            localSnapshotPath,
            """
            {
              "version": 1,
              "exportedAt": "2026-04-29T00:00:00Z",
              "usage": {
                "total_requests": 9,
                "success_count": 8,
                "failure_count": 1,
                "total_tokens": 99,
                "apis": {},
                "requests_by_day": {},
                "requests_by_hour": {},
                "tokens_by_day": {},
                "tokens_by_hour": {}
              }
            }
            """,
            Encoding.UTF8
        );
        var usageService = new FakeUsageService
        {
            ExportPayload = new ManagementUsageExportPayload
            {
                Version = 1,
                ExportedAt = DateTimeOffset.Parse(
                    "2026-04-28T00:00:00Z",
                    CultureInfo.InvariantCulture
                ),
                Usage = new ManagementUsageSnapshot(),
            },
        };
        var service = new ManagementPersistenceService(
            pathService,
            usageService,
            new FakeLogsService()
        );

        await service.ImportNewerUsageSnapshotAsync();

        Assert.NotNull(usageService.ImportedPayload);
        Assert.Equal(9, usageService.ImportedPayload!.Usage.TotalRequests);
    }

    [Fact]
    public async Task ClearUsageSnapshotDeletesLocalFileAndImportsEmptyUsage()
    {
        using var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        var snapshotPath = Path.Combine(
            pathService.Directories.PersistenceDirectory,
            "usage-snapshot.json"
        );
        await File.WriteAllTextAsync(snapshotPath, "{}", Encoding.UTF8);
        var usageService = new FakeUsageService();
        var service = new ManagementPersistenceService(
            pathService,
            usageService,
            new FakeLogsService()
        );

        await service.ClearUsageSnapshotAsync();

        Assert.False(File.Exists(snapshotPath));
        Assert.NotNull(usageService.ImportedPayload);
        Assert.Equal(0, usageService.ImportedPayload!.Usage.TotalRequests);
    }

    [Fact]
    public async Task SyncLogsSnapshotWritesLatestLogs()
    {
        using var pathService = new TestPathService(_rootDirectory);
        var service = new ManagementPersistenceService(
            pathService,
            new FakeUsageService(),
            new FakeLogsService
            {
                Snapshot = new ManagementLogsSnapshot
                {
                    Lines = ["line-a", "line-b"],
                    LineCount = 2,
                    LatestTimestamp = 123,
                },
            }
        );

        await service.SyncLogsSnapshotAsync();

        var snapshotPath = Path.Combine(
            pathService.Directories.PersistenceDirectory,
            "logs-snapshot.json"
        );
        Assert.True(File.Exists(snapshotPath));
        var json = await File.ReadAllTextAsync(snapshotPath);
        Assert.Contains("line-a", json, StringComparison.Ordinal);
        Assert.NotNull(service.GetStatus().LastLogsSnapshotAt);
    }

    [Fact]
    public void AppDirectoriesDetectsPersistenceFallbackDirectory()
    {
        var directories = CreateDirectories(
            _rootDirectory,
            Path.Combine(Path.GetTempPath(), "codexcliplus-persistence-fallback")
        );

        Assert.True(directories.UsesPersistenceFallback);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
        }
        catch { }
    }

    private static AppDirectories CreateDirectories(
        string rootDirectory,
        string persistenceDirectory
    )
    {
        return new AppDirectories(
            AppDataMode.Installed,
            rootDirectory,
            Path.Combine(rootDirectory, "logs"),
            Path.Combine(rootDirectory, "config"),
            Path.Combine(rootDirectory, "backend"),
            Path.Combine(rootDirectory, "cache"),
            Path.Combine(rootDirectory, "diagnostics"),
            Path.Combine(rootDirectory, "runtime"),
            Path.Combine(rootDirectory, "config", "appsettings.json"),
            Path.Combine(rootDirectory, "config", "backend.yaml"),
            persistenceDirectory
        );
    }

    private sealed class TestPathService : IPathService, IDisposable
    {
        public TestPathService(string rootDirectory)
        {
            Directories = CreateDirectories(
                rootDirectory,
                Path.Combine(rootDirectory, "persistence")
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
            Directory.CreateDirectory(Directories.PersistenceDirectory);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    private sealed class FakeUsageService : IManagementUsageService
    {
        public ManagementUsageExportPayload ExportPayload { get; init; } =
            new() { Version = 1, ExportedAt = DateTimeOffset.UtcNow };

        public ManagementUsageExportPayload? ImportedPayload { get; private set; }

        public Task<ManagementApiResponse<ManagementUsageSnapshot>> GetUsageAsync(
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(Response(ExportPayload.Usage));
        }

        public Task<ManagementApiResponse<ManagementUsageExportPayload>> ExportUsageAsync(
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(Response(ExportPayload));
        }

        public Task<ManagementApiResponse<ManagementUsageImportResult>> ImportUsageAsync(
            ManagementUsageExportPayload payload,
            CancellationToken cancellationToken = default
        )
        {
            ImportedPayload = payload;
            return Task.FromResult(Response(new ManagementUsageImportResult()));
        }
    }

    private sealed class FakeLogsService : IManagementLogsService
    {
        public ManagementLogsSnapshot Snapshot { get; init; } = new();

        public Task<ManagementApiResponse<ManagementLogsSnapshot>> GetLogsAsync(
            long after = 0,
            int limit = 0,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(Response(Snapshot));
        }

        public Task<ManagementApiResponse<ManagementOperationResult>> ClearLogsAsync(
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(Response(new ManagementOperationResult()));
        }

        public Task<
            ManagementApiResponse<IReadOnlyList<ManagementErrorLogFile>>
        > GetRequestErrorLogsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Response(
                    (IReadOnlyList<ManagementErrorLogFile>)Array.Empty<ManagementErrorLogFile>()
                )
            );
        }

        public Task<ManagementApiResponse<string>> GetRequestLogByIdAsync(
            string id,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(Response(string.Empty));
        }
    }

    private static ManagementApiResponse<T> Response<T>(T value)
    {
        return new ManagementApiResponse<T>
        {
            Value = value,
            Metadata = new ManagementServerMetadata(),
            StatusCode = HttpStatusCode.OK,
        };
    }
}
