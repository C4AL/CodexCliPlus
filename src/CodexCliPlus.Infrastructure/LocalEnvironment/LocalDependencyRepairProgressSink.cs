using System.Text.Json;
using System.Text.Json.Serialization;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Models.LocalEnvironment;
using CodexCliPlus.Infrastructure.Utilities;

namespace CodexCliPlus.Infrastructure.LocalEnvironment;

internal interface ILocalDependencyRepairProgressSink
{
    LocalDependencyRepairProgress? LastProgress { get; }

    Task ReportAsync(
        LocalDependencyRepairProgress progress,
        CancellationToken cancellationToken = default
    );
}

internal sealed class FileLocalDependencyRepairProgressSink(string statusPath)
    : ILocalDependencyRepairProgressSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public LocalDependencyRepairProgress? LastProgress { get; private set; }

    public string StatusPath { get; } = statusPath;

    public async Task ReportAsync(
        LocalDependencyRepairProgress progress,
        CancellationToken cancellationToken = default
    )
    {
        await AtomicFileWriter.WriteUtf8NoBomTextAsync(
            StatusPath,
            JsonSerializer.Serialize(progress, JsonOptions),
            cancellationToken
        );
        LastProgress = progress;
    }

}

internal sealed class CallbackLocalDependencyRepairProgressSink(
    Action<LocalDependencyRepairProgress>? progressReporter,
    IAppLogger logger
) : ILocalDependencyRepairProgressSink
{
    public LocalDependencyRepairProgress? LastProgress { get; private set; }

    public Task ReportAsync(
        LocalDependencyRepairProgress progress,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastProgress = progress;

        try
        {
            progressReporter?.Invoke(progress);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Warn(
                $"Failed to report local dependency repair progress: {exception.Message}"
            );
        }

        return Task.CompletedTask;
    }
}
