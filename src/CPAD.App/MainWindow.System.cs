using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using CPAD.Core.Exceptions;
using CPAD.Core.Models.Management;

using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfControl = System.Windows.Controls.Control;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace CPAD;

public partial class MainWindow
{
    private IReadOnlyList<ManagementModelDescriptor> _systemModels = [];
    private IReadOnlyList<ManagementAuthFileItem> _systemAuthFiles = [];
    private ManagementServerMetadata? _systemServerMetadata;
    private bool _systemLoading;
    private bool _systemProbeLoading;
    private int _systemApiKeyCount;
    private string? _systemError;
    private string? _systemStatusMessage;
    private string? _systemLatestVersion;
    private string? _systemLatestVersionError;
    private string? _systemModelsError;
    private string? _systemModelDiscoveryDetail;
    private DateTimeOffset? _systemLastLoadedAt;
    private bool? _systemHealthOk;
    private HttpStatusCode? _systemHealthStatusCode;
    private string? _systemHealthDetail;
    private string _systemProbeMethodDraft = "GET";
    private string _systemProbeUrlDraft = string.Empty;
    private string _systemProbeAuthIndexDraft = string.Empty;
    private string _systemProbeHeadersDraft = string.Empty;
    private string _systemProbeBodyDraft = string.Empty;
    private int? _systemProbeStatusCode;
    private string? _systemProbeSummary;
    private string _systemProbeRawBody = string.Empty;
    private string? _systemProbeBodyPreview;
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _systemProbeResponseHeaders =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    private UIElement BuildSystemContent()
    {
        if (_systemLoading && _systemLastLoadedAt is null)
        {
            return CreateStatePanel(
                "Loading system health and model inventory...",
                "Reading /latest-version, /v1/models, /api-call, and the managed backend /healthz endpoint.");
        }

        if (!string.IsNullOrWhiteSpace(_systemError) && _systemLastLoadedAt is null)
        {
            return CreateStatePanel("System and model data is unavailable.", _systemError);
        }

        if (_systemLastLoadedAt is null)
        {
            return CreateStatePanel(
                "No system data loaded yet.",
                "Open this route again or refresh after the managed backend becomes available.");
        }

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateSystemHero());

        if (!string.IsNullOrWhiteSpace(_systemStatusMessage))
        {
            root.Children.Add(CreateHintCard("System action", _systemStatusMessage));
        }

        if (!string.IsNullOrWhiteSpace(_systemError))
        {
            root.Children.Add(CreateHintCard("System issue", _systemError));
        }

        root.Children.Add(CreateSectionHeader("System Status"));
        root.Children.Add(CreateSystemStatusPanel());

        root.Children.Add(CreateSectionHeader("Health Check"));
        root.Children.Add(CreateSystemHealthCard());

        root.Children.Add(CreateSectionHeader("Model Inventory"));
        root.Children.Add(CreateSystemModelsCard());

        root.Children.Add(CreateSectionHeader("Connectivity Probe"));
        root.Children.Add(CreateSystemConnectivityCard());

