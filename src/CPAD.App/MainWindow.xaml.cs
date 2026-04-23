using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using CPAD.Core.Abstractions.Management;
using CPAD.Core.Abstractions.Build;
using CPAD.Core.Abstractions.Configuration;
using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Constants;
using CPAD.Core.Enums;
using CPAD.Core.Models;
using CPAD.Core.Models.Management;
using CPAD.Infrastructure.Backend;
using CPAD.Infrastructure.Diagnostics;

using Microsoft.Win32;

using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace CPAD;

public partial class MainWindow : Window
{
    private const string BackendExecutableFileName = "cli-proxy-api.exe";

    private readonly BackendAssetService _backendAssetService;
    private readonly BackendProcessManager _backendProcessManager;
    private readonly IManagementAuthService _authService;
    private readonly IManagementConfigurationService _managementConfigurationService;
    private readonly IManagementLogsService _logsService;
    private readonly IManagementOverviewService _overviewService;
    private readonly IManagementSystemService _systemService;
    private readonly IManagementUsageService _usageService;
    private readonly IAppConfigurationService _configurationService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPathService _pathService;
    private readonly IBuildInfo _buildInfo;
    private readonly DiagnosticsService _diagnosticsService;
    private readonly List<ShellSection> _sections = CreateSections();

    private AppSettings _settings = new();
    private ManagementOverviewSnapshot? _overviewSnapshot;
    private bool _overviewLoading;
    private string? _overviewError;
    private Forms.NotifyIcon? _notifyIcon;
    private bool _allowClose;

    public MainWindow(
        BackendAssetService backendAssetService,
        BackendProcessManager backendProcessManager,
        IManagementAuthService authService,
        IManagementConfigurationService managementConfigurationService,
        IManagementLogsService logsService,
        IManagementOverviewService overviewService,
        IManagementSystemService systemService,
        IManagementUsageService usageService,
        IAppConfigurationService configurationService,
        IHttpClientFactory httpClientFactory,
        IPathService pathService,
        IBuildInfo buildInfo,
        DiagnosticsService diagnosticsService)
    {
        _backendAssetService = backendAssetService;
        _backendProcessManager = backendProcessManager;
        _authService = authService;
        _managementConfigurationService = managementConfigurationService;
        _logsService = logsService;
        _overviewService = overviewService;
        _systemService = systemService;
        _usageService = usageService;
        _configurationService = configurationService;
        _httpClientFactory = httpClientFactory;
        _pathService = pathService;
        _buildInfo = buildInfo;
        _diagnosticsService = diagnosticsService;

        InitializeComponent();

        NavigationList.ItemsSource = _sections;
        NavigationList.SelectedIndex = 0;
        _backendProcessManager.StatusChanged += BackendProcessManager_StatusChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _configurationService.LoadAsync();
        _managementKeyDraft = _settings.ManagementKey;
        await ApplyThemeAsync(_settings.ThemeMode, persist: false);
        InitializeTrayIcon();
        UpdateSelectedSection();
        UpdateBackendStatus(_backendProcessManager.CurrentStatus);
        UpdateFooter();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _backendProcessManager.StatusChanged -= BackendProcessManager_StatusChanged;
        StopAccountsPagePolling();
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose || !_settings.EnableTrayIcon || !_settings.MinimizeToTrayOnClose)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private async void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.ThemeMode = _settings.ThemeMode switch
        {
            AppThemeMode.System => AppThemeMode.Light,
            AppThemeMode.Light => AppThemeMode.Dark,
            _ => AppThemeMode.System
        };

