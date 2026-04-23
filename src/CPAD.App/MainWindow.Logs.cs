using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using CPAD.Core.Exceptions;
using CPAD.Core.Models;
using CPAD.Core.Models.Management;

using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfControl = System.Windows.Controls.Control;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace CPAD;

public partial class MainWindow
{
    private ManagementLogsSnapshot? _logsSnapshot;
    private IReadOnlyList<ManagementErrorLogFile> _requestErrorLogs = [];
    private bool _logsLoading;
    private bool _logsClearing;
    private bool _requestLogLoading;
    private string? _logsError;
    private string? _logsStatusMessage;
    private string _logSearchDraft = string.Empty;
    private string _logLevelFilterDraft = "all";
    private bool _hideManagementLogDraft = true;
    private string _requestLogIdDraft = string.Empty;
    private string? _requestLogPreview;

    private UIElement BuildLogsContent()
    {
        if (_logsLoading && _logsSnapshot is null)
        {
            return CreateStatePanel(
                "Loading logs and request diagnostics...",
                "Reading /logs and /request-error-logs from the managed backend.");
        }

        if (!string.IsNullOrWhiteSpace(_logsError) && _logsSnapshot is null)
        {
            return CreateStatePanel("Logs are unavailable.", _logsError);
        }

        var snapshot = _logsSnapshot;
        if (snapshot is null)
        {
            return CreateStatePanel(
                "No log data loaded yet.",
                "Open this route again or refresh after the managed backend is running.");
        }

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        var filteredLines = FilterLogLines(snapshot.Lines).ToArray();
        root.Children.Add(CreateLogsHero(snapshot, filteredLines.Length));

        if (!string.IsNullOrWhiteSpace(_logsStatusMessage))
        {
            root.Children.Add(CreateHintCard("Log action", _logsStatusMessage));
        }

        if (!string.IsNullOrWhiteSpace(_logsError))
        {
            root.Children.Add(CreateHintCard("Log issue", _logsError));
        }

        root.Children.Add(CreateSectionHeader("Log Browser"));
        root.Children.Add(CreateLogBrowserCard(snapshot, filteredLines));

        root.Children.Add(CreateSectionHeader("Request Diagnostics"));
        root.Children.Add(CreateRequestDiagnosticsCard());

        root.Children.Add(CreateSectionHeader("Diagnostics Export"));
        root.Children.Add(CreateDiagnosticsExportCard());

        return root;
    }

    private async Task RefreshLogsAsync(bool force)
    {
        if (_logsLoading)
        {
            return;
        }

        if (!force && _logsSnapshot is not null)
        {
            return;
        }

        _logsLoading = true;
        _logsError = null;
        RefreshLogsSection();

        try
        {
            var logsTask = _logsService.GetLogsAsync(limit: 800);
            var errorLogsTask = _logsService.GetRequestErrorLogsAsync();
            await Task.WhenAll(logsTask, errorLogsTask);

            _logsSnapshot = logsTask.Result.Value;
            _requestErrorLogs = errorLogsTask.Result.Value
                .OrderByDescending(item => item.Modified ?? 0)
                .ToArray();
            _logsStatusMessage = $"Loaded {_logsSnapshot.Lines.Count.ToString(CultureInfo.InvariantCulture)} visible log lines from /logs.";
        }
        catch (Exception exception)
        {
            _logsError = exception.Message;
            _logsSnapshot ??= new ManagementLogsSnapshot();
            _requestErrorLogs = [];
        }
        finally
        {
            _logsLoading = false;
            RefreshLogsSection();
        }
    }

    private async Task ClearLogsAsync()
    {
        if (_logsClearing)
        {
            return;
        }

        _logsClearing = true;
        _logsError = null;
        _logsStatusMessage = "Clearing backend log files through DELETE /logs...";
        RefreshLogsSection();

        try
        {
            var result = await _logsService.ClearLogsAsync();
            _logsStatusMessage = result.Value.Message ??
                $"Logs cleared. Removed rotated files: {(result.Value.Removed ?? 0).ToString(CultureInfo.InvariantCulture)}.";
            await RefreshLogsAsync(force: true);
        }
        catch (Exception exception)
        {
            _logsError = exception.Message;
            _logsStatusMessage = "Clear logs failed.";
        }
        finally
        {
            _logsClearing = false;
            RefreshLogsSection();
        }
    }

