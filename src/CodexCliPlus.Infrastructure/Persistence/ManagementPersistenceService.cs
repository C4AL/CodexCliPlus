using System.Text.Json;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Persistence;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.Infrastructure.Persistence;

public sealed class ManagementPersistenceService : IManagementPersistenceService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IPathService _pathService;
    private readonly IManagementUsageService _usageService;
    private readonly IManagementLogsService _logsService;
    private readonly UsageKeeperStore _usageKeeperStore;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private string? _lastError;

    public ManagementPersistenceService(
        IPathService pathService,
        IManagementUsageService usageService,
        IManagementLogsService logsService
    )
    {
        _pathService = pathService;
        _usageService = usageService;
        _logsService = logsService;
        _usageKeeperStore = new UsageKeeperStore(_pathService.Directories.PersistenceDirectory);
    }

    public async Task ImportNewerUsageSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            await _pathService.EnsureCreatedAsync(cancellationToken);
            await _usageKeeperStore.InitializeAsync(cancellationToken);
            await _usageKeeperStore.MigrateLegacySnapshotAsync(cancellationToken);

            var current = (await _usageService.ExportUsageAsync(cancellationToken)).Value;
            var localClock = await _usageKeeperStore.GetLatestLocalSnapshotClockAsync(
                cancellationToken
            );
            if (IsLocalUsageSnapshotNewer(localClock, current.ExportedAt))
            {
                var localUsage = await _usageKeeperStore.BuildUsageSnapshotAsync(cancellationToken);
                await _usageService.ImportUsageAsync(
                    new ManagementUsageExportPayload
                    {
                        Version = current.Version <= 0 ? 1 : current.Version,
                        ExportedAt = localClock,
                        Usage = localUsage,
                    },
                    cancellationToken
                );
                _lastError = null;
                return;
            }

            await SaveExportAsync(current, cancellationToken);
            _lastError = null;
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            throw;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task SyncUsageSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            await _pathService.EnsureCreatedAsync(cancellationToken);
            await _usageKeeperStore.InitializeAsync(cancellationToken);
            await _usageKeeperStore.MigrateLegacySnapshotAsync(cancellationToken);
            var payload = (await _usageService.ExportUsageAsync(cancellationToken)).Value;
            await SaveExportAsync(payload, cancellationToken);
            _lastError = null;
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            throw;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task SyncLogsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            await _pathService.EnsureCreatedAsync(cancellationToken);
            await _usageKeeperStore.InitializeAsync(cancellationToken);
            var payload = (
                await _logsService.GetLogsAsync(limit: 500, cancellationToken: cancellationToken)
            ).Value;
            UsageKeeperStore.AtomicWriteText(
                _usageKeeperStore.LogsSnapshotPath,
                JsonSerializer.Serialize(payload, JsonOptions)
            );
            _lastError = null;
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            throw;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task ClearUsageSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            await _pathService.EnsureCreatedAsync(cancellationToken);
            await _usageKeeperStore.ClearUsageAsync(cancellationToken);
            await _usageService.ImportUsageAsync(
                new ManagementUsageExportPayload
                {
                    Version = 1,
                    ExportedAt = DateTimeOffset.UtcNow,
                    Usage = new ManagementUsageSnapshot(),
                },
                cancellationToken
            );
            _lastError = null;
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            throw;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public PersistenceStatus GetStatus()
    {
        try
        {
            var status = _usageKeeperStore
                .GetStatusAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return new PersistenceStatus
            {
                Directory = _usageKeeperStore.KeeperDirectory,
                UsesFallbackDirectory = _pathService.Directories.UsesPersistenceFallback,
                LastUsageSnapshotAt = status.LastSyncAt,
                LastLogsSnapshotAt = GetLastWriteTimeOrNull(_usageKeeperStore.LogsSnapshotPath),
                UsageDatabasePath = _usageKeeperStore.DatabasePath,
                UsageBackupDirectory = _usageKeeperStore.BackupDirectory,
                UsageEventCount = status.EventCount,
                LastUsageEventAt = status.LastEventAt,
                LastUsageSyncAt = status.LastSyncAt,
                LastPersistenceError = _lastError ?? status.LastError,
                UsesKeeperDatabase = true,
                KeeperSourceCommit = UsageKeeperStore.SourceCommit,
            };
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            return new PersistenceStatus
            {
                Directory = _usageKeeperStore.KeeperDirectory,
                UsesFallbackDirectory = _pathService.Directories.UsesPersistenceFallback,
                LastLogsSnapshotAt = GetLastWriteTimeOrNull(_usageKeeperStore.LogsSnapshotPath),
                UsageDatabasePath = _usageKeeperStore.DatabasePath,
                UsageBackupDirectory = _usageKeeperStore.BackupDirectory,
                LastPersistenceError = _lastError,
                UsesKeeperDatabase = false,
                KeeperSourceCommit = UsageKeeperStore.SourceCommit,
            };
        }
    }

    private async Task SaveExportAsync(
        ManagementUsageExportPayload payload,
        CancellationToken cancellationToken
    )
    {
        var rawJson = UsageKeeperJson.SerializeExport(payload);
        await _usageKeeperStore.SaveExportAsync(payload, rawJson, cancellationToken);
    }

    private static bool IsLocalUsageSnapshotNewer(
        DateTimeOffset? localClock,
        DateTimeOffset? currentExportedAt
    )
    {
        if (localClock is null)
        {
            return false;
        }

        return currentExportedAt is null || localClock.Value > currentExportedAt.Value;
    }

    private static DateTimeOffset? GetLastWriteTimeOrNull(string path)
    {
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    public void Dispose()
    {
        _syncLock.Dispose();
    }
}
