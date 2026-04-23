using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using CPAD.Core.Models.Management;

using WpfOrientation = System.Windows.Controls.Orientation;

namespace CPAD;

public partial class MainWindow
{
    private ManagementUsageSnapshot? _quotaSnapshot;
    private IReadOnlyList<ManagementAuthFileItem>? _quotaAuthFiles;
    private bool _quotaLoading;
    private string? _quotaError;
    private DateTimeOffset? _quotaLastLoadedAt;

    private UIElement BuildQuotaContent()
    {
        if (_quotaLoading && _quotaSnapshot is null)
        {
            return CreateStatePanel(
                "Loading quota and usage data...",
                "Reading live request, token, per-API, per-model, and auth-file availability data from the managed backend.");
        }

        if (!string.IsNullOrWhiteSpace(_quotaError) && _quotaSnapshot is null)
        {
            return CreateStatePanel("Quota and usage data is unavailable.", _quotaError);
        }

        var usage = _quotaSnapshot;
        if (usage is null)
        {
            return CreateStatePanel(
                "No quota and usage data loaded yet.",
                "Open this route again or refresh after the managed backend starts exposing usage statistics.");
        }

        var authFiles = _quotaAuthFiles ?? [];
        var apiSummaries = BuildUsageApiSummaries(usage);
        var modelSummaries = BuildUsageModelSummaries(usage);
        var requestEvents = BuildUsageRequestEvents(usage);

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateQuotaHero(usage, authFiles, apiSummaries.Count, modelSummaries.Count));

        if (!string.IsNullOrWhiteSpace(_quotaError))
        {
            root.Children.Add(CreateHintCard("Refresh issue", _quotaError));
        }

        root.Children.Add(CreateSectionHeader("Usage Summary"));
        root.Children.Add(CreateUsageSummaryPanel(usage, authFiles, apiSummaries.Count, modelSummaries.Count));

        root.Children.Add(CreateSectionHeader("Quota Signals"));
        root.Children.Add(CreateQuotaSignalsPanel(authFiles));

        root.Children.Add(CreateSectionHeader("Requests by API"));
        root.Children.Add(CreateUsageApiPanel(apiSummaries));

        root.Children.Add(CreateSectionHeader("Requests by Model"));
        root.Children.Add(CreateUsageModelPanel(modelSummaries));

        root.Children.Add(CreateSectionHeader("Recent Request Events"));
        root.Children.Add(CreateUsageRequestEventsPanel(requestEvents));

