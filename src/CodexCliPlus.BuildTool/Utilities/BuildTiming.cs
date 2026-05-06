using System.Diagnostics;

namespace CodexCliPlus.BuildTool;

public static class BuildTiming
{
    public static async Task<int> TimeAsync(
        BuildContext context,
        string stage,
        Func<Task<int>> action
    )
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await action();
        }
        finally
        {
            stopwatch.Stop();
            context.Logger.Info($"{stage} duration: {FormatDuration(stopwatch.Elapsed)}");
        }
    }

    public static async Task<T> TimeAsync<T>(
        BuildContext context,
        string stage,
        Func<Task<T>> action
    )
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await action();
        }
        finally
        {
            stopwatch.Stop();
            context.Logger.Info($"{stage} duration: {FormatDuration(stopwatch.Elapsed)}");
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalSeconds >= 1
            ? $"{duration.TotalSeconds:0.00}s"
            : $"{duration.TotalMilliseconds:0}ms";
    }
}
