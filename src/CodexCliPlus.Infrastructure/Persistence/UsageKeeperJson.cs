using System.Text.Json;
using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.Infrastructure.Persistence;

internal static class UsageKeeperJson
{
    private static readonly JsonSerializerOptions RawExportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public static string SerializeExport(ManagementUsageExportPayload payload)
    {
        return JsonSerializer.Serialize(
            new
            {
                version = payload.Version <= 0 ? 1 : payload.Version,
                exported_at = payload.ExportedAt?.ToUniversalTime(),
                usage = payload.Usage,
            },
            RawExportJsonOptions
        );
    }
}
