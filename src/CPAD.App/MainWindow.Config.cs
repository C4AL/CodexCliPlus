using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

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
    private ManagementConfigSnapshot? _configSnapshot;
    private string? _configYaml;
    private bool _configLoading;
    private bool _configSaving;
    private string? _configError;
    private string? _configStatusMessage;
    private bool _configDebugDraft;
    private bool _configUsageStatisticsEnabledDraft;
    private bool _configRequestLogDraft;
    private bool _configLoggingToFileDraft;
    private bool _configWebSocketAuthDraft;
    private bool _configForceModelPrefixDraft;
    private bool _configSwitchProjectDraft;
    private bool _configSwitchPreviewModelDraft;
    private string _configProxyUrlDraft = string.Empty;
    private string _configRequestRetryDraft = "0";
    private string _configMaxRetryIntervalDraft = "0";
    private string _configLogsMaxTotalSizeDraft = "0";
    private string _configErrorLogsMaxFilesDraft = "0";
    private string _configRoutingStrategyDraft = "round-robin";
    private string _configYamlDraft = string.Empty;

    private UIElement BuildConfigurationContent()
    {
        if (_configLoading && _configSnapshot is null)
        {
            return CreateStatePanel(
                "Loading backend configuration...",
                "Reading /config and /config.yaml from the managed backend.");
        }

        if (!string.IsNullOrWhiteSpace(_configError) && _configSnapshot is null)
        {
            return CreateStatePanel("Configuration is unavailable.", _configError);
        }

        var snapshot = _configSnapshot;
        if (snapshot is null)
        {
            return CreateStatePanel(
                "No configuration loaded yet.",
                "Open this route again or reload after the managed backend is available.");
        }

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateConfigurationHero(snapshot));

        if (!string.IsNullOrWhiteSpace(_configStatusMessage))
        {
            root.Children.Add(CreateHintCard("Configuration action", _configStatusMessage));
        }

        if (!string.IsNullOrWhiteSpace(_configError))
        {
            root.Children.Add(CreateHintCard("Configuration issue", _configError));
        }

        root.Children.Add(CreateSectionHeader("Editable Settings"));
        root.Children.Add(CreateConfigurationEditorCard());

        root.Children.Add(CreateSectionHeader("Raw YAML Editor"));
        root.Children.Add(CreateConfigurationYamlCard());

        root.Children.Add(CreateSectionHeader("Validation & Echo"));
        root.Children.Add(CreateConfigurationValidationCard(snapshot));

        root.Children.Add(CreateSectionHeader("Live Snapshot"));
        root.Children.Add(CreateConfigurationSnapshotCard(snapshot));

        return root;
    }

    private async Task RefreshConfigurationAsync(bool force)
    {
        if (_configLoading)
        {
            return;
        }

        if (!force && _configSnapshot is not null)
        {
            return;
        }

        _configLoading = true;
        _configError = null;
        RefreshConfigurationSection();

        try
        {
            var configTask = _managementConfigurationService.GetConfigAsync();
            var yamlTask = _managementConfigurationService.GetConfigYamlAsync();
            await Task.WhenAll(configTask, yamlTask);

            _configSnapshot = configTask.Result.Value;
            _configYaml = yamlTask.Result.Value;
            _configYamlDraft = _configYaml;
            LoadConfigurationDrafts(_configSnapshot);
        }
        catch (Exception exception)
        {
            _configError = exception.Message;
        }
        finally
        {
            _configLoading = false;
            RefreshConfigurationSection();
        }
    }

    private async Task ReloadConfigurationAsync()
    {
        _configStatusMessage = null;
        await RefreshConfigurationAsync(force: true);
    }

    private async Task ApplyConfigurationAsync()
    {
        if (_configSaving)
        {
            return;
        }

        var errors = ValidateConfigurationDrafts(
            out var requestRetry,
            out var maxRetryInterval,
            out var logsMaxTotalSize,
            out var errorLogsMaxFiles,
            out var routingStrategy,
            out var proxyUrl);

        if (errors.Count > 0)
        {
            _configStatusMessage = "Fix validation errors before saving: " + string.Join(" | ", errors);
            RefreshConfigurationSection();
            return;
        }

        _configSaving = true;
        _configError = null;
        _configStatusMessage = "Saving structured configuration through Management API...";
        RefreshConfigurationSection();

        try
        {
            await _managementConfigurationService.UpdateBooleanSettingAsync("debug", _configDebugDraft);
            await _managementConfigurationService.UpdateBooleanSettingAsync("usage-statistics-enabled", _configUsageStatisticsEnabledDraft);
            await _managementConfigurationService.UpdateBooleanSettingAsync("request-log", _configRequestLogDraft);
            await _managementConfigurationService.UpdateBooleanSettingAsync("logging-to-file", _configLoggingToFileDraft);
            await _managementConfigurationService.UpdateBooleanSettingAsync("ws-auth", _configWebSocketAuthDraft);
            await _managementConfigurationService.UpdateBooleanSettingAsync("force-model-prefix", _configForceModelPrefixDraft);
            await _managementConfigurationService.UpdateBooleanSettingAsync("quota-exceeded/switch-project", _configSwitchProjectDraft);
            await _managementConfigurationService.UpdateBooleanSettingAsync("quota-exceeded/switch-preview-model", _configSwitchPreviewModelDraft);
            await _managementConfigurationService.UpdateIntegerSettingAsync("request-retry", requestRetry);
            await _managementConfigurationService.UpdateIntegerSettingAsync("max-retry-interval", maxRetryInterval);
            await _managementConfigurationService.UpdateIntegerSettingAsync("logs-max-total-size-mb", logsMaxTotalSize);
            await _managementConfigurationService.UpdateIntegerSettingAsync("error-logs-max-files", errorLogsMaxFiles);
            await _managementConfigurationService.UpdateStringSettingAsync("routing/strategy", routingStrategy);

            if (string.IsNullOrWhiteSpace(proxyUrl))
            {
                await _managementConfigurationService.DeleteSettingAsync("proxy-url");
            }
            else
            {
                await _managementConfigurationService.UpdateStringSettingAsync("proxy-url", proxyUrl);
            }

            await RefreshConfigurationAsync(force: true);
            _configStatusMessage = "Structured settings were saved and echoed back from /config.";
        }
        catch (Exception exception)
        {
            _configError = exception.Message;
            _configStatusMessage = "Structured save failed. The backend rejected or could not persist a field.";
        }
        finally
        {
            _configSaving = false;
            RefreshConfigurationSection();
        }
    }

    private async Task SaveConfigurationYamlAsync()
    {
        if (_configSaving)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_configYamlDraft))
        {
            _configStatusMessage = "YAML cannot be empty.";
            RefreshConfigurationSection();
            return;
        }

        _configSaving = true;
        _configError = null;
        _configStatusMessage = "Validating and saving config.yaml through the backend...";
        RefreshConfigurationSection();

        try
        {
            await _managementConfigurationService.PutConfigYamlAsync(_configYamlDraft);
            await RefreshConfigurationAsync(force: true);
            _configStatusMessage = "config.yaml passed backend validation, was saved, and was reloaded.";
        }
        catch (Exception exception)
        {
            _configError = exception.Message;
            _configStatusMessage = "YAML save failed. The backend validation response is shown above.";
        }
        finally
        {
            _configSaving = false;
            RefreshConfigurationSection();
        }
    }

    private void LoadConfigurationDrafts(ManagementConfigSnapshot snapshot)
    {
        _configDebugDraft = snapshot.Debug ?? false;
        _configUsageStatisticsEnabledDraft = snapshot.UsageStatisticsEnabled ?? false;
        _configRequestLogDraft = snapshot.RequestLog ?? false;
        _configLoggingToFileDraft = snapshot.LoggingToFile ?? false;
        _configWebSocketAuthDraft = snapshot.WebSocketAuth ?? false;
        _configForceModelPrefixDraft = snapshot.ForceModelPrefix ?? false;
        _configSwitchProjectDraft = snapshot.QuotaExceeded?.SwitchProject ?? false;
        _configSwitchPreviewModelDraft = snapshot.QuotaExceeded?.SwitchPreviewModel ?? false;
        _configProxyUrlDraft = snapshot.ProxyUrl ?? string.Empty;
        _configRequestRetryDraft = FormatNullableInt(snapshot.RequestRetry);
        _configMaxRetryIntervalDraft = FormatNullableInt(snapshot.MaxRetryInterval);
        _configLogsMaxTotalSizeDraft = FormatNullableInt(snapshot.LogsMaxTotalSizeMb);
        _configErrorLogsMaxFilesDraft = FormatNullableInt(snapshot.ErrorLogsMaxFiles);
        _configRoutingStrategyDraft = string.IsNullOrWhiteSpace(snapshot.RoutingStrategy)
            ? "round-robin"
            : snapshot.RoutingStrategy;
    }

    private UIElement CreateConfigurationHero(ManagementConfigSnapshot snapshot)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            "Native configuration editor",
            22,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        panel.Children.Add(CreateText(
            "Structured controls use the audited Management API endpoints. The raw YAML editor uses /config.yaml so comments and backend validation remain authoritative.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 14)));
        panel.Children.Add(CreateMetricGrid(
            CreateMetricCard("Routing", snapshot.RoutingStrategy ?? "round-robin", "Persisted by /routing/strategy"),
            CreateMetricCard("Usage Stats", FormatBoolean(snapshot.UsageStatisticsEnabled), "Persisted by /usage-statistics-enabled"),
            CreateMetricCard("Request Log", FormatBoolean(snapshot.RequestLog), "Persisted by /request-log"),
            CreateMetricCard("Retry Count", FormatNullableInt(snapshot.RequestRetry), "Persisted by /request-retry"),
            CreateMetricCard("YAML Lines", CountLines(_configYamlDraft).ToString(CultureInfo.InvariantCulture), "Loaded from /config.yaml"),
            CreateMetricCard("Backend", _backendProcessManager.CurrentStatus.State.ToString(), _backendProcessManager.CurrentStatus.Message)));
        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateConfigurationEditorCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "These controls write directly to the same endpoints used by the upstream Management Center and CPA-UV config tab.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 14)));

        root.Children.Add(CreateText("Runtime & Routing", 14, FontWeights.SemiBold, "PrimaryTextBrush", new Thickness(0, 0, 0, 10)));
        root.Children.Add(CreateConfigFieldGrid(
            CreateConfigToggleField("Debug", "PUT /debug", _configDebugDraft, value => _configDebugDraft = value),
            CreateConfigTextField("Proxy URL", "PUT/DELETE /proxy-url", _configProxyUrlDraft, value => _configProxyUrlDraft = value),
            CreateConfigTextField("Request Retry", "PUT /request-retry", _configRequestRetryDraft, value => _configRequestRetryDraft = value),
            CreateConfigTextField("Max Retry Interval", "PUT /max-retry-interval", _configMaxRetryIntervalDraft, value => _configMaxRetryIntervalDraft = value),
            CreateRoutingStrategyField(),
            CreateConfigToggleField("Force Model Prefix", "PUT /force-model-prefix", _configForceModelPrefixDraft, value => _configForceModelPrefixDraft = value)));

        root.Children.Add(CreateText("Logging & Statistics", 14, FontWeights.SemiBold, "PrimaryTextBrush", new Thickness(0, 6, 0, 10)));
        root.Children.Add(CreateConfigFieldGrid(
            CreateConfigToggleField("Usage Statistics", "PUT /usage-statistics-enabled", _configUsageStatisticsEnabledDraft, value => _configUsageStatisticsEnabledDraft = value),
            CreateConfigToggleField("Request Log", "PUT /request-log", _configRequestLogDraft, value => _configRequestLogDraft = value),
            CreateConfigToggleField("Logging To File", "PUT /logging-to-file", _configLoggingToFileDraft, value => _configLoggingToFileDraft = value),
            CreateConfigTextField("Logs Max Total Size MB", "PUT /logs-max-total-size-mb", _configLogsMaxTotalSizeDraft, value => _configLogsMaxTotalSizeDraft = value),
            CreateConfigTextField("Error Logs Max Files", "PUT /error-logs-max-files", _configErrorLogsMaxFilesDraft, value => _configErrorLogsMaxFilesDraft = value),
            CreateConfigToggleField("WebSocket Auth", "PUT /ws-auth", _configWebSocketAuthDraft, value => _configWebSocketAuthDraft = value)));

        root.Children.Add(CreateText("Quota Exceeded Behavior", 14, FontWeights.SemiBold, "PrimaryTextBrush", new Thickness(0, 6, 0, 10)));
        root.Children.Add(CreateConfigFieldGrid(
            CreateConfigToggleField("Switch Project", "PUT /quota-exceeded/switch-project", _configSwitchProjectDraft, value => _configSwitchProjectDraft = value),
            CreateConfigToggleField("Switch Preview Model", "PUT /quota-exceeded/switch-preview-model", _configSwitchPreviewModelDraft, value => _configSwitchPreviewModelDraft = value)));

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(CreateActionButton("Apply Structured Changes", ApplyConfigurationAsync));
        actions.Children.Add(CreateActionButton("Reload From Backend", ReloadConfigurationAsync));
        root.Children.Add(actions);

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateConfigurationYamlCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "Edit the raw backend configuration when a setting is not exposed in the structured editor. Save sends the full YAML to /config.yaml; the backend validates it before writing.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        var editor = new WpfTextBox
        {
            Text = _configYamlDraft,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 280,
            FontFamily = new WpfFontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1)
        };
        editor.SetResourceReference(WpfControl.ForegroundProperty, "PrimaryTextBrush");
        editor.SetResourceReference(WpfControl.BackgroundProperty, "SurfaceBrush");
        editor.SetResourceReference(WpfControl.BorderBrushProperty, "BorderBrush");
        editor.TextChanged += (_, _) => _configYamlDraft = editor.Text;
        root.Children.Add(editor);

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(CreateActionButton("Validate & Save YAML", SaveConfigurationYamlAsync));
        actions.Children.Add(CreateActionButton("Reset YAML Draft", () =>
        {
            _configYamlDraft = _configYaml ?? string.Empty;
            RefreshConfigurationSection();
            return Task.CompletedTask;
        }));
        root.Children.Add(actions);

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateConfigurationValidationCard(ManagementConfigSnapshot snapshot)
    {
        var validationMessages = ValidateConfigurationDrafts(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _);

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            validationMessages.Count == 0 ? "Local validation passed" : "Local validation needs attention",
            15,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        root.Children.Add(CreateText(
            validationMessages.Count == 0
                ? "Integer fields, proxy URL shape, and routing strategy are valid. The backend still performs final YAML validation on save."
                : string.Join(Environment.NewLine, validationMessages),
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 12)));

        var echo = new UniformGrid { Columns = 2 };
        AddKeyValue(echo, "Backend raw JSON bytes", snapshot.RawJson.Length.ToString(CultureInfo.InvariantCulture));
        AddKeyValue(echo, "YAML editor bytes", _configYamlDraft.Length.ToString(CultureInfo.InvariantCulture));
        AddKeyValue(echo, "Structured save", "PUT/PATCH-compatible field endpoints");
        AddKeyValue(echo, "YAML save", "PUT /config.yaml with backend validation");
        root.Children.Add(echo);

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateConfigurationSnapshotCard(ManagementConfigSnapshot snapshot)
    {
        var grid = new UniformGrid { Columns = 2 };
        AddKeyValue(grid, "Debug", FormatBoolean(snapshot.Debug));
        AddKeyValue(grid, "Proxy URL", string.IsNullOrWhiteSpace(snapshot.ProxyUrl) ? "Not set" : snapshot.ProxyUrl);
        AddKeyValue(grid, "Request retry", FormatNullableInt(snapshot.RequestRetry));
        AddKeyValue(grid, "Max retry interval", FormatNullableInt(snapshot.MaxRetryInterval));
        AddKeyValue(grid, "Routing strategy", snapshot.RoutingStrategy ?? "round-robin");
        AddKeyValue(grid, "Force model prefix", FormatBoolean(snapshot.ForceModelPrefix));
        AddKeyValue(grid, "Usage statistics", FormatBoolean(snapshot.UsageStatisticsEnabled));
        AddKeyValue(grid, "Request log", FormatBoolean(snapshot.RequestLog));
        AddKeyValue(grid, "Logging to file", FormatBoolean(snapshot.LoggingToFile));
        AddKeyValue(grid, "Log cap", snapshot.LogsMaxTotalSizeMb is null ? "-" : $"{snapshot.LogsMaxTotalSizeMb} MB");
        AddKeyValue(grid, "Error log files", FormatNullableInt(snapshot.ErrorLogsMaxFiles));
        AddKeyValue(grid, "WebSocket auth", FormatBoolean(snapshot.WebSocketAuth));
        AddKeyValue(grid, "Switch project on quota", FormatBoolean(snapshot.QuotaExceeded?.SwitchProject));
        AddKeyValue(grid, "Switch preview model", FormatBoolean(snapshot.QuotaExceeded?.SwitchPreviewModel));
        return CreateCard(grid, new Thickness(0, 0, 0, 18));
    }

    private UniformGrid CreateConfigFieldGrid(params UIElement[] fields)
    {
        var grid = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 12)
        };

        foreach (var field in fields)
        {
            grid.Children.Add(field);
        }

        return grid;
    }

    private UIElement CreateConfigToggleField(string label, string detail, bool value, Action<bool> setter)
    {
        var checkbox = new WpfCheckBox
        {
            IsChecked = value,
            Content = value ? "Enabled" : "Disabled",
            Margin = new Thickness(0, 8, 0, 0)
        };
        checkbox.SetResourceReference(WpfControl.ForegroundProperty, "PrimaryTextBrush");
        checkbox.Checked += (_, _) =>
        {
            setter(true);
            checkbox.Content = "Enabled";
        };
        checkbox.Unchecked += (_, _) =>
        {
            setter(false);
            checkbox.Content = "Disabled";
        };

        return CreateConfigFieldShell(label, detail, checkbox);
    }

    private UIElement CreateConfigTextField(string label, string detail, string value, Action<string> setter)
    {
        var textBox = new WpfTextBox
        {
            Text = value,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(10, 7, 10, 7),
            BorderThickness = new Thickness(1)
        };
        textBox.SetResourceReference(WpfControl.ForegroundProperty, "PrimaryTextBrush");
        textBox.SetResourceReference(WpfControl.BackgroundProperty, "SurfaceBrush");
        textBox.SetResourceReference(WpfControl.BorderBrushProperty, "BorderBrush");
        textBox.TextChanged += (_, _) => setter(textBox.Text);
        return CreateConfigFieldShell(label, detail, textBox);
    }

    private UIElement CreateRoutingStrategyField()
    {
        var current = NormalizeRoutingStrategy(_configRoutingStrategyDraft);
        var comboBox = new WpfComboBox
        {
            ItemsSource = new[] { "round-robin", "fill-first" },
            SelectedItem = string.IsNullOrWhiteSpace(current) ? "round-robin" : current,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8, 5, 8, 5)
        };
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is string selected)
            {
                _configRoutingStrategyDraft = selected;
            }
        };
        return CreateConfigFieldShell("Routing Strategy", "PUT /routing/strategy", comboBox);
    }

    private UIElement CreateConfigFieldShell(string label, string detail, UIElement input)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(label, 13, FontWeights.SemiBold, "PrimaryTextBrush"));
        panel.Children.Add(CreateText(detail, 12, FontWeights.Normal, "SecondaryTextBrush", new Thickness(0, 4, 0, 0)));
        panel.Children.Add(input);
        return CreateCard(panel, new Thickness(0, 0, 12, 12));
    }

    private IReadOnlyList<string> ValidateConfigurationDrafts(
        out int requestRetry,
        out int maxRetryInterval,
        out int logsMaxTotalSize,
        out int errorLogsMaxFiles,
        out string routingStrategy,
        out string proxyUrl)
    {
        var errors = new List<string>();
        requestRetry = ParseNonNegativeInt(_configRequestRetryDraft, "Request Retry", errors);
        maxRetryInterval = ParseNonNegativeInt(_configMaxRetryIntervalDraft, "Max Retry Interval", errors);
        logsMaxTotalSize = ParseNonNegativeInt(_configLogsMaxTotalSizeDraft, "Logs Max Total Size MB", errors);
        errorLogsMaxFiles = ParseNonNegativeInt(_configErrorLogsMaxFilesDraft, "Error Logs Max Files", errors);
        routingStrategy = NormalizeRoutingStrategy(_configRoutingStrategyDraft);
        proxyUrl = _configProxyUrlDraft.Trim();

        if (string.IsNullOrWhiteSpace(routingStrategy))
        {
            errors.Add("Routing Strategy must be round-robin or fill-first.");
            routingStrategy = "round-robin";
        }

        if (!string.IsNullOrWhiteSpace(proxyUrl) && !Uri.TryCreate(proxyUrl, UriKind.Absolute, out _))
        {
            errors.Add("Proxy URL must be an absolute URI or empty.");
        }

        return errors;
    }

    private void RefreshConfigurationSection()
    {
        if ((NavigationList.SelectedItem as ShellSection)?.Key == "config")
        {
            UpdateSelectedSection();
        }
    }

    private static int ParseNonNegativeInt(string value, string label, ICollection<string> errors)
    {
        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            errors.Add($"{label} must be an integer.");
            return 0;
        }

        if (parsed < 0)
        {
            errors.Add($"{label} must be greater than or equal to 0.");
            return 0;
        }

        return parsed;
    }

    private static string NormalizeRoutingStrategy(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "" or "roundrobin" or "round-robin" or "rr" => "round-robin",
            "fillfirst" or "fill-first" or "ff" => "fill-first",
            _ => string.Empty
        };
    }

    private static string FormatNullableInt(int? value)
    {
        return (value ?? 0).ToString(CultureInfo.InvariantCulture);
    }

    private static int CountLines(string value)
    {
        if (value.Length == 0)
        {
            return 0;
        }

        return value.Count(character => character == '\n') + 1;
    }
}
