using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Infrastructure.Management;
using Microsoft.Data.Sqlite;

namespace CodexCliPlus.Infrastructure.Persistence;

internal sealed class UsageKeeperStore
{
    public const string SourceCommit = "06117c79ca254a5fe5113d05768f17c335d62596";

    private static readonly TimeSpan OverlapWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan BackupInterval = TimeSpan.FromHours(1);
    private const int BackupRetentionDays = 30;

    private readonly string _keeperDirectory;
    private readonly string _databasePath;
    private readonly string _backupDirectory;

    public UsageKeeperStore(string persistenceDirectory)
    {
        _keeperDirectory = Path.Combine(persistenceDirectory, "usage-keeper");
        _databasePath = Path.Combine(_keeperDirectory, "usage.db");
        _backupDirectory = Path.Combine(_keeperDirectory, "backups");
    }

    public string KeeperDirectory => _keeperDirectory;

    public string DatabasePath => _databasePath;

    public string BackupDirectory => _backupDirectory;

    public string LogsSnapshotPath => Path.Combine(_keeperDirectory, "logs-snapshot.json");

    public string LegacyUsageSnapshotPath =>
        Path.Combine(Path.GetDirectoryName(_keeperDirectory)!, "usage-snapshot.json");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_keeperDirectory);
        Directory.CreateDirectory(_backupDirectory);

        if (HasInvalidSqliteHeader())
        {
            IsolateDatabaseFiles();
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await CreateSchemaAsync(connection, cancellationToken);
        }
        catch (SqliteException exception) when (IsCorruptionException(exception))
        {
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            IsolateDatabaseFiles();
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await CreateSchemaAsync(connection, cancellationToken);
        }
    }

    public async Task<UsageKeeperSyncResult> SaveExportAsync(
        ManagementUsageExportPayload payload,
        string rawJson,
        CancellationToken cancellationToken
    )
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var fetchedAt = DateTimeOffset.UtcNow;
        var rawPayload = Encoding.UTF8.GetBytes(rawJson);
        var payloadHash = HashPayload(rawPayload);
        var snapshotRunId = await CreateSnapshotRunAsync(
            connection,
            transaction,
            fetchedAt,
            payload,
            payloadHash,
            rawPayload,
            cancellationToken
        );

        var backupFilePath = await MaybeWriteBackupAsync(
            connection,
            transaction,
            snapshotRunId,
            fetchedAt,
            rawPayload,
            cancellationToken
        );

        var events = UsageKeeperEventMapper
            .Flatten(snapshotRunId, payload)
            .Select(item =>
            {
                item.SnapshotRunId = snapshotRunId;
                return item;
            })
            .ToArray();
        var filteredEvents = await FilterByWatermarkAsync(
            connection,
            transaction,
            events,
            cancellationToken
        );
        var (inserted, deduped) = await InsertEventsAsync(
            connection,
            transaction,
            filteredEvents,
            cancellationToken
        );

        await FinalizeSnapshotRunAsync(
            connection,
            transaction,
            snapshotRunId,
            "completed",
            backupFilePath,
            inserted,
            deduped,
            null,
            cancellationToken
        );
        await transaction.CommitAsync(cancellationToken);

        CleanupBackups(fetchedAt);

        return new UsageKeeperSyncResult(
            SnapshotRunId: snapshotRunId,
            InsertedEvents: inserted,
            DedupedEvents: deduped,
            PayloadHash: payloadHash,
            ExportedAt: payload.ExportedAt?.ToUniversalTime(),
            BackupFilePath: backupFilePath
        );
    }

    public async Task<ManagementUsageSnapshot> BuildUsageSnapshotAsync(
        CancellationToken cancellationToken
    )
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var events = new List<UsageKeeperEvent>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, event_key, snapshot_run_id, api_group_key, model, timestamp, source,
                       auth_index, failed, latency_ms, input_tokens, output_tokens,
                       reasoning_tokens, cached_tokens, total_tokens
                FROM usage_events
                ORDER BY timestamp ASC, id ASC;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                events.Add(ReadEvent(reader));
            }
        }

        return BuildSnapshot(events);
    }

    public async Task<UsageKeeperStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var eventCount = await ExecuteScalarLongAsync(
            connection,
            "SELECT COUNT(*) FROM usage_events;",
            cancellationToken
        );
        var lastEventText = await ExecuteScalarStringAsync(
            connection,
            "SELECT timestamp FROM usage_events ORDER BY timestamp DESC LIMIT 1;",
            cancellationToken
        );
        var lastSyncText = await ExecuteScalarStringAsync(
            connection,
            """
            SELECT fetched_at
            FROM snapshot_runs
            WHERE status IN ('completed', 'completed_with_warnings')
            ORDER BY fetched_at DESC
            LIMIT 1;
            """,
            cancellationToken
        );
        var lastError = await ExecuteScalarStringAsync(
            connection,
            """
            SELECT error_message
            FROM snapshot_runs
            WHERE TRIM(error_message) <> ''
            ORDER BY id DESC
            LIMIT 1;
            """,
            cancellationToken
        );

        return new UsageKeeperStatus(
            EventCount: eventCount,
            LastEventAt: ParseDateTimeOffset(lastEventText),
            LastSyncAt: ParseDateTimeOffset(lastSyncText),
            LastError: string.IsNullOrWhiteSpace(lastError) ? null : lastError.Trim()
        );
    }

    public async Task<DateTimeOffset?> GetLatestLocalSnapshotClockAsync(
        CancellationToken cancellationToken
    )
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var latestText = await ExecuteScalarStringAsync(
            connection,
            """
            SELECT COALESCE(exported_at, fetched_at)
            FROM snapshot_runs
            WHERE status IN ('completed', 'completed_with_warnings')
            ORDER BY COALESCE(exported_at, fetched_at) DESC
            LIMIT 1;
            """,
            cancellationToken
        );
        return ParseDateTimeOffset(latestText);
    }

    public async Task ClearUsageAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        foreach (
            var statement in new[] { "DELETE FROM usage_events;", "DELETE FROM snapshot_runs;" }
        )
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        if (File.Exists(LegacyUsageSnapshotPath))
        {
            File.Delete(LegacyUsageSnapshotPath);
        }

        if (Directory.Exists(_backupDirectory))
        {
            Directory.Delete(_backupDirectory, recursive: true);
        }

        Directory.CreateDirectory(_backupDirectory);
    }

    public async Task<bool> MigrateLegacySnapshotAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(LegacyUsageSnapshotPath))
        {
            return false;
        }

        ManagementUsageExportPayload payload;
        string rawJson;
        try
        {
            rawJson = await File.ReadAllTextAsync(LegacyUsageSnapshotPath, cancellationToken);
            using var document = ManagementJson.Parse(rawJson);
            payload = ManagementMappers.MapUsageExport(document.RootElement);
        }
        catch
        {
            return false;
        }

        await SaveExportAsync(payload, rawJson, cancellationToken);
        File.Delete(LegacyUsageSnapshotPath);
        return true;
    }

    public static void AtomicWriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(
            tempPath,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 5,
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);

        foreach (
            var pragma in new[]
            {
                "PRAGMA journal_mode=WAL;",
                "PRAGMA busy_timeout=5000;",
                "PRAGMA foreign_keys=ON;",
            }
        )
        {
            await using var command = connection.CreateCommand();
            command.CommandText = pragma;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return connection;
    }

    private static async Task CreateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        var statements = new[]
        {
            """
                CREATE TABLE IF NOT EXISTS snapshot_runs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    fetched_at TEXT NOT NULL,
                    cpa_base_url TEXT NOT NULL DEFAULT '',
                    exported_at TEXT NULL,
                    version TEXT NOT NULL DEFAULT '',
                    status TEXT NOT NULL,
                    http_status INTEGER NOT NULL DEFAULT 0,
                    payload_hash TEXT NOT NULL DEFAULT '',
                    raw_payload BLOB NULL,
                    backup_file_path TEXT NOT NULL DEFAULT '',
                    error_message TEXT NOT NULL DEFAULT '',
                    inserted_events INTEGER NOT NULL DEFAULT 0,
                    deduped_events INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                """,
            "CREATE INDEX IF NOT EXISTS idx_snapshot_runs_fetched_at ON snapshot_runs(fetched_at);",
            "CREATE INDEX IF NOT EXISTS idx_snapshot_runs_status ON snapshot_runs(status);",
            """
                CREATE TABLE IF NOT EXISTS usage_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_key TEXT NOT NULL UNIQUE,
                    snapshot_run_id INTEGER NOT NULL,
                    api_group_key TEXT NOT NULL,
                    model TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    source TEXT NOT NULL,
                    auth_index TEXT NOT NULL,
                    failed INTEGER NOT NULL DEFAULT 0,
                    latency_ms INTEGER NOT NULL DEFAULT 0,
                    input_tokens INTEGER NOT NULL DEFAULT 0,
                    output_tokens INTEGER NOT NULL DEFAULT 0,
                    reasoning_tokens INTEGER NOT NULL DEFAULT 0,
                    cached_tokens INTEGER NOT NULL DEFAULT 0,
                    total_tokens INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL
                );
                """,
            "CREATE INDEX IF NOT EXISTS idx_usage_events_api_group_key ON usage_events(api_group_key);",
            "CREATE INDEX IF NOT EXISTS idx_usage_events_model ON usage_events(model);",
            "CREATE INDEX IF NOT EXISTS idx_usage_events_timestamp ON usage_events(timestamp);",
            "CREATE INDEX IF NOT EXISTS idx_usage_events_source ON usage_events(source);",
            "CREATE INDEX IF NOT EXISTS idx_usage_events_auth_index ON usage_events(auth_index);",
            "CREATE INDEX IF NOT EXISTS idx_usage_events_failed ON usage_events(failed);",
        };

        foreach (var statement in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<long> CreateSnapshotRunAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset fetchedAt,
        ManagementUsageExportPayload payload,
        string payloadHash,
        byte[] rawPayload,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO snapshot_runs (
                fetched_at, exported_at, version, status, http_status, payload_hash,
                raw_payload, created_at, updated_at
            )
            VALUES ($fetched_at, $exported_at, $version, 'pending', 200, $payload_hash,
                $raw_payload, $now, $now);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$fetched_at", FormatDateTime(fetchedAt));
        command.Parameters.AddWithValue(
            "$exported_at",
            payload.ExportedAt is null ? DBNull.Value : FormatDateTime(payload.ExportedAt.Value)
        );
        command.Parameters.AddWithValue(
            "$version",
            (payload.Version <= 0 ? 1 : payload.Version).ToString(CultureInfo.InvariantCulture)
        );
        command.Parameters.AddWithValue("$payload_hash", payloadHash);
        command.Parameters.Add("$raw_payload", SqliteType.Blob).Value = rawPayload;
        command.Parameters.AddWithValue("$now", FormatDateTime(DateTimeOffset.UtcNow));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task<string> MaybeWriteBackupAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long snapshotRunId,
        DateTimeOffset fetchedAt,
        byte[] rawPayload,
        CancellationToken cancellationToken
    )
    {
        if (rawPayload.Length == 0)
        {
            return string.Empty;
        }

        var lastBackupAtText = await ExecuteScalarStringAsync(
            connection,
            """
            SELECT fetched_at
            FROM snapshot_runs
            WHERE status IN ('completed', 'completed_with_warnings')
              AND TRIM(backup_file_path) <> ''
            ORDER BY fetched_at DESC
            LIMIT 1;
            """,
            cancellationToken,
            transaction
        );
        var lastBackupAt = ParseDateTimeOffset(lastBackupAtText);
        if (lastBackupAt is not null && fetchedAt - lastBackupAt.Value < BackupInterval)
        {
            return string.Empty;
        }

        var dayDirectory = Path.Combine(
            _backupDirectory,
            fetchedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        );
        Directory.CreateDirectory(dayDirectory);
        var fileName =
            $"snapshot_{snapshotRunId:000000}_{fetchedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss.fffffff'Z'", CultureInfo.InvariantCulture)}.json";
        var path = Path.Combine(dayDirectory, fileName);
        await File.WriteAllBytesAsync(path, rawPayload, cancellationToken);
        return path;
    }

    private static async Task<UsageKeeperEvent[]> FilterByWatermarkAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UsageKeeperEvent[] events,
        CancellationToken cancellationToken
    )
    {
        if (events.Length == 0)
        {
            return events;
        }

        var watermarkText = await ExecuteScalarStringAsync(
            connection,
            "SELECT timestamp FROM usage_events ORDER BY timestamp DESC LIMIT 1;",
            cancellationToken,
            transaction
        );
        var watermark = ParseDateTimeOffset(watermarkText);
        if (watermark is null)
        {
            return events;
        }

        var cutoff = watermark.Value.Subtract(OverlapWindow);
        return events
            .Where(item => item.Timestamp == DateTimeOffset.MinValue || item.Timestamp >= cutoff)
            .ToArray();
    }

    private static async Task<(int Inserted, int Deduped)> InsertEventsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UsageKeeperEvent[] events,
        CancellationToken cancellationToken
    )
    {
        if (events.Length == 0)
        {
            return (0, 0);
        }

        var inserted = 0;
        foreach (var usageEvent in events)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO usage_events (
                    event_key, snapshot_run_id, api_group_key, model, timestamp, source,
                    auth_index, failed, latency_ms, input_tokens, output_tokens,
                    reasoning_tokens, cached_tokens, total_tokens, created_at
                )
                VALUES (
                    $event_key, $snapshot_run_id, $api_group_key, $model, $timestamp, $source,
                    $auth_index, $failed, $latency_ms, $input_tokens, $output_tokens,
                    $reasoning_tokens, $cached_tokens, $total_tokens, $created_at
                );
                """;
            AddEventParameters(command, usageEvent);
            inserted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return (inserted, events.Length - inserted);
    }

    private static async Task FinalizeSnapshotRunAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long snapshotRunId,
        string status,
        string backupFilePath,
        int inserted,
        int deduped,
        string? errorMessage,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE snapshot_runs
            SET status = $status,
                backup_file_path = $backup_file_path,
                inserted_events = $inserted_events,
                deduped_events = $deduped_events,
                error_message = $error_message,
                updated_at = $updated_at
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$backup_file_path", backupFilePath);
        command.Parameters.AddWithValue("$inserted_events", inserted);
        command.Parameters.AddWithValue("$deduped_events", deduped);
        command.Parameters.AddWithValue("$error_message", errorMessage ?? string.Empty);
        command.Parameters.AddWithValue("$updated_at", FormatDateTime(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", snapshotRunId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddEventParameters(SqliteCommand command, UsageKeeperEvent usageEvent)
    {
        command.Parameters.AddWithValue("$event_key", usageEvent.EventKey);
        command.Parameters.AddWithValue("$snapshot_run_id", usageEvent.SnapshotRunId);
        command.Parameters.AddWithValue("$api_group_key", usageEvent.ApiGroupKey);
        command.Parameters.AddWithValue("$model", usageEvent.Model);
        command.Parameters.AddWithValue("$timestamp", FormatDateTime(usageEvent.Timestamp));
        command.Parameters.AddWithValue("$source", usageEvent.Source);
        command.Parameters.AddWithValue("$auth_index", usageEvent.AuthIndex);
        command.Parameters.AddWithValue("$failed", usageEvent.Failed ? 1 : 0);
        command.Parameters.AddWithValue("$latency_ms", usageEvent.LatencyMs);
        command.Parameters.AddWithValue("$input_tokens", usageEvent.InputTokens);
        command.Parameters.AddWithValue("$output_tokens", usageEvent.OutputTokens);
        command.Parameters.AddWithValue("$reasoning_tokens", usageEvent.ReasoningTokens);
        command.Parameters.AddWithValue("$cached_tokens", usageEvent.CachedTokens);
        command.Parameters.AddWithValue("$total_tokens", usageEvent.TotalTokens);
        command.Parameters.AddWithValue("$created_at", FormatDateTime(DateTimeOffset.UtcNow));
    }

    private static UsageKeeperEvent ReadEvent(SqliteDataReader reader)
    {
        return new UsageKeeperEvent
        {
            Id = reader.GetInt64(0),
            EventKey = reader.GetString(1),
            SnapshotRunId = reader.GetInt64(2),
            ApiGroupKey = reader.GetString(3),
            Model = reader.GetString(4),
            Timestamp = ParseDateTimeOffset(reader.GetString(5)) ?? DateTimeOffset.MinValue,
            Source = reader.GetString(6),
            AuthIndex = reader.GetString(7),
            Failed = reader.GetInt64(8) != 0,
            LatencyMs = reader.GetInt64(9),
            InputTokens = reader.GetInt64(10),
            OutputTokens = reader.GetInt64(11),
            ReasoningTokens = reader.GetInt64(12),
            CachedTokens = reader.GetInt64(13),
            TotalTokens = reader.GetInt64(14),
        };
    }

    private static ManagementUsageSnapshot BuildSnapshot(IReadOnlyList<UsageKeeperEvent> events)
    {
        var apiBuilders = new Dictionary<string, UsageApiBuilder>(StringComparer.OrdinalIgnoreCase);
        var requestsByDay = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var requestsByHour = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var tokensByDay = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var tokensByHour = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long totalRequests = 0;
        long successCount = 0;
        long failureCount = 0;
        long totalTokens = 0;

        foreach (var usageEvent in events)
        {
            var apiKey = NormalizeDimension(usageEvent.ApiGroupKey);
            var modelName = NormalizeDimension(usageEvent.Model);
            if (!apiBuilders.TryGetValue(apiKey, out var apiBuilder))
            {
                apiBuilder = new UsageApiBuilder();
                apiBuilders[apiKey] = apiBuilder;
            }

            if (!apiBuilder.Models.TryGetValue(modelName, out var modelBuilder))
            {
                modelBuilder = new UsageModelBuilder();
                apiBuilder.Models[modelName] = modelBuilder;
            }

            var detail = new ManagementUsageRequestDetail
            {
                Timestamp = usageEvent.Timestamp.ToUniversalTime(),
                Source = usageEvent.Source,
                AuthIndex = usageEvent.AuthIndex,
                LatencyMs = usageEvent.LatencyMs,
                Failed = usageEvent.Failed,
                Tokens = new ManagementUsageTokenStats
                {
                    InputTokens = usageEvent.InputTokens,
                    OutputTokens = usageEvent.OutputTokens,
                    ReasoningTokens = usageEvent.ReasoningTokens,
                    CachedTokens = usageEvent.CachedTokens,
                    TotalTokens = usageEvent.TotalTokens,
                },
            };
            modelBuilder.Details.Add(detail);
            modelBuilder.TotalRequests++;
            modelBuilder.TotalTokens += usageEvent.TotalTokens;
            apiBuilder.TotalRequests++;
            apiBuilder.TotalTokens += usageEvent.TotalTokens;
            totalRequests++;
            totalTokens += usageEvent.TotalTokens;
            if (usageEvent.Failed)
            {
                failureCount++;
            }
            else
            {
                successCount++;
            }

            var dayKey = usageEvent.Timestamp.UtcDateTime.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );
            var hourKey = usageEvent.Timestamp.UtcDateTime.ToString(
                "yyyy-MM-dd'T'HH:00:00'Z'",
                CultureInfo.InvariantCulture
            );
            Increment(requestsByDay, dayKey, 1);
            Increment(requestsByHour, hourKey, 1);
            Increment(tokensByDay, dayKey, usageEvent.TotalTokens);
            Increment(tokensByHour, hourKey, usageEvent.TotalTokens);
        }

        var apis = apiBuilders.ToDictionary(
            pair => pair.Key,
            pair => new ManagementUsageApiSnapshot
            {
                TotalRequests = pair.Value.TotalRequests,
                TotalTokens = pair.Value.TotalTokens,
                Models = pair.Value.Models.ToDictionary(
                    modelPair => modelPair.Key,
                    modelPair =>
                    {
                        modelPair.Value.Details.Sort(
                            (first, second) => Nullable.Compare(first.Timestamp, second.Timestamp)
                        );
                        return new ManagementUsageModelSnapshot
                        {
                            TotalRequests = modelPair.Value.TotalRequests,
                            TotalTokens = modelPair.Value.TotalTokens,
                            Details = modelPair.Value.Details,
                        };
                    },
                    StringComparer.OrdinalIgnoreCase
                ),
            },
            StringComparer.OrdinalIgnoreCase
        );

        return new ManagementUsageSnapshot
        {
            TotalRequests = totalRequests,
            SuccessCount = successCount,
            FailureCount = failureCount,
            TotalTokens = totalTokens,
            Apis = apis,
            RequestsByDay = requestsByDay,
            RequestsByHour = requestsByHour,
            TokensByDay = tokensByDay,
            TokensByHour = tokensByHour,
        };
    }

    private static void Increment(Dictionary<string, long> values, string key, long amount)
    {
        values[key] = values.TryGetValue(key, out var current) ? current + amount : amount;
    }

    private static string NormalizeDimension(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "unknown" : trimmed;
    }

    private static async Task<string?> ExecuteScalarStringAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull
            ? null
            : Convert.ToString(result, CultureInfo.InvariantCulture);
    }

    private static async Task<long> ExecuteScalarLongAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0 : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static string FormatDateTime(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed
            )
        )
        {
            return null;
        }

        return parsed.ToUniversalTime();
    }

    private static string HashPayload(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return string.Empty;
        }

        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private void CleanupBackups(DateTimeOffset now)
    {
        if (!Directory.Exists(_backupDirectory))
        {
            return;
        }

        var cutoff = now.UtcDateTime.Date.AddDays(-BackupRetentionDays);
        foreach (var directory in Directory.EnumerateDirectories(_backupDirectory))
        {
            var name = Path.GetFileName(directory);
            if (
                DateTime.TryParseExact(
                    name,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var backupDay
                )
                && backupDay.Date < cutoff
            )
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private void IsolateDatabaseFiles()
    {
        var stamp = DateTimeOffset.UtcNow.ToString(
            "yyyyMMddHHmmssfffffff",
            CultureInfo.InvariantCulture
        );
        foreach (
            var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" }
        )
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var target = $"{path}.corrupt.{stamp}";
            try
            {
                File.Move(path, target, overwrite: true);
            }
            catch { }
        }
    }

    private bool HasInvalidSqliteHeader()
    {
        if (!File.Exists(_databasePath))
        {
            return false;
        }

        try
        {
            using var stream = File.Open(
                _databasePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );
            if (stream.Length == 0)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[16];
            var read = stream.Read(header);
            return read < header.Length || !header.SequenceEqual("SQLite format 3\0"u8);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCorruptionException(SqliteException exception)
    {
        return exception.SqliteErrorCode is 11 or 26
            || exception.Message.Contains(
                "database disk image is malformed",
                StringComparison.OrdinalIgnoreCase
            )
            || exception.Message.Contains(
                "file is not a database",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private sealed class UsageApiBuilder
    {
        public long TotalRequests { get; set; }

        public long TotalTokens { get; set; }

        public Dictionary<string, UsageModelBuilder> Models { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class UsageModelBuilder
    {
        public long TotalRequests { get; set; }

        public long TotalTokens { get; set; }

        public List<ManagementUsageRequestDetail> Details { get; } = [];
    }
}

internal sealed record UsageKeeperSyncResult(
    long SnapshotRunId,
    int InsertedEvents,
    int DedupedEvents,
    string PayloadHash,
    DateTimeOffset? ExportedAt,
    string BackupFilePath
);

internal sealed record UsageKeeperStatus(
    long EventCount,
    DateTimeOffset? LastEventAt,
    DateTimeOffset? LastSyncAt,
    string? LastError
);