        await ApplyThemeAsync(_settings.ThemeMode, persist: true);
    }

    private void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        ShowUpdatesPlaceholder();
    }

    private async void BackendActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProcessManager.CurrentStatus.State == BackendStateKind.Running)
        {
            await _backendProcessManager.RestartAsync();
            return;
        }

        await _backendProcessManager.StartAsync();
    }

    private void NavigationList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateSelectedSection();
    }

    private async void PrimaryPageActionButton_Click(object sender, RoutedEventArgs e)
    {
        var section = NavigationList.SelectedItem as ShellSection;
        if (section is null)
        {
            return;
        }

        switch (section.Key)
        {
            case "overview":
                await RefreshOverviewAsync(force: true);
                break;
            case "config":
                await ApplyConfigurationAsync();
                break;
            case "logs":
                await RefreshLogsAsync(force: true);
                break;
            case "system":
                await RefreshSystemAsync(force: true);
                break;
            case "updates":
                ShowUpdatesPlaceholder();
                break;
            default:
                RestoreFromTray();
                break;
        }
    }

    private async void SecondaryPageActionButton_Click(object sender, RoutedEventArgs e)
    {
        var section = NavigationList.SelectedItem as ShellSection;
        if (section?.Key == "about")
        {
            ProcessStartFolder(_pathService.Directories.RootDirectory);
            return;
        }

        if (section?.Key == "config")
        {
            await ReloadConfigurationAsync();
            return;
        }

        if (section?.Key == "logs")
        {
            await ExportDiagnosticsAsync();
            return;
        }

        if (section?.Key == "system")
        {
            if (_backendProcessManager.CurrentStatus.State == BackendStateKind.Running)
            {
                await _backendProcessManager.RestartAsync();
            }
            else
            {
                await _backendProcessManager.StartAsync();
            }

            await RefreshSystemAsync(force: true);
            return;
        }

        ProcessStartFolder(_pathService.Directories.ConfigDirectory);
    }

    private async void BackendProcessManager_StatusChanged(object? sender, BackendStatusSnapshot snapshot)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            UpdateBackendStatus(snapshot);
            UpdateFooter();
            UpdateSelectedSection();
        });
    }

    private async Task ApplyThemeAsync(AppThemeMode themeMode, bool persist)
    {
        var effectiveTheme = ResolveEffectiveTheme(themeMode);
        var isDark = effectiveTheme == AppThemeMode.Dark;

        SetBrush("ApplicationBackgroundBrush", isDark ? "#111827" : "#F4F6FA");
        SetBrush("SurfaceBrush", isDark ? "#16202B" : "#FFFFFF");
        SetBrush("SurfaceAltBrush", isDark ? "#1E2A38" : "#EEF2F7");
        SetBrush("AccentBrush", isDark ? "#34D3C2" : "#0F766E");
        SetBrush("PrimaryTextBrush", isDark ? "#E5EEF7" : "#17202B");
        SetBrush("SecondaryTextBrush", isDark ? "#A8B4C5" : "#526070");
        SetBrush("BorderBrush", isDark ? "#314153" : "#D7DCE5");

        ThemeButton.Content = $"Theme: {GetThemeLabel(themeMode)}";
        ThemeStatusText.Text = $"Theme: {GetThemeLabel(themeMode)} ({GetThemeLabel(effectiveTheme)})";

        if (persist)
        {
            await _configurationService.SaveAsync(_settings);
        }
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon?.Dispose();
        _notifyIcon = null;

        if (!_settings.EnableTrayIcon)
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Main Interface", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Restart Backend", null, async (_, _) => await Dispatcher.InvokeAsync(async () =>
        {
            if (_backendProcessManager.CurrentStatus.State == BackendStateKind.Running)
            {
                await _backendProcessManager.RestartAsync();
            }
            else
            {
                await _backendProcessManager.StartAsync();
            }
        }));
        menu.Items.Add("Check Updates", null, (_, _) => Dispatcher.Invoke(ShowUpdatesPlaceholder));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit and Stop Backend", null, async (_, _) => await Dispatcher.InvokeAsync(ExitAndStopBackendAsync));

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = AppConstants.TrayToolTip,
            Icon = CreateTrayIcon(),
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void UpdateSelectedSection()
    {
        var section = NavigationList.SelectedItem as ShellSection ?? _sections[0];

        PageTitleText.Text = section.Title;
        PageSubtitleText.Text = section.Subtitle;
        PageStatusText.Text = BuildPageStatus(section);

        switch (section.Key)
        {
            case "overview":
                PrimaryPageActionButton.Visibility = Visibility.Collapsed;
                SecondaryPageActionButton.Visibility = Visibility.Collapsed;
                PrimaryPageActionButton.Content = _overviewLoading ? "Loading..." : "Refresh Overview";
                PrimaryPageActionButton.IsEnabled = !_overviewLoading;
                SecondaryPageActionButton.Content = "Open Config Folder";
                PageContentHost.Content = BuildOverviewContent();
                if (_overviewSnapshot is null && !_overviewLoading)
                {
                    _ = RefreshOverviewAsync(force: false);
                }

                break;
            case "accounts":
                PrimaryPageActionButton.Visibility = Visibility.Collapsed;
                SecondaryPageActionButton.Visibility = Visibility.Collapsed;
                PrimaryPageActionButton.IsEnabled = true;
                PageContentHost.Content = BuildAccountsContent();
                if (_accountsAuthFiles is null && !_accountsLoading)
                {
                    _ = RefreshAccountsAsync(force: false);
                }

                break;
            case "quota":
                PrimaryPageActionButton.Visibility = Visibility.Collapsed;
                SecondaryPageActionButton.Visibility = Visibility.Collapsed;
                PrimaryPageActionButton.IsEnabled = true;
                PageContentHost.Content = BuildQuotaContent();
                if (_quotaSnapshot is null && !_quotaLoading)
                {
                    _ = RefreshQuotaAsync(force: false);
                }

                break;
            case "config":
                PrimaryPageActionButton.Visibility = Visibility.Visible;
                SecondaryPageActionButton.Visibility = Visibility.Visible;
                PrimaryPageActionButton.Content = _configSaving ? "Saving..." : "Apply Structured Changes";
                PrimaryPageActionButton.IsEnabled = !_configLoading && !_configSaving;
                SecondaryPageActionButton.Content = _configLoading ? "Reloading..." : "Reload Config";
                SecondaryPageActionButton.IsEnabled = !_configLoading && !_configSaving;
                PageContentHost.Content = BuildConfigurationContent();
                if (_configSnapshot is null && !_configLoading)
                {
                    _ = RefreshConfigurationAsync(force: false);
                }

                break;
            case "logs":
                PrimaryPageActionButton.Visibility = Visibility.Visible;
                SecondaryPageActionButton.Visibility = Visibility.Visible;
                PrimaryPageActionButton.Content = _logsLoading ? "Refreshing..." : "Refresh Logs";
                PrimaryPageActionButton.IsEnabled = !_logsLoading && !_logsClearing;
                SecondaryPageActionButton.Content = "Export Diagnostics";
                SecondaryPageActionButton.IsEnabled = !_logsLoading && !_logsClearing;
                PageContentHost.Content = BuildLogsContent();
                if (_logsSnapshot is null && !_logsLoading)
                {
                    _ = RefreshLogsAsync(force: false);
                }

                break;
            case "system":
                PrimaryPageActionButton.Visibility = Visibility.Visible;
                SecondaryPageActionButton.Visibility = Visibility.Visible;
                PrimaryPageActionButton.Content = _systemLoading ? "Refreshing..." : "Refresh System";
                PrimaryPageActionButton.IsEnabled = !_systemLoading && !_systemProbeLoading;
                SecondaryPageActionButton.Content = _backendProcessManager.CurrentStatus.State == BackendStateKind.Running
                    ? "Restart Backend"
                    : "Start Backend";
                SecondaryPageActionButton.IsEnabled = !_systemLoading && !_systemProbeLoading;
                PageContentHost.Content = BuildSystemContent();
                if (_systemLastLoadedAt is null && !_systemLoading)
                {
                    _ = RefreshSystemAsync(force: false);
                }

                break;
            case "updates":
                PrimaryPageActionButton.Visibility = Visibility.Visible;
                SecondaryPageActionButton.Visibility = Visibility.Visible;
                PrimaryPageActionButton.IsEnabled = true;
                PrimaryPageActionButton.Content = "Check Updates";
                SecondaryPageActionButton.Content = "Open Data Folder";
                PageContentHost.Content = BuildPlaceholderContent(section);
                break;
            case "about":
                PrimaryPageActionButton.Visibility = Visibility.Collapsed;
                SecondaryPageActionButton.Visibility = Visibility.Visible;
                PrimaryPageActionButton.IsEnabled = true;
                SecondaryPageActionButton.Content = "Open Data Folder";
                PageContentHost.Content = BuildPlaceholderContent(section);
                break;
            default:
                PrimaryPageActionButton.Visibility = Visibility.Collapsed;
                SecondaryPageActionButton.Visibility = Visibility.Visible;
                PrimaryPageActionButton.IsEnabled = true;
                SecondaryPageActionButton.Content = "Open Config Folder";
                PageContentHost.Content = BuildPlaceholderContent(section);
                break;
        }
    }

    private void UpdateBackendStatus(BackendStatusSnapshot status)
    {
        BackendStatusText.Text = $"Backend: {status.State} | {status.Message}";
        BackendActionButton.Content = status.State == BackendStateKind.Running ? "Restart Backend" : "Start Backend";
    }

    private void UpdateFooter()
    {
        SourceStatusText.Text = $"Source: {_settings.PreferredCodexSource}";
        VersionStatusText.Text = $"Version: {_buildInfo.ApplicationVersion}";
    }

    private async Task RefreshOverviewAsync(bool force)
    {
        if (_overviewLoading)
        {
            return;
        }

        if (!force && _overviewSnapshot is not null)
        {
            return;
        }

        _overviewLoading = true;
        _overviewError = null;
        if ((NavigationList.SelectedItem as ShellSection)?.Key == "overview")
        {
            UpdateSelectedSection();
        }

        try
        {
            var response = await _overviewService.GetOverviewAsync();
            _overviewSnapshot = response.Value;
        }
        catch (Exception exception)
        {
            _overviewError = exception.Message;
        }
        finally
        {
            _overviewLoading = false;
            if ((NavigationList.SelectedItem as ShellSection)?.Key == "overview")
            {
                UpdateSelectedSection();
            }
        }
    }

    private UIElement BuildOverviewContent()
    {
        if (_overviewLoading && _overviewSnapshot is null)
        {
            return CreateStatePanel(
                "Loading live overview...",
                "Starting or contacting the managed backend and reading live management data.");
        }

        if (!string.IsNullOrWhiteSpace(_overviewError) && _overviewSnapshot is null)
        {
            return CreateStatePanel("Overview data is unavailable.", _overviewError);
        }

        var snapshot = _overviewSnapshot;
        if (snapshot is null)
        {
            return CreateStatePanel(
                "No overview data loaded yet.",
                "Use Refresh Overview to start the backend and load live Management API data.");
        }

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };

        root.Children.Add(CreateOverviewHero(snapshot));
        root.Children.Add(CreateSectionHeader("Live Status"));
        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Backend", _backendProcessManager.CurrentStatus.State.ToString(), _backendProcessManager.CurrentStatus.Message),
            CreateMetricCard("Management Keys", snapshot.ApiKeyCount.ToString(CultureInfo.InvariantCulture), "Configured API keys from /api-keys"),
            CreateMetricCard("Auth Files", snapshot.AuthFileCount.ToString(CultureInfo.InvariantCulture), "OAuth/device credentials from /auth-files"),
            CreateMetricCard("Providers", CountProviders(snapshot).ToString(CultureInfo.InvariantCulture), "Gemini, Codex, Claude, Vertex, OpenAI entries"),
            CreateMetricCard("Requests", FormatCompact(snapshot.Usage.TotalRequests), $"{FormatCompact(snapshot.Usage.SuccessCount)} success / {FormatCompact(snapshot.Usage.FailureCount)} failed"),
            CreateMetricCard("Tokens", FormatCompact(snapshot.Usage.TotalTokens), "Usage statistics from /usage")));

        root.Children.Add(CreateSectionHeader("Current Configuration"));
        root.Children.Add(CreateConfigSummary(snapshot.Config));

        root.Children.Add(CreateSectionHeader("Update Reminder"));
        root.Children.Add(CreateUpdateSummary(snapshot));

        root.Children.Add(CreateSectionHeader("Desktop Context"));
        root.Children.Add(CreateDesktopSummary(snapshot));

        root.Children.Add(CreateSectionHeader("Dependency Status"));
        root.Children.Add(CreateDependencySummary());

        root.Children.Add(CreateSectionHeader("Quick Entries"));
        root.Children.Add(CreateQuickActionsPanel());

        if (!string.IsNullOrWhiteSpace(snapshot.AvailableModelsError))
        {
            root.Children.Add(CreateHintCard("Model discovery", snapshot.AvailableModelsError));
        }

        return root;
    }

    private UIElement CreateOverviewHero(ManagementOverviewSnapshot snapshot)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            "Live overview connected to the managed backend API",
            22,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        panel.Children.Add(CreateText(
            $"Management API: {snapshot.ManagementApiBaseUrl}",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 0)));
        panel.Children.Add(CreateText(
            "The overview brings together connection state, account/auth inventory, quota summary, update signals, dependency state, and fast desktop actions.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 4, 0, 0)));

        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateConfigSummary(ManagementConfigSnapshot config)
    {
        var grid = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 8)
        };

        AddKeyValue(grid, "Routing", config.RoutingStrategy ?? "round-robin");
        AddKeyValue(grid, "Debug", FormatBoolean(config.Debug));
        AddKeyValue(grid, "Usage stats", FormatBoolean(config.UsageStatisticsEnabled));
        AddKeyValue(grid, "Log to file", FormatBoolean(config.LoggingToFile));
        AddKeyValue(grid, "Request log", FormatBoolean(config.RequestLog));
        AddKeyValue(grid, "Retry count", config.RequestRetry?.ToString(CultureInfo.InvariantCulture) ?? "0");
        AddKeyValue(grid, "Log cap", config.LogsMaxTotalSizeMb is null ? "-" : $"{config.LogsMaxTotalSizeMb} MB");
        AddKeyValue(grid, "Proxy", string.IsNullOrWhiteSpace(config.ProxyUrl) ? "Not set" : config.ProxyUrl);

        return CreateCard(grid, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateDesktopSummary(ManagementOverviewSnapshot snapshot)
    {
        var grid = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 8)
        };

        AddKeyValue(grid, "Data root", _pathService.Directories.RootDirectory);
        AddKeyValue(grid, "Config directory", _pathService.Directories.ConfigDirectory);
        AddKeyValue(grid, "Backend directory", _pathService.Directories.BackendDirectory);
        AddKeyValue(grid, "Backend state", _backendProcessManager.CurrentStatus.State.ToString());
        AddKeyValue(grid, "Theme", GetThemeLabel(_settings.ThemeMode));
        AddKeyValue(grid, "Tray", _settings.EnableTrayIcon ? "Enabled" : "Disabled");
        AddKeyValue(grid, "Models", snapshot.AvailableModelCount?.ToString(CultureInfo.InvariantCulture) ?? "Check when providers are ready");

        return CreateCard(grid, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateUpdateSummary(ManagementOverviewSnapshot snapshot)
    {
        var grid = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 8)
        };

        AddKeyValue(grid, "Current version", snapshot.ServerVersion ?? "Unknown");
        AddKeyValue(grid, "Latest version", snapshot.LatestVersion ?? "Unknown");
        AddKeyValue(grid, "Status", BuildUpdateStatus(snapshot));
        AddKeyValue(grid, "Channel", "Stable");

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(grid);

        if (!string.IsNullOrWhiteSpace(snapshot.LatestVersionError))
        {
            root.Children.Add(CreateText(
                $"Update check detail: {snapshot.LatestVersionError}",
                12,
                FontWeights.Normal,
                "SecondaryTextBrush"));
        }

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateDependencySummary()
    {
        var dependencyStatus = GetDependencyStatus();
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            dependencyStatus.IsAvailable ? "Desktop dependencies are ready." : "Desktop dependencies need repair.",
            16,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        panel.Children.Add(CreateText(
            dependencyStatus.Summary,
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 0)));

        if (!string.IsNullOrWhiteSpace(dependencyStatus.Detail))
        {
            panel.Children.Add(CreateText(
                dependencyStatus.Detail,
                12,
                FontWeights.Normal,
                "SecondaryTextBrush",
                new Thickness(0, 6, 0, 0)));
        }

        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateQuickActionsPanel()
    {
        var panel = new WrapPanel();
        panel.Children.Add(CreateActionButton("Refresh Overview", async () => await RefreshOverviewAsync(force: true)));
        panel.Children.Add(CreateActionButton(
            _backendProcessManager.CurrentStatus.State == BackendStateKind.Running ? "Restart Backend" : "Start Backend",
            async () =>
            {
                if (_backendProcessManager.CurrentStatus.State == BackendStateKind.Running)
                {
                    await _backendProcessManager.RestartAsync();
                }
                else
                {
                    await _backendProcessManager.StartAsync();
                }

                await RefreshOverviewAsync(force: true);
            }));
        panel.Children.Add(CreateActionButton("Open Config Folder", () =>
        {
            ProcessStartFolder(_pathService.Directories.ConfigDirectory);
            return Task.CompletedTask;
        }));
        panel.Children.Add(CreateActionButton("Open Data Folder", () =>
        {
            ProcessStartFolder(_pathService.Directories.RootDirectory);
            return Task.CompletedTask;
        }));
        panel.Children.Add(CreateActionButton("Repair Backend Files", RepairBackendAssetsAsync));

        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private System.Windows.Controls.Button CreateActionButton(string label, Func<Task> action)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = label,
            Style = TryFindResource("CaptionButtonStyle") as Style,
            Margin = new Thickness(0, 0, 10, 10)
        };

        button.Click += async (_, _) => await action();
        return button;
    }

    private UIElement BuildPlaceholderContent(ShellSection section)
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateCard(
            CreateText(BuildPageBody(section), 15, FontWeights.Normal, "PrimaryTextBrush"),
            new Thickness(0, 0, 0, 16)));
        root.Children.Add(CreateHintCard("Reference", BuildReferenceHint(section)));
        return root;
    }

    private UIElement CreateStatePanel(string title, string detail)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(title, 18, FontWeights.SemiBold, "PrimaryTextBrush"));
        panel.Children.Add(CreateText(detail, 13, FontWeights.Normal, "SecondaryTextBrush", new Thickness(0, 8, 0, 0)));
        return CreateCard(panel);
    }

    private UIElement CreateHintCard(string title, string detail)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(title, 13, FontWeights.SemiBold, "PrimaryTextBrush"));
        panel.Children.Add(CreateText(detail, 13, FontWeights.Normal, "SecondaryTextBrush", new Thickness(0, 6, 0, 0)));
        return CreateCard(panel, new Thickness(0, 0, 0, 16));
    }

    private TextBlock CreateSectionHeader(string text)
    {
        return CreateText(text, 16, FontWeights.SemiBold, "PrimaryTextBrush", new Thickness(0, 4, 0, 10));
    }

    private UniformGrid CreateMetricGrid(params UIElement[] cards)
    {
        var grid = new UniformGrid
        {
            Columns = 3,
            Margin = new Thickness(0, 0, 0, 18)
        };

        foreach (var card in cards)
        {
            grid.Children.Add(card);
        }

        return grid;
    }

    private Border CreateMetricCard(string label, string value, string detail)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(value, 24, FontWeights.SemiBold, "PrimaryTextBrush"));
        panel.Children.Add(CreateText(label, 13, FontWeights.SemiBold, "SecondaryTextBrush", new Thickness(0, 4, 0, 0)));
        panel.Children.Add(CreateText(detail, 12, FontWeights.Normal, "SecondaryTextBrush", new Thickness(0, 8, 0, 0)));
        return CreateCard(panel, new Thickness(0, 0, 12, 12));
    }

    private void AddKeyValue(System.Windows.Controls.Panel root, string key, string value)
    {
        var panel = new StackPanel
        {
            Orientation = WpfOrientation.Vertical,
            Margin = new Thickness(0, 0, 14, 14)
        };
        panel.Children.Add(CreateText(key, 12, FontWeights.SemiBold, "SecondaryTextBrush"));
        panel.Children.Add(CreateText(value, 14, FontWeights.SemiBold, "PrimaryTextBrush", new Thickness(0, 4, 0, 0)));
        root.Children.Add(panel);
    }

    private Border CreateCard(UIElement content, Thickness? margin = null)
    {
        var card = new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Margin = margin ?? new Thickness(0)
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        card.Child = content;
        return card;
    }

    private TextBlock CreateText(
        string text,
        double fontSize,
        FontWeight fontWeight,
        string brushKey,
        Thickness? margin = null)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin ?? new Thickness(0)
        };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return textBlock;
    }

    private static int CountProviders(ManagementOverviewSnapshot snapshot)
    {
        return snapshot.GeminiKeyCount +
            snapshot.CodexKeyCount +
            snapshot.ClaudeKeyCount +
            snapshot.VertexKeyCount +
            snapshot.OpenAiCompatibilityCount;
    }

    private static string FormatBoolean(bool? value)
    {
        return value switch
        {
            true => "Enabled",
            false => "Disabled",
            _ => "Unknown"
        };
    }

    private static string FormatCompact(long value)
    {
        return value switch
        {
            >= 1_000_000 => $"{value / 1_000_000d:0.0}M",
            >= 1_000 => $"{value / 1_000d:0.0}K",
            _ => value.ToString(CultureInfo.InvariantCulture)
        };
    }

    private async Task RepairBackendAssetsAsync()
    {
        try
        {
            await _backendAssetService.EnsureAssetsAsync();
            UpdateSelectedSection();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                exception.Message,
                "Repair Backend Files",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private DependencyCheckResult GetDependencyStatus()
    {
        var backendExecutablePath = Path.Combine(_pathService.Directories.BackendDirectory, BackendExecutableFileName);
        if (File.Exists(backendExecutablePath))
        {
            return new DependencyCheckResult
            {
                IsAvailable = true,
                Summary = "Backend runtime files are present and ready for the desktop manager.",
                Detail = backendExecutablePath
            };
        }

        return new DependencyCheckResult
        {
            IsAvailable = false,
            Summary = "Backend runtime files are missing from the managed backend directory.",
            Detail = $"Expected executable: {backendExecutablePath}"
        };
    }

    private static string BuildUpdateStatus(ManagementOverviewSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.LatestVersionError))
        {
            return "Check failed";
        }

        if (string.IsNullOrWhiteSpace(snapshot.LatestVersion))
        {
            return "Unknown";
        }

        if (string.IsNullOrWhiteSpace(snapshot.ServerVersion))
        {
            return "Current version unavailable";
        }

        var current = NormalizeVersion(snapshot.ServerVersion);
        var latest = NormalizeVersion(snapshot.LatestVersion);

        if (Version.TryParse(current, out var currentVersion) &&
            Version.TryParse(latest, out var latestVersion))
        {
            return latestVersion > currentVersion ? "Update available" : "Up to date";
        }

        return string.Equals(current, latest, StringComparison.OrdinalIgnoreCase)
            ? "Up to date"
            : "Review latest version";
    }

    private static string NormalizeVersion(string version)
    {
        return version.Trim().TrimStart('v', 'V');
    }

    private string BuildPageBody(ShellSection section)
    {
        var backendStatus = _backendProcessManager.CurrentStatus;

        return section.Key switch
        {
            "overview" =>
                $"This native overview surfaces backend state, account and quota summary, update signals, dependency readiness, and desktop health. Current backend state: {backendStatus.State}. Data root: {_pathService.Directories.RootDirectory}.",
            "accounts" =>
                "This route is reserved for OAuth, device flow, cookie import, management keys, and secure credential storage.",
            "quota" =>
                "This route is reserved for quota, request counts, token usage, and model-level usage summaries.",
            "config" =>
                "This route is reserved for native configuration editing, validation, save, and round-trip display of backend settings. The managed configuration services are already in place underneath the shell.",
            "logs" =>
                "This route is reserved for log browsing, filtering, request diagnostics, and diagnostics export. Logs stay on their own page rather than returning to an embedded host console.",
            "system" =>
                $"This route is reserved for backend health, model availability, and connectivity. Current backend state is {backendStatus.State}; use the shell action to start or restart the managed process.",
            "tools" =>
                "This route is reserved for official/cpa source switching and desktop tool integration entry points.",
            "updates" =>
                "This route is reserved for GitHub Releases based stable update checks, a reserved beta channel, and package/version diagnostics.",
            "settings" =>
                "This route is reserved for theme, startup, tray, directory, update, and privacy preferences. Theme switching already works at the shell level and persists to desktop settings.",
            "about" =>
                $"Cli Proxy API Desktop {_buildInfo.InformationalVersion}. The native desktop shell now manages the backend directly and is being expanded page by page around the audited management APIs.",
            _ => section.Subtitle
        };
    }

    private string BuildReferenceHint(ShellSection section)
    {
        return section.Key switch
        {
            "overview" => "The overview is now driven by live management data plus local desktop runtime state.",
            "updates" => "Stable updates are planned against GitHub Releases. Beta remains reserved for a later phase.",
            _ => "This route is reserved in the native shell and will be connected to the audited management APIs as a first-class desktop page."
        };
    }

    private string BuildPageStatus(ShellSection section)
    {
        return $"Route key: {section.Key} | Backend: {_backendProcessManager.CurrentStatus.State} | Theme: {GetThemeLabel(_settings.ThemeMode)} | Tray: {(_settings.EnableTrayIcon ? "Enabled" : "Disabled")}";
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private async Task ExitAndStopBackendAsync()
    {
        _allowClose = true;

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
        }

        await _backendProcessManager.StopAsync();
        Close();
    }

    private void ShowUpdatesPlaceholder()
    {
        System.Windows.MessageBox.Show(
            this,
            "Stable update checks will be connected after the native update flow lands. The entry is wired now so tray and shell behavior remain stable.",
            "Check Updates",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private void ProcessStartFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static AppThemeMode ResolveEffectiveTheme(AppThemeMode requestedTheme)
    {
        if (requestedTheme != AppThemeMode.System)
        {
            return requestedTheme;
        }

        const string personalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        using var key = Registry.CurrentUser.OpenSubKey(personalizeKey);
        var value = key?.GetValue("AppsUseLightTheme");
        return value is int intValue && intValue == 0 ? AppThemeMode.Dark : AppThemeMode.Light;
    }

    private static string GetThemeLabel(AppThemeMode themeMode)
    {
        return themeMode switch
        {
            AppThemeMode.Light => "Light",
            AppThemeMode.Dark => "Dark",
            _ => "System"
        };
    }

    private static List<ShellSection> CreateSections()
    {
        return
        [
            new ShellSection("overview", "\uE80F", "Overview", "Home status, quick entry, and shell health."),
            new ShellSection("accounts", "\uE13D", "Accounts & Auth", "OAuth, device flow, cookies, and management keys."),
            new ShellSection("quota", "\uE9D2", "Quota & Usage", "Quota, request counts, token usage, and model stats."),
            new ShellSection("config", "\uE713", "Configuration", "Backend config editing, save, and validation."),
            new ShellSection("logs", "\uE9D5", "Logs & Diagnostics", "Logs, request diagnostics, and export."),
            new ShellSection("system", "\uE9CE", "System & Models", "Health, models, and connectivity."),
            new ShellSection("tools", "\uE9CA", "Sources & Tools", "Official/CPA switching and desktop integrations."),
            new ShellSection("updates", "\uE895", "Updates & Version", "Stable update checks and release diagnostics."),
            new ShellSection("settings", "\uE713", "Settings", "Theme, startup, tray, and privacy preferences."),
            new ShellSection("about", "\uE946", "About", "Version, notices, and diagnostics entry.")
        ];
    }

    private static void SetBrush(string resourceKey, string hex)
    {
        System.Windows.Application.Current.Resources[resourceKey] =
            new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
    }

    private static Drawing.Icon CreateTrayIcon()
    {
        var resourceInfo = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Resources/Icons/ico.png"));
        if (resourceInfo is null)
        {
            return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
        }

        using var bitmap = new Drawing.Bitmap(resourceInfo.Stream);
        var handle = bitmap.GetHicon();

        try
        {
            using var icon = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private sealed record ShellSection(string Key, string Glyph, string Title, string Subtitle);
}
