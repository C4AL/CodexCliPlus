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
    public async Task SyncUsageSnapshotWritesKeeperDatabaseBackupAndStatus()
    {
        using var pathService = new TestPathService(_rootDirectory);
        var usageService = new FakeUsageService
        {
            ExportPayload = CreatePayload(
                "2026-04-29T10:00:00Z",
                CreateDetail("2026-04-29T09:30:00Z", "codex-a", "1", totalTokens: 35)
            ),
        };
        using var service = new ManagementPersistenceService(
            pathService,
            usageService,
            new FakeLogsService()
        );

        await service.SyncUsageSnapshotAsync();

        var status = service.GetStatus();
        Assert.True(File.Exists(status.UsageDatabasePath));
        Assert.Equal(
            Path.Combine(pathService.Directories.PersistenceDirectory, "usage-keeper"),
            status.Directory
        );
        Assert.Equal(1, status.UsageEventCount);
        Assert.NotNull(status.LastUsageEventAt);
        Assert.NotNull(status.LastUsageSnapshotAt);
        Assert.Contains(
            UsageKeeperStore.SourceCommit,
            status.KeeperSourceCommit,
            StringComparison.Ordinal
        );
        Assert.False(
            File.Exists(
                Path.Combine(pathService.Directories.PersistenceDirectory, "usage-snapshot.json")
            )
        );
        Assert.Contains(
            "snapshot_",
            Directory
                .EnumerateFiles(status.UsageBackupDirectory, "*.json", SearchOption.AllDirectories)
                .Single(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task SyncUsageSnapshotDeduplicatesStableEventKeys()
    {
        using var pathService = new TestPathService(_rootDirectory);
        var usageService = new FakeUsageService
        {
            ExportPayload = CreatePayload(
                "2026-04-29T10:00:00Z",
                CreateDetail("2026-04-29T09:30:00Z", "codex-a", "1", totalTokens: 35)
            ),
        };
        using var service = new ManagementPersistenceService(
            pathService,
            usageService,
            new FakeLogsService()
        );

        await service.SyncUsageSnapshotAsync();
        await service.SyncUsageSnapshotAsync();

        Assert.Equal(1, service.GetStatus().UsageEventCount);
    }

    [Fact]
    public void UsageKeeperEventKeyIsStableAndNormalizesTotalTokens()
    {
        var timestamp = DateTimeOffset.Parse(
            "2026-04-16T12:00:00.0000123Z",
            CultureInfo.InvariantCulture
        );
        var tokens = new ManagementUsageTokenStats
        {
            InputTokens = 1,
            OutputTokens = 2,
            ReasoningTokens = 3,
            CachedTokens = 4,
        };

        var key1 = UsageKeeperEventMapper.BuildEventKey(
            " provider-a ",
            "claude-sonnet",
            timestamp,
            " source-a ",
            "0",
            failed: false,
            tokens
        );
        var key2 = UsageKeeperEventMapper.BuildEventKey(
            "provider-a",
            "claude-sonnet",
            timestamp,
            "source-a",
            "0",
            failed: false,
            tokens
        );

        Assert.Equal(key1, key2);
        Assert.Equal(64, key1.Length);
        Assert.Equal(6, UsageKeeperEventMapper.NormalizeTokens(tokens).TotalTokens);
    }

    [Fact]
    public async Task SyncUsageSnapshotFiltersEventsOlderThanLocalWatermarkOverlap()
    {
        using var pathService = new TestPathService(_rootDirectory);
        var usageService = new FakeUsageService
        {
            ExportPayload = CreatePayload(
                "2026-04-20T13:00:00Z",
                CreateDetail("2026-04-20T12:00:00Z", "seed-source", "1", totalTokens: 10)
            ),
        };
        using var service = new ManagementPersistenceService(
            pathService,
            usageService,
            new FakeLogsService()
        );

        await service.SyncUsageSnapshotAsync();
        usageService.ExportPayload = CreatePayload(
            "2026-04-20T14:00:00Z",
            CreateDetail("2026-04-18T12:00:00Z", "old-source", "2", totalTokens: 2),
            CreateDetail("2026-04-20T00:00:00Z", "recent-source", "3", totalTokens: 4)
        );
        await service.SyncUsageSnapshotAsync();

        var currentBackend = CreatePayload("2026-04-19T00:00:00Z");
        usageService.ExportPayload = currentBackend;
        await service.ImportNewerUsageSnapshotAsync();

        Assert.NotNull(usageService.ImportedPayload);
        var details = usageService
            .ImportedPayload!.Usage.Apis.Single()
            .Value.Models.Single()
            .Value.Details;
        Assert.DoesNotContain(details, item => item.Source == "old-source");
        Assert.Contains(details, item => item.Source == "recent-source");
        Assert.Contains(details, item => item.Source == "seed-source");
    }

    [Fact]
    public async Task ImportNewerUsageSnapshotMigratesLegacyJsonAndImportsAggregate()
    {
        using var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        var legacySnapshotPath = Path.Combine(
            pathService.Directories.PersistenceDirectory,
            "usage-snapshot.json"
        );
        await File.WriteAllTextAsync(
            legacySnapshotPath,
            """
            {
              "version": 1,
              "exportedAt": "2026-04-29T00:00:00Z",
              "usage": {
                "apis": {
                  "provider-a": {
                    "models": {
                      "gpt-5": {
                        "details": [
                          {
                            "timestamp": "2026-04-28T23:00:00Z",
                            "source": "codex-a",
                            "auth_index": "1",
                            "failed": false,
                            "tokens": { "input_tokens": 10, "output_tokens": 20, "total_tokens": 30 }
                          }
                        ]
                      }
                    }
                  }
                }
              }
            }
            """,
            Encoding.UTF8
        );
        var usageService = new FakeUsageService
        {
            ExportPayload = CreatePayload("2026-04-28T00:00:00Z"),
        };
        using var service = new ManagementPersistenceService(
            pathService,
            usageService,
            new FakeLogsService()
        );

        await service.ImportNewerUsageSnapshotAsync();

        Assert.False(File.Exists(legacySnapshotPath));
        Assert.NotNull(usageService.ImportedPayload);
        Assert.Equal(1, usageService.ImportedPayload!.Usage.TotalRequests);
        Assert.Equal(30, usageService.ImportedPayload.Usage.TotalTokens);
        Assert.Equal(1, service.GetStatus().UsageEventCount);
    }

    [Fact]
    public async Task ClearUsageSnapshotClearsUsageAndBackupsButKeepsLogs()
    {
        using var pathService = new TestPathService(_rootDirectory);
        var usageService = new FakeUsageService
        {
            ExportPayload = CreatePayload(
                "2026-04-29T10:00:00Z",
                CreateDetail("2026-04-29T09:30:00Z", "codex-a", "1", totalTokens: 35)
            ),
        };
        using var service = new ManagementPersistenceService(
            pathService,
            usageService,
            new FakeLogsService { Snapshot = new ManagementLogsSnapshot { Lines = ["line-a"] } }
        );
        await service.SyncUsageSnapshotAsync();
        await service.SyncLogsSnapshotAsync();
        var logsSnapshotPath = Path.Combine(
            pathService.Directories.PersistenceDirectory,
            "usage-keeper",
            "logs-snapshot.json"
        );
        Assert.True(File.Exists(logsSnapshotPath));

        await service.ClearUsageSnapshotAsync();

        Assert.Equal(0, service.GetStatus().UsageEventCount);
        Assert.NotNull(usageService.ImportedPayload);
        Assert.Equal(0, usageService.ImportedPayload!.Usage.TotalRequests);
        Assert.True(File.Exists(logsSnapshotPath));
        Assert.Empty(
            Directory.EnumerateFiles(
                service.GetStatus().UsageBackupDirectory,
                "*.json",
                SearchOption.AllDirectories
            )
        );
    }

    [Fact]
    public async Task CorruptKeeperDatabaseIsIsolatedAndRebuilt()
    {
        using var pathService = new TestPathService(_rootDirectory);
        await pathService.EnsureCreatedAsync();
        var keeperDirectory = Path.Combine(
            pathService.Directories.PersistenceDirectory,
            "usage-keeper"
        );
        Directory.CreateDirectory(keeperDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(keeperDirectory, "usage.db"),
            "not sqlite",
            Encoding.UTF8
        );
        var usageService = new FakeUsageService
        {
            ExportPayload = CreatePayload(
                "2026-04-29T10:00:00Z",
                CreateDetail("2026-04-29T09:30:00Z", "codex-a", "1", totalTokens: 35)
            ),
        };
        using var service = new ManagementPersistenceService(
            pathService,
            usageService,
            new FakeLogsService()
        );

        await service.SyncUsageSnapshotAsync();

        Assert.Equal(1, service.GetStatus().UsageEventCount);
        Assert.Contains(
            Directory.EnumerateFiles(keeperDirectory, "usage.db.corrupt.*"),
            path => path.Contains(".corrupt.", StringComparison.Ordinal)
        );
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

    private static ManagementUsageExportPayload CreatePayload(
        string exportedAt,
        params ManagementUsageRequestDetail[] details
    )
    {
        return CreatePayload(
            DateTimeOffset.Parse(exportedAt, CultureInfo.InvariantCulture),
            details
        );
    }

    private static ManagementUsageExportPayload CreatePayload(
        DateTimeOffset exportedAt,
        params ManagementUsageRequestDetail[] details
    )
    {
        var normalizedDetails = details.ToArray();
        return new ManagementUsageExportPayload
        {
            Version = 1,
            ExportedAt = exportedAt,
            Usage =
                normalizedDetails.Length == 0
                    ? new ManagementUsageSnapshot()
                    : new ManagementUsageSnapshot
                    {
                        Apis = new Dictionary<string, ManagementUsageApiSnapshot>
                        {
                            ["provider-a"] = new()
                            {
                                Models = new Dictionary<string, ManagementUsageModelSnapshot>
                                {
                                    ["gpt-5"] = new() { Details = normalizedDetails },
                                },
                            },
                        },
                    },
        };
    }

    private static ManagementUsageRequestDetail CreateDetail(
        string timestamp,
        string source,
        string authIndex,
        long totalTokens,
        bool failed = false
    )
    {
        return new ManagementUsageRequestDetail
        {
            Timestamp = DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture),
            Source = source,
            AuthIndex = authIndex,
            Failed = failed,
            Tokens = new ManagementUsageTokenStats
            {
                InputTokens = totalTokens,
                TotalTokens = totalTokens,
            },
        };
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
        public ManagementUsageExportPayload ExportPayload { get; set; } =
            new()
            {
                Version = 1,
                ExportedAt = DateTimeOffset.UtcNow,
                Usage = new ManagementUsageSnapshot(),
            };

        public ManagementUsageExportPayload? ImportedPayload { get; private set; }

        public Task<ManagementApiResponse<ManagementUsageSnapshot>> GetUsageAsync(
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(Response(ExportPayload.Usage));
        }

        public Task<ManagementApiResponse<ManagementApiKeyUsageSnapshot>> GetApiKeyUsageAsync(
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(Response(new ManagementApiKeyUsageSnapshot()));
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
