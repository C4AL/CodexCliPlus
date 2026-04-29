using System.Text;
using System.Text.Json;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Persistence;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Infrastructure.Management;

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
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public ManagementPersistenceService(
        IPathService pathService,
        IManagementUsageService usageService,
        IManagementLogsService logsService
    )
    {
        _pathService = pathService;
        _usageService = usageService;
        _logsService = logsService;
    }

    private string UsageSnapshotPath =>
        Path.Combine(_pathService.Directories.PersistenceDirectory, "usage-snapshot.json");

    private string LogsSnapshotPath =>
        Path.Combine(_pathService.Directories.PersistenceDirectory, "logs-snapshot.json");

    public async Task ImportNewerUsageSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(UsageSnapshotPath))
        {
            return;
        }

        var local = await ReadUsageSnapshotAsync(cancellationToken);
        if (local is null)
        {
            return;
        }

        ManagementUsageExportPayload? current = null;
        try
        {
            current = (await _usageService.ExportUsageAsync(cancellationToken)).Value;
        }
        catch
        {
            return;
        }

        if (!IsLocalUsageSnapshotNewer(local, current))
        {
            return;
        }

        await _usageService.ImportUsageAsync(local, cancellationToken);
    }

    public async Task SyncUsageSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            await _pathService.EnsureCreatedAsync(cancellationToken);
            var payload = (await _usageService.ExportUsageAsync(cancellationToken)).Value;
            var json = JsonSerializer.Serialize(
                new
                {
                    version = payload.Version <= 0 ? 1 : payload.Version,
                    exportedAt = DateTimeOffset.UtcNow,
                    usage = payload.Usage,
                },
                JsonOptions
            );
            await File.WriteAllTextAsync(
                UsageSnapshotPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken
            );
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
            var payload = (
                await _logsService.GetLogsAsync(limit: 500, cancellationToken: cancellationToken)
            ).Value;
            await File.WriteAllTextAsync(
                LogsSnapshotPath,
                JsonSerializer.Serialize(payload, JsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken
            );
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
            if (File.Exists(UsageSnapshotPath))
            {
                File.Delete(UsageSnapshotPath);
            }

            await _usageService.ImportUsageAsync(
                new ManagementUsageExportPayload
                {
                    Version = 1,
                    Usage = new ManagementUsageSnapshot(),
                },
                cancellationToken
            );
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public PersistenceStatus GetStatus()
    {
        return new PersistenceStatus
        {
            Directory = _pathService.Directories.PersistenceDirectory,
            UsesFallbackDirectory = _pathService.Directories.UsesPersistenceFallback,
            LastUsageSnapshotAt = GetLastWriteTimeOrNull(UsageSnapshotPath),
            LastLogsSnapshotAt = GetLastWriteTimeOrNull(LogsSnapshotPath),
        };
    }

    private async Task<ManagementUsageExportPayload?> ReadUsageSnapshotAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var stream = File.OpenRead(UsageSnapshotPath);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken
            );
            return ManagementMappers.MapUsageExport(document.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsLocalUsageSnapshotNewer(
        ManagementUsageExportPayload local,
        ManagementUsageExportPayload current
    )
    {
        if (local.ExportedAt is null)
        {
            return false;
        }

        return current.ExportedAt is null || local.ExportedAt > current.ExportedAt;
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
