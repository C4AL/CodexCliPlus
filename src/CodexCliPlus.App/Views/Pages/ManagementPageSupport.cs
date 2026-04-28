using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

using CodexCliPlus.Management.DesignSystem.Controls;

using MessageBox = System.Windows.MessageBox;

namespace CodexCliPlus.Views.Pages;

internal static class ManagementPageSupport
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string FormatValue(string? value, string fallback = "未设置")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public static string FormatBoolean(bool? value)
    {
        return value switch
        {
            true => "已启用",
            false => "已禁用",
            _ => "未设置"
        };
    }

    public static string FormatDate(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) ?? "未记录";
    }

    public static string FormatFileSize(long? bytes)
    {
        if (bytes is null)
        {
            return "未知大小";
        }

        if (bytes < 1024)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{bytes} B");
        }

        var kb = bytes.Value / 1024d;
        if (kb < 1024)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{kb:F1} KB");
        }

        var mb = kb / 1024d;
        return string.Create(CultureInfo.CurrentCulture, $"{mb:F1} MB");
    }

    public static string FormatUnixTimestamp(long? value)
    {
        if (value is null or <= 0)
        {
            return "未记录";
        }

        var absolute = Math.Abs(value.Value);
        var dateTime = absolute switch
        {
            < 100_000_000_000L => DateTimeOffset.FromUnixTimeSeconds(value.Value),
            < 100_000_000_000_000L => DateTimeOffset.FromUnixTimeMilliseconds(value.Value),
            < 100_000_000_000_000_000L => DateTimeOffset.FromUnixTimeMilliseconds(value.Value / 1000),
            _ => DateTimeOffset.FromUnixTimeMilliseconds(value.Value / 1_000_000)
        };

        return FormatDate(dateTime);
    }

    public static string ToJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static BadgeTone GetTone(string? error)
    {
        return string.IsNullOrWhiteSpace(error) ? BadgeTone.Accent : BadgeTone.Danger;
    }

    public static int CountOccurrences(string source, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        var count = 0;
        var start = 0;
        while (start < source.Length)
        {
            var index = source.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                break;
            }

            count++;
            start = index + query.Length;
        }

        return count;
    }

    public static int CountChangedLines(string before, string after)
    {
        var beforeLines = before.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var afterLines = after.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var max = Math.Max(beforeLines.Length, afterLines.Length);
        var count = 0;

        for (var index = 0; index < max; index++)
        {
            var left = index < beforeLines.Length ? beforeLines[index] : string.Empty;
            var right = index < afterLines.Length ? afterLines[index] : string.Empty;
            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    public static void ShowInfo(Page owner, string title, string message)
    {
        MessageBox.Show(Window.GetWindow(owner), message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static void ShowError(Page owner, string title, Exception exception)
    {
        MessageBox.Show(Window.GetWindow(owner), exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

internal sealed record ManagementMetricItem(string Value, string Label, string Detail);

internal sealed record ManagementKeyValueItem(string Label, string Value);

internal sealed record UsageApiSummaryItem(string Provider, long TotalRequests, long TotalTokens, int ModelCount);

internal sealed record ErrorLogViewItem(string Name, string Size, string Modified);