        return root;
    }

    private async Task RefreshQuotaAsync(bool force)
    {
        if (_quotaLoading)
        {
            return;
        }

        if (!force && _quotaSnapshot is not null)
        {
            return;
        }

        _quotaLoading = true;
        _quotaError = null;
        RefreshQuotaSection();

        try
        {
            ManagementUsageSnapshot? usage = null;
            IReadOnlyList<ManagementAuthFileItem>? authFiles = null;
            var errors = new List<string>();

            try
            {
                usage = (await _usageService.GetUsageAsync()).Value;
            }
            catch (Exception exception)
            {
                errors.Add($"Usage statistics: {exception.Message}");
            }

            try
            {
                authFiles = (await _authService.GetAuthFilesAsync()).Value
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception)
            {
                errors.Add($"Auth inventory: {exception.Message}");
            }

            if (usage is null)
            {
                throw new InvalidOperationException(
                    errors.Count == 0
                        ? "The usage endpoint did not return a snapshot."
                        : string.Join(" | ", errors));
            }

            _quotaSnapshot = usage;
            _quotaAuthFiles = authFiles ?? [];
            _quotaLastLoadedAt = DateTimeOffset.Now;
            _quotaError = errors.Count == 0 ? null : string.Join(" | ", errors);
        }
        catch (Exception exception)
        {
            _quotaError = exception.Message;
        }
        finally
        {
            _quotaLoading = false;
            RefreshQuotaSection();
        }
    }

    private UIElement CreateQuotaHero(
        ManagementUsageSnapshot usage,
        IReadOnlyList<ManagementAuthFileItem> authFiles,
        int apiCount,
        int modelCount)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            "Live quota and usage analytics from the managed backend",
            22,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        panel.Children.Add(CreateText(
            "This page focuses on real request volume, token load, per-API and per-model breakdowns, and auth credentials currently affected by disablement, cooldown, or availability issues.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 0)));
        panel.Children.Add(CreateMetricGrid(
            CreateMetricCard("Total Requests", FormatCompact(usage.TotalRequests), $"{FormatCompact(usage.SuccessCount)} success / {FormatCompact(usage.FailureCount)} failed"),
            CreateMetricCard("Total Tokens", FormatCompact(usage.TotalTokens), $"Across {apiCount} APIs and {modelCount} models"),
            CreateMetricCard("Auth Signals", BuildAuthSignalCount(authFiles).ToString(CultureInfo.InvariantCulture), "Disabled, unavailable, or cooldown credentials")));
        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateUsageSummaryPanel(
        ManagementUsageSnapshot usage,
        IReadOnlyList<ManagementAuthFileItem> authFiles,
        int apiCount,
        int modelCount)
    {
        var successRate = usage.TotalRequests == 0
            ? "0%"
            : $"{(usage.SuccessCount * 100d / usage.TotalRequests):0.#}%";
        var rpm = usage.RequestsByHour.Count == 0
            ? 0d
            : usage.RequestsByHour.Values.Average() / 60d;
        var tpm = usage.TokensByHour.Count == 0
            ? 0d
            : usage.TokensByHour.Values.Average() / 60d;

        return CreateMetricGrid(
            CreateMetricCard("Success Rate", successRate, "Share of successful requests in the current usage snapshot"),
            CreateMetricCard("Average RPM", $"{rpm:0.0}", "Average requests per minute across hourly buckets"),
            CreateMetricCard("Average TPM", $"{tpm:0.0}", "Average tokens per minute across hourly buckets"),
            CreateMetricCard("APIs", apiCount.ToString(CultureInfo.InvariantCulture), "Distinct API sources currently tracked"),
            CreateMetricCard("Models", modelCount.ToString(CultureInfo.InvariantCulture), "Distinct models currently tracked"),
            CreateMetricCard("Loaded", _quotaLastLoadedAt is null ? "-" : _quotaLastLoadedAt.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture), $"{BuildAuthSignalCount(authFiles)} credential signal(s) need attention"));
    }

    private UIElement CreateQuotaSignalsPanel(IReadOnlyList<ManagementAuthFileItem> authFiles)
    {
        if (authFiles.Count == 0)
        {
            return CreateStatePanel(
                "No auth files are available for quota signals.",
                "Add credentials on the Accounts & Auth page to correlate availability and cooldown state with usage.");
        }

        var now = DateTimeOffset.Now;
        var disabledCount = authFiles.Count(item => item.Disabled);
        var unavailableCount = authFiles.Count(item => item.Unavailable);
        var cooldownCount = authFiles.Count(item => item.NextRetryAfter is { } retryAfter && retryAfter > now);
        var flagged = authFiles
            .Where(item => item.Disabled || item.Unavailable || item.NextRetryAfter is { } retryAfter && retryAfter > now)
            .OrderByDescending(item => item.Unavailable)
            .ThenByDescending(item => item.Disabled)
            .ThenBy(item => item.NextRetryAfter ?? DateTimeOffset.MaxValue)
            .ToArray();

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Disabled", disabledCount.ToString(CultureInfo.InvariantCulture), "Credentials intentionally removed from routing"),
            CreateMetricCard("Unavailable", unavailableCount.ToString(CultureInfo.InvariantCulture), "Credentials currently reporting temporary availability issues"),
            CreateMetricCard("Cooldown", cooldownCount.ToString(CultureInfo.InvariantCulture), "Credentials waiting for next retry time")));

        if (flagged.Length == 0)
        {
            root.Children.Add(CreateHintCard(
                "Quota status",
                "No auth file is currently disabled, unavailable, or waiting for a retry window."));
            return root;
        }

        foreach (var item in flagged.Take(10))
        {
            var details = new UniformGrid
            {
                Columns = 2,
                Margin = new Thickness(0, 10, 0, 0)
            };
            AddKeyValue(details, "Credential", item.Email ?? item.Account ?? item.Name);
            AddKeyValue(details, "Provider", item.Provider ?? item.Type ?? "Unknown");
            AddKeyValue(details, "Status", BuildQuotaSignalStatus(item));
            AddKeyValue(details, "Retry", item.NextRetryAfter is null ? "-" : FormatTimestamp(item.NextRetryAfter));

            var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
            panel.Children.Add(CreateText(item.Name, 14, FontWeights.SemiBold, "PrimaryTextBrush"));
            panel.Children.Add(details);
            root.Children.Add(CreateCard(panel, new Thickness(0, 0, 0, 12)));
        }

        return root;
    }

    private UIElement CreateUsageApiPanel(IReadOnlyList<UsageApiSummary> apiSummaries)
    {
        if (apiSummaries.Count == 0)
        {
            return CreateStatePanel(
                "No API usage has been recorded yet.",
                "The backend has not emitted usage data yet, or usage statistics are still empty.");
        }

        var root = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 18)
        };

        foreach (var summary in apiSummaries)
        {
            var details = new UniformGrid
            {
                Columns = 2,
                Margin = new Thickness(0, 10, 0, 0)
            };
            AddKeyValue(details, "Requests", FormatCompact(summary.TotalRequests));
            AddKeyValue(details, "Tokens", FormatCompact(summary.TotalTokens));
            AddKeyValue(details, "Models", summary.ModelCount.ToString(CultureInfo.InvariantCulture));
            AddKeyValue(details, "Tokens / Request", summary.TokensPerRequest <= 0 ? "0" : $"{summary.TokensPerRequest:0.0}");

            var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
            panel.Children.Add(CreateText(summary.Name, 15, FontWeights.SemiBold, "PrimaryTextBrush"));
            panel.Children.Add(CreateText(
                summary.TopModels.Count == 0
                    ? "No model-level breakdown recorded yet."
                    : $"Top models: {string.Join(", ", summary.TopModels)}",
                12,
                FontWeights.Normal,
                "SecondaryTextBrush",
                new Thickness(0, 6, 0, 0)));
            panel.Children.Add(details);

            root.Children.Add(CreateCard(panel, new Thickness(0, 0, 12, 12)));
        }

        return root;
    }

    private UIElement CreateUsageModelPanel(IReadOnlyList<UsageModelSummary> modelSummaries)
    {
        if (modelSummaries.Count == 0)
        {
            return CreateStatePanel(
                "No model statistics are available yet.",
                "The backend has not emitted model-level usage events.");
        }

        var root = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 18)
        };

        foreach (var summary in modelSummaries.Take(12))
        {
            var details = new UniformGrid
            {
                Columns = 2,
                Margin = new Thickness(0, 10, 0, 0)
            };
            AddKeyValue(details, "Requests", FormatCompact(summary.TotalRequests));
            AddKeyValue(details, "Tokens", FormatCompact(summary.TotalTokens));
            AddKeyValue(details, "Input / Output", $"{FormatCompact(summary.InputTokens)} / {FormatCompact(summary.OutputTokens)}");
            AddKeyValue(details, "Cached / Reasoning", $"{FormatCompact(summary.CachedTokens)} / {FormatCompact(summary.ReasoningTokens)}");

            var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
            panel.Children.Add(CreateText(summary.Name, 15, FontWeights.SemiBold, "PrimaryTextBrush"));
            panel.Children.Add(CreateText(
                $"APIs: {string.Join(", ", summary.ApiNames)}",
                12,
                FontWeights.Normal,
                "SecondaryTextBrush",
                new Thickness(0, 6, 0, 0)));
            panel.Children.Add(details);

            root.Children.Add(CreateCard(panel, new Thickness(0, 0, 12, 12)));
        }

        return root;
    }

    private UIElement CreateUsageRequestEventsPanel(IReadOnlyList<UsageRequestEvent> requestEvents)
    {
        if (requestEvents.Count == 0)
        {
            return CreateStatePanel(
                "No request event details are available yet.",
                "Detailed request events appear here once the backend records per-request usage entries.");
        }

        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };

        foreach (var item in requestEvents.Take(12))
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var timestamp = item.Timestamp is null
                ? "-"
                : item.Timestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            AddUsageEventCell(grid, 0, timestamp);
            AddUsageEventCell(grid, 1, $"{item.Api} / {item.Model}");
            AddUsageEventCell(grid, 2, item.Failed ? "Failed" : "Success");
            AddUsageEventCell(grid, 3, item.LatencyMs is null ? "-" : $"{item.LatencyMs} ms");
            AddUsageEventCell(
                grid,
                4,
                $"Tokens {FormatCompact(item.Tokens.TotalTokens)} | Source {item.Source ?? "-"} | Auth {item.AuthIndex ?? "-"}");

            panel.Children.Add(grid);
        }

        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private void RefreshQuotaSection()
    {
        if ((NavigationList.SelectedItem as ShellSection)?.Key == "quota")
        {
            UpdateSelectedSection();
        }
    }

    private static IReadOnlyList<UsageApiSummary> BuildUsageApiSummaries(ManagementUsageSnapshot snapshot)
    {
        return snapshot.Apis
            .Select(pair => new UsageApiSummary(
                pair.Key,
                pair.Value.TotalRequests,
                pair.Value.TotalTokens,
                pair.Value.Models.Count,
                pair.Value.TotalRequests == 0 ? 0d : pair.Value.TotalTokens / (double)pair.Value.TotalRequests,
                pair.Value.Models
                    .OrderByDescending(model => model.Value.TotalRequests)
                    .ThenByDescending(model => model.Value.TotalTokens)
                    .Take(3)
                    .Select(model => model.Key)
                    .ToArray()))
            .OrderByDescending(item => item.TotalRequests)
            .ThenByDescending(item => item.TotalTokens)
            .ToArray();
    }

    private static IReadOnlyList<UsageModelSummary> BuildUsageModelSummaries(ManagementUsageSnapshot snapshot)
    {
        var builders = new Dictionary<string, UsageModelSummaryBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var api in snapshot.Apis)
        {
            foreach (var model in api.Value.Models)
            {
                if (!builders.TryGetValue(model.Key, out var builder))
                {
                    builder = new UsageModelSummaryBuilder(model.Key);
                    builders[model.Key] = builder;
                }

                builder.ApiNames.Add(api.Key);
                builder.TotalRequests += model.Value.TotalRequests;
                builder.TotalTokens += model.Value.TotalTokens;

                foreach (var detail in model.Value.Details)
                {
                    builder.InputTokens += detail.Tokens.InputTokens;
                    builder.OutputTokens += detail.Tokens.OutputTokens;
                    builder.CachedTokens += detail.Tokens.CachedTokens;
                    builder.ReasoningTokens += detail.Tokens.ReasoningTokens;
                }
            }
        }

        return builders.Values
            .Select(builder => new UsageModelSummary(
                builder.Name,
                builder.TotalRequests,
                builder.TotalTokens,
                builder.InputTokens,
                builder.OutputTokens,
                builder.CachedTokens,
                builder.ReasoningTokens,
                builder.ApiNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderByDescending(item => item.TotalRequests)
            .ThenByDescending(item => item.TotalTokens)
            .ToArray();
    }

    private static IReadOnlyList<UsageRequestEvent> BuildUsageRequestEvents(ManagementUsageSnapshot snapshot)
    {
        return snapshot.Apis
            .SelectMany(api => api.Value.Models.SelectMany(model => model.Value.Details.Select(detail => new UsageRequestEvent(
                api.Key,
                model.Key,
                detail.Timestamp,
                detail.Source,
                detail.AuthIndex,
                detail.LatencyMs,
                detail.Failed,
                detail.Tokens))))
            .OrderByDescending(item => item.Timestamp ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Api, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Model, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int BuildAuthSignalCount(IReadOnlyList<ManagementAuthFileItem> authFiles)
    {
        var now = DateTimeOffset.Now;
        return authFiles.Count(item => item.Disabled || item.Unavailable || item.NextRetryAfter is { } retryAfter && retryAfter > now);
    }

    private static string BuildQuotaSignalStatus(ManagementAuthFileItem item)
    {
        if (item.Disabled)
        {
            return "Disabled";
        }

        if (item.Unavailable)
        {
            return string.IsNullOrWhiteSpace(item.StatusMessage)
                ? "Unavailable"
                : $"Unavailable | {item.StatusMessage}";
        }

        if (item.NextRetryAfter is { } retryAfter && retryAfter > DateTimeOffset.Now)
        {
            return "Cooldown";
        }

        return "Active";
    }

    private static void AddUsageEventCell(Grid grid, int columnIndex, string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 12, 0)
        };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        Grid.SetColumn(textBlock, columnIndex);
        grid.Children.Add(textBlock);
    }

    private sealed record UsageApiSummary(
        string Name,
        long TotalRequests,
        long TotalTokens,
        int ModelCount,
        double TokensPerRequest,
        IReadOnlyList<string> TopModels);

    private sealed record UsageModelSummary(
        string Name,
        long TotalRequests,
        long TotalTokens,
        long InputTokens,
        long OutputTokens,
        long CachedTokens,
        long ReasoningTokens,
        IReadOnlyList<string> ApiNames);

    private sealed record UsageRequestEvent(
        string Api,
        string Model,
        DateTimeOffset? Timestamp,
        string? Source,
        string? AuthIndex,
        long? LatencyMs,
        bool Failed,
        ManagementUsageTokenStats Tokens);

    private sealed class UsageModelSummaryBuilder
    {
        public UsageModelSummaryBuilder(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public long TotalRequests { get; set; }

        public long TotalTokens { get; set; }

        public long InputTokens { get; set; }

        public long OutputTokens { get; set; }

        public long CachedTokens { get; set; }

        public long ReasoningTokens { get; set; }

        public HashSet<string> ApiNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