    private async Task LoadRequestLogByIdAsync()
    {
        if (_requestLogLoading)
        {
            return;
        }

        var requestId = _requestLogIdDraft.Trim();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            _logsStatusMessage = "Enter a request ID before loading a request log.";
            RefreshLogsSection();
            return;
        }

        _requestLogLoading = true;
        _logsError = null;
        _requestLogPreview = null;
        RefreshLogsSection();

        try
        {
            var response = await _logsService.GetRequestLogByIdAsync(requestId);
            _requestLogPreview = response.Value;
            _logsStatusMessage = $"Loaded request log for ID {requestId}.";
        }
        catch (ManagementApiException exception) when (exception.StatusCode == 404)
        {
            _logsStatusMessage = $"No request log file was found for ID {requestId}.";
        }
        catch (Exception exception)
        {
            _logsError = exception.Message;
            _logsStatusMessage = "Request log lookup failed.";
        }
        finally
        {
            _requestLogLoading = false;
            RefreshLogsSection();
        }
    }

    private Task ExportDiagnosticsAsync()
    {
        try
        {
            var packagePath = _diagnosticsService.ExportPackage(
                _backendProcessManager.CurrentStatus,
                BuildCodexDiagnosticsSnapshot(),
                GetDependencyStatus());
            _logsStatusMessage = $"Diagnostic package exported: {packagePath}";
        }
        catch (Exception exception)
        {
            _logsError = exception.Message;
            _logsStatusMessage = "Diagnostic package export failed.";
        }

        RefreshLogsSection();
        return Task.CompletedTask;
    }

    private UIElement CreateLogsHero(ManagementLogsSnapshot snapshot, int filteredCount)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            "Independent log and request diagnostics",
            22,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        panel.Children.Add(CreateText(
            "This page keeps log browsing out of the main shell layout while still exposing live backend logs, request log lookup, error-log inventory, and desktop diagnostic package export.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 14)));
        panel.Children.Add(CreateMetricGrid(
            CreateMetricCard("Buffered Lines", snapshot.Lines.Count.ToString(CultureInfo.InvariantCulture), "Latest /logs response"),
            CreateMetricCard("Filtered Lines", filteredCount.ToString(CultureInfo.InvariantCulture), "Current search and level filters"),
            CreateMetricCard("Backend Count", snapshot.LineCount.ToString(CultureInfo.InvariantCulture), "line-count from /logs"),
            CreateMetricCard("Request Error Logs", _requestErrorLogs.Count.ToString(CultureInfo.InvariantCulture), "Files from /request-error-logs"),
            CreateMetricCard("Latest Timestamp", FormatUnixTimestamp(snapshot.LatestTimestamp), "latest-timestamp from /logs"),
            CreateMetricCard("Backend", _backendProcessManager.CurrentStatus.State.ToString(), _backendProcessManager.CurrentStatus.Message)));
        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateLogBrowserCard(ManagementLogsSnapshot snapshot, IReadOnlyList<string> filteredLines)
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "Filter and inspect backend file logs without turning the main UI into a terminal.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        filters.Children.Add(CreateLogSearchBox());
        filters.Children.Add(CreateLogLevelFilter());
        filters.Children.Add(CreateHideManagementLogsToggle());
        filters.Children.Add(CreateActionButton("Apply Filters", () =>
        {
            RefreshLogsSection();
            return Task.CompletedTask;
        }));
        filters.Children.Add(CreateActionButton("Refresh Logs", () => RefreshLogsAsync(force: true)));
        filters.Children.Add(CreateActionButton(_logsClearing ? "Clearing..." : "Clear Logs", ClearLogsAsync));
        root.Children.Add(filters);

        if (filteredLines.Count == 0)
        {
            root.Children.Add(CreateStatePanel(
                "No log lines match the current filters.",
                snapshot.Lines.Count == 0
                    ? "The backend did not return log lines. Logging to file may be disabled or no log file has been created yet."
                    : "Clear the search text or lower the severity filter."));
            return CreateCard(root, new Thickness(0, 0, 0, 18));
        }

        var textBox = new WpfTextBox
        {
            Text = string.Join(Environment.NewLine, filteredLines.TakeLast(400)),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 330,
            FontFamily = new WpfFontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1)
        };
        textBox.SetResourceReference(WpfControl.ForegroundProperty, "PrimaryTextBrush");
        textBox.SetResourceReference(WpfControl.BackgroundProperty, "SurfaceBrush");
        textBox.SetResourceReference(WpfControl.BorderBrushProperty, "BorderBrush");
        root.Children.Add(textBox);

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateRequestDiagnosticsCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "Request diagnostics expose backend request-error files and direct request-log lookup by request ID.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        var requestLookup = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var idBox = new WpfTextBox
        {
            Text = _requestLogIdDraft,
            Width = 260,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 10, 10)
        };
        idBox.SetResourceReference(WpfControl.ForegroundProperty, "PrimaryTextBrush");
        idBox.SetResourceReference(WpfControl.BackgroundProperty, "SurfaceBrush");
        idBox.SetResourceReference(WpfControl.BorderBrushProperty, "BorderBrush");
        idBox.TextChanged += (_, _) => _requestLogIdDraft = idBox.Text;
        requestLookup.Children.Add(idBox);
        requestLookup.Children.Add(CreateActionButton(_requestLogLoading ? "Loading..." : "Load Request Log", LoadRequestLogByIdAsync));
        root.Children.Add(requestLookup);

        if (!string.IsNullOrWhiteSpace(_requestLogPreview))
        {
            root.Children.Add(CreateText("Request Log Preview", 14, FontWeights.SemiBold, "PrimaryTextBrush", new Thickness(0, 0, 0, 8)));
            root.Children.Add(CreateReadOnlyLogBox(_requestLogPreview, minHeight: 180));
        }

        root.Children.Add(CreateText("Request Error Log Files", 14, FontWeights.SemiBold, "PrimaryTextBrush", new Thickness(0, 12, 0, 8)));
        if (_requestErrorLogs.Count == 0)
        {
            root.Children.Add(CreateHintCard(
                "No request error logs",
                "The backend did not report request error log files. When request-log is disabled, /request-error-logs lists captured error files here."));
            return CreateCard(root, new Thickness(0, 0, 0, 18));
        }

        foreach (var file in _requestErrorLogs.Take(12))
        {
            var row = new UniformGrid { Columns = 3 };
            AddKeyValue(row, "File", file.Name);
            AddKeyValue(row, "Size", file.Size is null ? "-" : FormatBytes(file.Size.Value));
            AddKeyValue(row, "Modified", file.Modified is null ? "-" : FormatUnixTimestamp(file.Modified.Value));
            root.Children.Add(CreateCard(row, new Thickness(0, 0, 0, 10)));
        }

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateDiagnosticsExportCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "Export a redacted desktop diagnostic package containing the desktop report, local desktop log, desktop settings, and backend config snapshot.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        var details = new UniformGrid { Columns = 2 };
        AddKeyValue(details, "Output directory", _pathService.Directories.DiagnosticsDirectory);
        AddKeyValue(details, "Redaction", "Sensitive fields are redacted before packaging");
        AddKeyValue(details, "Backend status", _backendProcessManager.CurrentStatus.State.ToString());
        AddKeyValue(details, "Dependency status", GetDependencyStatus().Summary);
        root.Children.Add(details);

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(CreateActionButton("Export Diagnostic Package", ExportDiagnosticsAsync));
        actions.Children.Add(CreateActionButton("Open Diagnostics Folder", () =>
        {
            ProcessStartFolder(_pathService.Directories.DiagnosticsDirectory);
            return Task.CompletedTask;
        }));
        root.Children.Add(actions);

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateReadOnlyLogBox(string text, double minHeight)
    {
        var textBox = new WpfTextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = minHeight,
            FontFamily = new WpfFontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1)
        };
        textBox.SetResourceReference(WpfControl.ForegroundProperty, "PrimaryTextBrush");
        textBox.SetResourceReference(WpfControl.BackgroundProperty, "SurfaceBrush");
        textBox.SetResourceReference(WpfControl.BorderBrushProperty, "BorderBrush");
        return textBox;
    }

    private UIElement CreateLogSearchBox()
    {
        var textBox = new WpfTextBox
        {
            Text = _logSearchDraft,
            Width = 220,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 10, 10)
        };
        textBox.SetResourceReference(WpfControl.ForegroundProperty, "PrimaryTextBrush");
        textBox.SetResourceReference(WpfControl.BackgroundProperty, "SurfaceBrush");
        textBox.SetResourceReference(WpfControl.BorderBrushProperty, "BorderBrush");
        textBox.TextChanged += (_, _) => _logSearchDraft = textBox.Text;
        return textBox;
    }

    private UIElement CreateLogLevelFilter()
    {
        var comboBox = new WpfComboBox
        {
            ItemsSource = new[] { "all", "info+", "warn+", "error" },
            SelectedItem = _logLevelFilterDraft,
            Width = 120,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 10, 10)
        };
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is string selected)
            {
                _logLevelFilterDraft = selected;
            }
        };
        return comboBox;
    }

    private UIElement CreateHideManagementLogsToggle()
    {
        var checkbox = new WpfCheckBox
        {
            IsChecked = _hideManagementLogDraft,
            Content = "Hide management API noise",
            Margin = new Thickness(0, 8, 10, 10)
        };
        checkbox.SetResourceReference(WpfControl.ForegroundProperty, "PrimaryTextBrush");
        checkbox.Checked += (_, _) => _hideManagementLogDraft = true;
        checkbox.Unchecked += (_, _) => _hideManagementLogDraft = false;
        return checkbox;
    }

    private IEnumerable<string> FilterLogLines(IEnumerable<string> lines)
    {
        var search = _logSearchDraft.Trim();
        foreach (var line in lines)
        {
            if (_hideManagementLogDraft && IsManagementLogLine(line))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(search) &&
                !line.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!MatchesLogLevel(line, _logLevelFilterDraft))
            {
                continue;
            }

            yield return line;
        }
    }

    private void RefreshLogsSection()
    {
        if ((NavigationList.SelectedItem as ShellSection)?.Key == "logs")
        {
            UpdateSelectedSection();
        }
    }

    private static bool IsManagementLogLine(string line)
    {
        return line.Contains("/v0/management", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Management API", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesLogLevel(string line, string filter)
    {
        var level = ResolveLogLevel(line);
        return filter switch
        {
            "error" => level == LogSeverity.Error,
            "warn+" => level >= LogSeverity.Warning,
            "info+" => level >= LogSeverity.Information,
            _ => true
        };
    }

    private static LogSeverity ResolveLogLevel(string line)
    {
        if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("fatal", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("panic", StringComparison.OrdinalIgnoreCase))
        {
            return LogSeverity.Error;
        }

        if (line.Contains("warn", StringComparison.OrdinalIgnoreCase))
        {
            return LogSeverity.Warning;
        }

        if (line.Contains("info", StringComparison.OrdinalIgnoreCase))
        {
            return LogSeverity.Information;
        }

        if (line.Contains("debug", StringComparison.OrdinalIgnoreCase))
        {
            return LogSeverity.Debug;
        }

        return LogSeverity.Information;
    }

    private static string FormatUnixTimestamp(long timestamp)
    {
        if (timestamp <= 0)
        {
            return "-";
        }

        return DateTimeOffset.FromUnixTimeSeconds(timestamp)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_048_576 => $"{bytes / 1_048_576d:0.0} MB",
            >= 1024 => $"{bytes / 1024d:0.0} KB",
            _ => $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
        };
    }

    private static CodexStatusSnapshot BuildCodexDiagnosticsSnapshot()
    {
        return new CodexStatusSnapshot
        {
            IsInstalled = false,
            DefaultProfile = "official",
            AuthenticationState = "Not inspected from Logs page",
            EffectiveSource = "official"
        };
    }

    private enum LogSeverity
    {
        Debug = 0,
        Information = 1,
        Warning = 2,
        Error = 3
    }
}
