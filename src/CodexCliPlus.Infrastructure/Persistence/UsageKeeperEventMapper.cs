using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.Infrastructure.Persistence;

internal static class UsageKeeperEventMapper
{
    public static IReadOnlyList<UsageKeeperEvent> Flatten(
        long snapshotRunId,
        ManagementUsageExportPayload payload
    )
    {
        if (payload.Usage.Apis.Count == 0)
        {
            return [];
        }

        var events = new List<UsageKeeperEvent>();
        foreach (var apiPair in payload.Usage.Apis)
        {
            var apiGroupKey = apiPair.Key.Trim();
            if (string.IsNullOrWhiteSpace(apiGroupKey))
            {
                continue;
            }

            foreach (var modelPair in apiPair.Value.Models)
            {
                var model = modelPair.Key.Trim();
                if (string.IsNullOrWhiteSpace(model))
                {
                    model = "unknown";
                }

                foreach (var detail in modelPair.Value.Details)
                {
                    var tokens = NormalizeTokens(detail.Tokens);
                    var timestamp =
                        detail.Timestamp?.ToUniversalTime()
                        ?? payload.ExportedAt?.ToUniversalTime()
                        ?? DateTimeOffset.MinValue;
                    var source = detail.Source?.Trim() ?? string.Empty;
                    var authIndex = detail.AuthIndex?.Trim() ?? string.Empty;

                    events.Add(
                        new UsageKeeperEvent
                        {
                            SnapshotRunId = snapshotRunId,
                            EventKey = BuildEventKey(
                                apiGroupKey,
                                model,
                                timestamp,
                                source,
                                authIndex,
                                detail.Failed,
                                tokens
                            ),
                            ApiGroupKey = apiGroupKey,
                            Model = model,
                            Timestamp = timestamp,
                            Source = source,
                            AuthIndex = authIndex,
                            Failed = detail.Failed,
                            LatencyMs = Math.Max(detail.LatencyMs ?? 0, 0),
                            InputTokens = tokens.InputTokens,
                            OutputTokens = tokens.OutputTokens,
                            ReasoningTokens = tokens.ReasoningTokens,
                            CachedTokens = tokens.CachedTokens,
                            TotalTokens = tokens.TotalTokens,
                        }
                    );
                }
            }
        }

        return events;
    }

    public static string BuildEventKey(
        string apiGroupKey,
        string model,
        DateTimeOffset timestamp,
        string? source,
        string? authIndex,
        bool failed,
        ManagementUsageTokenStats tokens
    )
    {
        var normalized = NormalizeTokens(tokens);
        var payload =
            $"{apiGroupKey.Trim()}|{model.Trim()}|{FormatRfc3339Nano(timestamp)}|{(source ?? string.Empty).Trim()}|{(authIndex ?? string.Empty).Trim()}|{failed.ToString().ToLowerInvariant()}|{normalized.InputTokens}|{normalized.OutputTokens}|{normalized.ReasoningTokens}|{normalized.CachedTokens}|{normalized.TotalTokens}";
        var sum = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(sum).ToLowerInvariant();
    }

    public static ManagementUsageTokenStats NormalizeTokens(ManagementUsageTokenStats tokens)
    {
        var total = tokens.TotalTokens;
        if (total == 0)
        {
            total = tokens.InputTokens + tokens.OutputTokens + tokens.ReasoningTokens;
        }

        if (total == 0)
        {
            total =
                tokens.InputTokens
                + tokens.OutputTokens
                + tokens.ReasoningTokens
                + tokens.CachedTokens;
        }

        return new ManagementUsageTokenStats
        {
            InputTokens = tokens.InputTokens,
            OutputTokens = tokens.OutputTokens,
            ReasoningTokens = tokens.ReasoningTokens,
            CachedTokens = tokens.CachedTokens,
            TotalTokens = total,
        };
    }

    internal static string FormatRfc3339Nano(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var baseText = utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var ticks = utc.Ticks % TimeSpan.TicksPerSecond;
        if (ticks == 0)
        {
            return $"{baseText}Z";
        }

        var fraction = ticks.ToString("0000000", CultureInfo.InvariantCulture).TrimEnd('0');
        return $"{baseText}.{fraction}Z";
    }
}

internal sealed class UsageKeeperEvent
{
    public long Id { get; init; }

    public string EventKey { get; init; } = string.Empty;

    public long SnapshotRunId { get; set; }

    public string ApiGroupKey { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }

    public string Source { get; init; } = string.Empty;

    public string AuthIndex { get; init; } = string.Empty;

    public bool Failed { get; init; }

    public long LatencyMs { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long ReasoningTokens { get; init; }

    public long CachedTokens { get; init; }

    public long TotalTokens { get; init; }
}