        return root;
    }

    private async Task RefreshSystemAsync(bool force)
    {
        if (_systemLoading)
        {
            return;
        }

        if (!force && _systemLastLoadedAt is not null)
        {
            return;
        }

        _systemLoading = true;
        _systemError = null;
        _systemStatusMessage = "Refreshing backend health, version, models, and connectivity state...";
        RefreshSystemSection();

        try
        {
            var configTask = _managementConfigurationService.GetConfigAsync();
            var apiKeysTask = _authService.GetApiKeysAsync();
            var authFilesTask = _authService.GetAuthFilesAsync();
            var latestVersionTask = _systemService.GetLatestVersionAsync();
            var healthTask = ProbeBackendHealthAsync();

            var errors = new List<string>();
            ManagementApiResponse<ManagementConfigSnapshot>? configResponse = null;
            IReadOnlyList<string> apiKeys = [];

            try
            {
                configResponse = await configTask;
                _systemServerMetadata = configResponse.Metadata;
            }
            catch (Exception exception)
            {
                errors.Add($"Config metadata: {exception.Message}");
                _systemServerMetadata = null;
            }

            try
            {
                apiKeys = (await apiKeysTask).Value;
            }
            catch (Exception exception)
            {
                errors.Add($"API keys: {exception.Message}");
            }

            try
            {
                _systemAuthFiles = (await authFilesTask).Value
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception)
            {
                errors.Add($"Auth files: {exception.Message}");
                _systemAuthFiles = [];
            }

            try
            {
                var latestVersion = await latestVersionTask;
                _systemLatestVersion = latestVersion.Value.LatestVersion;
                _systemLatestVersionError = null;
                _systemServerMetadata ??= latestVersion.Metadata;
            }
            catch (Exception exception)
            {
                _systemLatestVersion = null;
                _systemLatestVersionError = exception.Message;
                errors.Add($"Latest version: {exception.Message}");
            }

            var health = await healthTask;
            _systemHealthOk = health.IsHealthy;
            _systemHealthStatusCode = health.StatusCode;
            _systemHealthDetail = health.Detail;

            var mergedApiKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (configResponse is not null)
            {
                foreach (var apiKey in configResponse.Value.ApiKeys)
                {
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        mergedApiKeys.Add(apiKey);
                    }
                }
            }

            foreach (var apiKey in apiKeys)
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    mergedApiKeys.Add(apiKey);
                }
            }

            _systemApiKeyCount = mergedApiKeys.Count;
            var primaryApiKey = mergedApiKeys.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(primaryApiKey))
            {
                try
                {
                    var modelResponse = await _systemService.GetAvailableModelsAsync(primaryApiKey);
                    _systemModels = modelResponse.Value
                        .OrderBy(model => ResolveSystemModelGroup(model), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    _systemModelsError = null;
                    _systemModelDiscoveryDetail = "Loaded through backend GET /v1/models using the first configured API key.";
                }
                catch (Exception exception)
                {
                    _systemModels = [];
                    _systemModelsError = exception.Message;
                    _systemModelDiscoveryDetail = "The backend rejected or could not complete the /v1/models request.";
                    errors.Add($"Model inventory: {exception.Message}");
                }
            }
            else
            {
                _systemModels = [];
                _systemModelsError = "No API key is configured, so /v1/models cannot be queried yet.";
                _systemModelDiscoveryDetail = "Add an API key on Accounts & Auth before loading model inventory.";
            }

            if (string.IsNullOrWhiteSpace(_systemProbeUrlDraft))
            {
                _systemProbeUrlDraft = _backendProcessManager.CurrentStatus.Runtime?.HealthUrl ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(_systemProbeHeadersDraft))
            {
                _systemProbeHeadersDraft = "Accept: application/json";
            }

            if (string.IsNullOrWhiteSpace(_systemProbeAuthIndexDraft))
            {
                _systemProbeAuthIndexDraft = _systemAuthFiles
                    .FirstOrDefault(item => !item.Disabled && !string.IsNullOrWhiteSpace(item.AuthIndex))
                    ?.AuthIndex ?? string.Empty;
            }

            _systemLastLoadedAt = DateTimeOffset.Now;
            _systemStatusMessage = $"System state refreshed at {_systemLastLoadedAt.Value.ToLocalTime():HH:mm:ss}.";
            _systemError = errors.Count == 0 ? null : string.Join(" | ", errors);
        }
        catch (Exception exception)
        {
            _systemError = exception.Message;
            _systemStatusMessage = "System refresh failed.";
        }
        finally
        {
            _systemLoading = false;
            RefreshSystemSection();
        }
    }

    private async Task RunSystemConnectivityProbeAsync()
    {
        if (_systemProbeLoading)
        {
            return;
        }

        var method = string.IsNullOrWhiteSpace(_systemProbeMethodDraft)
            ? "GET"
            : _systemProbeMethodDraft.Trim().ToUpperInvariant();
        var url = _systemProbeUrlDraft.Trim();

        var validationErrors = new List<string>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var targetUri))
        {
            validationErrors.Add("Probe URL must be an absolute URL.");
        }

        var headers = ParseProbeHeaders(_systemProbeHeadersDraft, validationErrors);
        if (validationErrors.Count > 0)
        {
            _systemStatusMessage = string.Join(" | ", validationErrors);
            RefreshSystemSection();
            return;
        }

        _systemProbeLoading = true;
        _systemError = null;
        _systemProbeSummary = null;
        _systemProbeStatusCode = null;
        _systemProbeRawBody = string.Empty;
        _systemProbeBodyPreview = null;
        _systemProbeResponseHeaders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        _systemStatusMessage = $"Executing {method} {url} through /v0/management/api-call...";
        RefreshSystemSection();

        try
        {
            var response = await _systemService.ExecuteApiCallAsync(new ManagementApiCallRequest
            {
                Method = method,
                Url = targetUri!.ToString(),
                AuthIndex = string.IsNullOrWhiteSpace(_systemProbeAuthIndexDraft) ? null : _systemProbeAuthIndexDraft,
                Headers = headers,
                Data = string.IsNullOrWhiteSpace(_systemProbeBodyDraft) ? null : _systemProbeBodyDraft
            });

            _systemProbeStatusCode = response.Value.StatusCode;
            _systemProbeResponseHeaders = response.Value.Headers;
            _systemProbeRawBody = response.Value.BodyText ?? string.Empty;
            _systemProbeBodyPreview = FormatProbeBody(_systemProbeRawBody);
            _systemProbeSummary = $"Probe completed with HTTP {response.Value.StatusCode} {BuildHttpStatusText(response.Value.StatusCode)}.";
            _systemStatusMessage = _systemProbeSummary;

            if (_backendProcessManager.CurrentStatus.Runtime is { } runtime &&
                string.Equals(targetUri.ToString(), runtime.HealthUrl, StringComparison.OrdinalIgnoreCase))
            {
                _systemHealthOk = response.Value.StatusCode == (int)HttpStatusCode.OK;
                _systemHealthStatusCode = (HttpStatusCode)response.Value.StatusCode;
                _systemHealthDetail = string.IsNullOrWhiteSpace(response.Value.BodyText)
                    ? "The backend health endpoint returned no body."
                    : TruncateSingleLine(response.Value.BodyText, 140);
            }
        }
        catch (ManagementApiException exception)
        {
            _systemError = exception.Message;
            _systemProbeSummary = "Connectivity probe failed before an upstream response was returned.";
            _systemStatusMessage = _systemProbeSummary;
        }
        catch (Exception exception)
        {
            _systemError = exception.Message;
            _systemProbeSummary = "Connectivity probe failed.";
            _systemStatusMessage = _systemProbeSummary;
        }
        finally
        {
            _systemProbeLoading = false;
            RefreshSystemSection();
        }
    }

    private Task ResetSystemProbeDraftsAsync()
    {
        _systemProbeMethodDraft = "GET";
        _systemProbeUrlDraft = _backendProcessManager.CurrentStatus.Runtime?.HealthUrl ?? string.Empty;
        _systemProbeHeadersDraft = "Accept: application/json";
        _systemProbeBodyDraft = string.Empty;
        _systemProbeAuthIndexDraft = _systemAuthFiles
            .FirstOrDefault(item => !item.Disabled && !string.IsNullOrWhiteSpace(item.AuthIndex))
            ?.AuthIndex ?? string.Empty;
        _systemProbeStatusCode = null;
        _systemProbeSummary = null;
        _systemProbeRawBody = string.Empty;
        _systemProbeBodyPreview = null;
        _systemProbeResponseHeaders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        _systemStatusMessage = "Connectivity probe defaults were restored.";
        RefreshSystemSection();
        return Task.CompletedTask;
    }

    private UIElement CreateSystemHero()
    {
        var runtime = _backendProcessManager.CurrentStatus.Runtime;
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            "Live system status, model inventory, and connectivity probes",
            22,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        panel.Children.Add(CreateText(
            $"Management API: {runtime?.ManagementApiBaseUrl ?? "Backend runtime unavailable"}",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 0)));
        panel.Children.Add(CreateText(
            "This page is backed by /latest-version, /v1/models, /api-call, and a direct /healthz probe so it can show real desktop runtime state instead of a placeholder shell.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 4, 0, 0)));
        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateSystemStatusPanel()
    {
        var modelGroupCount = BuildSystemModelGroups(_systemModels).Count;
        var aliasCount = _systemModels.Count(model => !string.IsNullOrWhiteSpace(model.Alias));

        return CreateMetricGrid(
            CreateMetricCard("Backend", _backendProcessManager.CurrentStatus.State.ToString(), _backendProcessManager.CurrentStatus.Message),
            CreateMetricCard("Health", BuildHealthStateLabel(), BuildHealthDetail()),
            CreateMetricCard("Current Version", _systemServerMetadata?.Version ?? "Unknown", $"Build date: {FormatBuildDate(_systemServerMetadata?.BuildDate)}"),
            CreateMetricCard("Latest Version", string.IsNullOrWhiteSpace(_systemLatestVersion) ? "Unavailable" : _systemLatestVersion, string.IsNullOrWhiteSpace(_systemLatestVersionError) ? "Read from /latest-version" : _systemLatestVersionError),
            CreateMetricCard("Models", _systemModels.Count.ToString(CultureInfo.InvariantCulture), $"{modelGroupCount.ToString(CultureInfo.InvariantCulture)} groups | {aliasCount.ToString(CultureInfo.InvariantCulture)} aliases"),
            CreateMetricCard("API Keys", _systemApiKeyCount.ToString(CultureInfo.InvariantCulture), _systemModelDiscoveryDetail ?? "No model discovery detail recorded yet."));
    }

    private UIElement CreateSystemHealthCard()
    {
        var runtime = _backendProcessManager.CurrentStatus.Runtime;
        if (runtime is null)
        {
            return CreateStatePanel(
                "Backend runtime is not available.",
                "Start the managed backend to expose the local /healthz endpoint and management API URLs.");
        }

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "The desktop performs a direct request to the backend health endpoint so the page can distinguish process state from an actual healthy HTTP listener.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        var grid = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 10)
        };
        AddKeyValue(grid, "Health URL", runtime.HealthUrl);
        AddKeyValue(grid, "HTTP status", _systemHealthStatusCode is null ? "No response" : $"{(int)_systemHealthStatusCode.Value} {BuildHttpStatusText((int)_systemHealthStatusCode.Value)}");
        AddKeyValue(grid, "Last refresh", _systemLastLoadedAt is null ? "-" : _systemLastLoadedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        AddKeyValue(grid, "Process ID", _backendProcessManager.CurrentStatus.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "Unknown");
        AddKeyValue(grid, "Base URL", runtime.BaseUrl);
        AddKeyValue(grid, "Management API", runtime.ManagementApiBaseUrl);
        AddKeyValue(grid, "Port", runtime.Port.ToString(CultureInfo.InvariantCulture));
        AddKeyValue(grid, "Port adjustment", runtime.PortWasAdjusted ? (runtime.PortMessage ?? "Adjusted to avoid a conflict.") : "Requested port was used directly.");
        AddKeyValue(grid, "Config path", runtime.ConfigPath);
        AddKeyValue(grid, "Data root", _pathService.Directories.RootDirectory);
        root.Children.Add(grid);

        if (!string.IsNullOrWhiteSpace(_systemHealthDetail))
        {
            root.Children.Add(CreateText(
                $"Health detail: {_systemHealthDetail}",
                12,
                FontWeights.Normal,
                "SecondaryTextBrush",
                new Thickness(0, 0, 0, 12)));
        }

        var actions = new WrapPanel();
        actions.Children.Add(CreateActionButton("Refresh System Status", () => RefreshSystemAsync(force: true)));
        actions.Children.Add(CreateActionButton(
            _backendProcessManager.CurrentStatus.State == CPAD.Core.Enums.BackendStateKind.Running ? "Restart Backend" : "Start Backend",
            async () =>
            {
                if (_backendProcessManager.CurrentStatus.State == CPAD.Core.Enums.BackendStateKind.Running)
                {
                    await _backendProcessManager.RestartAsync();
                }
                else
                {
                    await _backendProcessManager.StartAsync();
                }

                await RefreshSystemAsync(force: true);
            }));
        actions.Children.Add(CreateActionButton("Open Backend Folder", () =>
        {
            ProcessStartFolder(_pathService.Directories.BackendDirectory);
            return Task.CompletedTask;
        }));
        root.Children.Add(actions);

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateSystemModelsCard()
    {
        if (!string.IsNullOrWhiteSpace(_systemModelsError) && _systemModels.Count == 0)
        {
            return CreateStatePanel("Model inventory is unavailable.", _systemModelsError);
        }

        if (_systemModels.Count == 0)
        {
            return CreateStatePanel(
                "No models were returned by /v1/models.",
                "Configure an API key or verify the upstream provider route before retrying model discovery.");
        }

        var groups = BuildSystemModelGroups(_systemModels);
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "The model list is grouped by provider family or upstream owner to stay close to the upstream Management Center system page while remaining native to WPF.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        if (!string.IsNullOrWhiteSpace(_systemModelDiscoveryDetail))
        {
            root.Children.Add(CreateText(
                _systemModelDiscoveryDetail,
                12,
                FontWeights.Normal,
                "SecondaryTextBrush",
                new Thickness(0, 0, 0, 12)));
        }

        var grid = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 8)
        };

        foreach (var group in groups)
        {
            grid.Children.Add(CreateSystemModelGroupCard(group));
        }

        root.Children.Add(grid);
        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateSystemConnectivityCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "Connectivity probes call the audited /v0/management/api-call passthrough. Use an auth file plus Authorization: Bearer $TOKEN$ when a refreshed provider token should be injected automatically.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        var methodBox = new WpfComboBox
        {
            ItemsSource = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" },
            SelectedItem = _systemProbeMethodDraft,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8, 5, 8, 5)
        };
        methodBox.SelectionChanged += (_, _) =>
        {
            if (methodBox.SelectedItem is string selected)
            {
                _systemProbeMethodDraft = selected;
            }
        };

        var authBox = new WpfComboBox
        {
            ItemsSource = BuildProbeAuthOptions(),
            DisplayMemberPath = nameof(SystemProbeAuthOption.Label),
            SelectedValuePath = nameof(SystemProbeAuthOption.AuthIndex),
            SelectedValue = _systemProbeAuthIndexDraft,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8, 5, 8, 5)
        };
        authBox.SelectionChanged += (_, _) =>
        {
            _systemProbeAuthIndexDraft = authBox.SelectedValue as string ?? string.Empty;
        };

        var urlBox = CreateSystemEditorTextBox(_systemProbeUrlDraft, minHeight: 0, acceptsReturn: false, fontFamily: null);
        urlBox.TextChanged += (_, _) => _systemProbeUrlDraft = urlBox.Text;

        var headersBox = CreateSystemEditorTextBox(_systemProbeHeadersDraft, minHeight: 160, acceptsReturn: true, fontFamily: "Consolas");
        headersBox.TextChanged += (_, _) => _systemProbeHeadersDraft = headersBox.Text;

        var bodyBox = CreateSystemEditorTextBox(_systemProbeBodyDraft, minHeight: 160, acceptsReturn: true, fontFamily: "Consolas");
        bodyBox.TextChanged += (_, _) => _systemProbeBodyDraft = bodyBox.Text;

        root.Children.Add(CreateConfigFieldGrid(
            CreateConfigFieldShell("Probe Method", "Forwarded as method in /api-call", methodBox),
            CreateConfigFieldShell("Auth File Token", "Optional auth_index used for $TOKEN$ replacement", authBox)));
        root.Children.Add(CreateConfigFieldShell("Probe URL", "Absolute URL required by /api-call", urlBox));
        root.Children.Add(CreateConfigFieldGrid(
            CreateConfigFieldShell("Headers", "One header per line using Name: Value", headersBox),
            CreateConfigFieldShell("Body", "Raw request body for POST/PUT/PATCH probes", bodyBox)));

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(CreateActionButton(_systemProbeLoading ? "Running..." : "Run Connectivity Probe", RunSystemConnectivityProbeAsync));
        actions.Children.Add(CreateActionButton("Reset Probe", ResetSystemProbeDraftsAsync));
        actions.Children.Add(CreateActionButton("Use Health URL", () =>
        {
            _systemProbeMethodDraft = "GET";
            _systemProbeUrlDraft = _backendProcessManager.CurrentStatus.Runtime?.HealthUrl ?? _systemProbeUrlDraft;
            _systemProbeBodyDraft = string.Empty;
            _systemProbeAuthIndexDraft = string.Empty;
            RefreshSystemSection();
            return Task.CompletedTask;
        }));
        actions.Children.Add(CreateActionButton("Use /v1/models URL", () =>
        {
            var runtime = _backendProcessManager.CurrentStatus.Runtime;
            _systemProbeMethodDraft = "GET";
            _systemProbeUrlDraft = runtime is null
                ? _systemProbeUrlDraft
                : CombineAbsoluteUrl(runtime.BaseUrl, "v1/models");
            _systemProbeHeadersDraft = "Accept: application/json";
            _systemProbeBodyDraft = string.Empty;
            RefreshSystemSection();
            return Task.CompletedTask;
        }));
        root.Children.Add(actions);

        if (!string.IsNullOrWhiteSpace(_systemProbeSummary))
        {
            root.Children.Add(CreateHintCard("Probe result", _systemProbeSummary));
        }

        if (_systemProbeStatusCode is null && string.IsNullOrWhiteSpace(_systemProbeBodyPreview))
        {
            root.Children.Add(CreateStatePanel(
                "No connectivity probe has been run yet.",
                "Use the health URL shortcut for a local smoke check, or point the probe at a provider endpoint to inspect upstream reachability through /api-call."));
            return CreateCard(root, new Thickness(0, 0, 0, 18));
        }

        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Status Code", _systemProbeStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "Unknown", _systemProbeStatusCode is null ? "No upstream response" : BuildHttpStatusText(_systemProbeStatusCode.Value)),
            CreateMetricCard("Response Headers", _systemProbeResponseHeaders.Count.ToString(CultureInfo.InvariantCulture), "Count returned by /api-call"),
            CreateMetricCard("Response Bytes", _systemProbeRawBody.Length.ToString(CultureInfo.InvariantCulture), "Raw body size before formatting")));

        root.Children.Add(CreateText("Response Headers", 14, FontWeights.SemiBold, "PrimaryTextBrush", new Thickness(0, 0, 0, 8)));
        root.Children.Add(CreateReadOnlyLogBox(BuildProbeHeaderPreview(_systemProbeResponseHeaders), minHeight: 140));

        root.Children.Add(CreateText("Response Body", 14, FontWeights.SemiBold, "PrimaryTextBrush", new Thickness(0, 12, 0, 8)));
        root.Children.Add(CreateReadOnlyLogBox(_systemProbeBodyPreview ?? "(empty response body)", minHeight: 220));

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private WpfTextBox CreateSystemEditorTextBox(string text, double minHeight, bool acceptsReturn, string? fontFamily)
    {
        var textBox = new WpfTextBox
        {
            Text = text,
            AcceptsReturn = acceptsReturn,
            AcceptsTab = acceptsReturn,
            TextWrapping = acceptsReturn ? TextWrapping.NoWrap : TextWrapping.Wrap,
            VerticalScrollBarVisibility = acceptsReturn ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = minHeight,
            Padding = new Thickness(10, 7, 10, 7),
            BorderThickness = new Thickness(1)
        };
        textBox.SetResourceReference(WpfControl.ForegroundProperty, "PrimaryTextBrush");
        textBox.SetResourceReference(WpfControl.BackgroundProperty, "SurfaceBrush");
        textBox.SetResourceReference(WpfControl.BorderBrushProperty, "BorderBrush");

        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            textBox.FontFamily = new WpfFontFamily(fontFamily);
            textBox.FontSize = 12;
        }

        return textBox;
    }

    private Border CreateSystemModelGroupCard(SystemModelGroup group)
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(group.Label, 15, FontWeights.SemiBold, "PrimaryTextBrush"));
        root.Children.Add(CreateText(
            $"{group.Items.Count.ToString(CultureInfo.InvariantCulture)} model(s)",
            12,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 6, 0, 10)));

        var tags = new WrapPanel();
        foreach (var model in group.Items.Take(14))
        {
            tags.Children.Add(CreateSystemModelChip(model));
        }

        if (group.Items.Count > 14)
        {
            tags.Children.Add(CreateText(
                $"+{(group.Items.Count - 14).ToString(CultureInfo.InvariantCulture)} more",
                12,
                FontWeights.SemiBold,
                "SecondaryTextBrush",
                new Thickness(0, 6, 0, 0)));
        }

        root.Children.Add(tags);
        return CreateCard(root, new Thickness(0, 0, 12, 12));
    }

    private Border CreateSystemModelChip(ManagementModelDescriptor model)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(model.Id, 12, FontWeights.SemiBold, "PrimaryTextBrush"));

        var subtitle = string.Join(
            " | ",
            new[] { model.DisplayName, model.Alias, model.Type }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            panel.Children.Add(CreateText(
                subtitle,
                11,
                FontWeights.Normal,
                "SecondaryTextBrush",
                new Thickness(0, 4, 0, 0)));
        }

        var border = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = panel
        };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return border;
    }

    private IReadOnlyList<SystemModelGroup> BuildSystemModelGroups(IReadOnlyList<ManagementModelDescriptor> models)
    {
        return models
            .GroupBy(ResolveSystemModelGroup, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SystemModelGroup(
                group.Key,
                group.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderByDescending(group => group.Items.Count)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<SystemProbeAuthOption> BuildProbeAuthOptions()
    {
        return
        [
            new SystemProbeAuthOption(string.Empty, "None"),
            .. _systemAuthFiles
                .Where(item => !string.IsNullOrWhiteSpace(item.AuthIndex))
                .Select(item =>
                {
                    var label = item.Email ??
                        item.Account ??
                        item.Label ??
                        item.Name;
                    var provider = item.Provider ?? item.Type ?? "auth";
                    return new SystemProbeAuthOption(
                        item.AuthIndex ?? string.Empty,
                        $"{label} ({provider})");
                })
                .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private async Task<SystemHealthProbeResult> ProbeBackendHealthAsync()
    {
        var runtime = _backendProcessManager.CurrentStatus.Runtime;
        if (runtime is null)
        {
            return new SystemHealthProbeResult(null, null, "Backend runtime is not available.");
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var client = _httpClientFactory.CreateClient();

        try
        {
            using var response = await client.GetAsync(runtime.HealthUrl, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var detail = string.IsNullOrWhiteSpace(body)
                ? $"HTTP {(int)response.StatusCode} {BuildHttpStatusText((int)response.StatusCode)}"
                : TruncateSingleLine(body, 140);
            return new SystemHealthProbeResult(
                response.StatusCode == HttpStatusCode.OK,
                response.StatusCode,
                detail);
        }
        catch (Exception exception)
        {
            return new SystemHealthProbeResult(false, null, exception.Message);
        }
    }

    private void RefreshSystemSection()
    {
        if ((NavigationList.SelectedItem as ShellSection)?.Key == "system")
        {
            UpdateSelectedSection();
        }
    }

    private static IReadOnlyDictionary<string, string> ParseProbeHeaders(string text, ICollection<string> errors)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                errors.Add($"Invalid header line: {line}");
                continue;
            }

            var name = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (name.Length == 0)
            {
                errors.Add($"Invalid header name: {line}");
                continue;
            }

            headers[name] = value;
        }

        return headers;
    }

    private static string ResolveSystemModelGroup(ManagementModelDescriptor model)
    {
        if (!string.IsNullOrWhiteSpace(model.OwnedBy))
        {
            return model.OwnedBy!;
        }

        if (!string.IsNullOrWhiteSpace(model.Type) &&
            !string.Equals(model.Type, "model", StringComparison.OrdinalIgnoreCase))
        {
            return model.Type!;
        }

        var id = model.Id.Trim();
        if (id.Length == 0)
        {
            return "Other";
        }

        var separatorIndex = id.IndexOfAny(['-', '.', '/']);
        if (separatorIndex > 0)
        {
            return id[..separatorIndex];
        }

        return id;
    }

    private string BuildHealthStateLabel()
    {
        return _systemHealthOk switch
        {
            true => "Healthy",
            false => "Warning",
            _ => "Unknown"
        };
    }

    private string BuildHealthDetail()
    {
        if (_systemHealthStatusCode is { } statusCode)
        {
            var detail = $"{(int)statusCode} {BuildHttpStatusText((int)statusCode)}";
            if (!string.IsNullOrWhiteSpace(_systemHealthDetail))
            {
                detail = $"{detail} | {_systemHealthDetail}";
            }

            return detail;
        }

        return string.IsNullOrWhiteSpace(_systemHealthDetail)
            ? "No health response captured yet."
            : _systemHealthDetail;
    }

    private static string BuildHttpStatusText(int statusCode)
    {
        var text = Enum.IsDefined(typeof(HttpStatusCode), statusCode)
            ? Enum.GetName(typeof(HttpStatusCode), (HttpStatusCode)statusCode)
            : null;
        return string.IsNullOrWhiteSpace(text)
            ? "Unknown"
            : text.Replace('_', ' ');
    }

    private static string FormatBuildDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : value;
    }

    private static string CombineAbsoluteUrl(string baseUrl, string relativePath)
    {
        var normalizedBase = baseUrl.EndsWith("/", StringComparison.Ordinal)
            ? baseUrl
            : $"{baseUrl}/";
        return new Uri(new Uri(normalizedBase, UriKind.Absolute), relativePath.TrimStart('/')).ToString();
    }

    private static string FormatProbeBody(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty response body)";
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (JsonException)
        {
            return text.Length > 6000
                ? $"{text[..6000]}{Environment.NewLine}... truncated ..."
                : text;
        }
    }

    private static string BuildProbeHeaderPreview(IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
    {
        if (headers.Count == 0)
        {
            return "(no response headers)";
        }

        return string.Join(
            Environment.NewLine,
            headers
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key}: {string.Join(", ", pair.Value)}"));
    }

    private static string TruncateSingleLine(string value, int maxLength)
    {
        var normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return $"{normalized[..maxLength]}...";
    }

    private sealed record SystemModelGroup(string Label, IReadOnlyList<ManagementModelDescriptor> Items);

    private sealed record SystemProbeAuthOption(string AuthIndex, string Label);

    private sealed record SystemHealthProbeResult(bool? IsHealthy, HttpStatusCode? StatusCode, string Detail);
}
