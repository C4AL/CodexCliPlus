using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

using CPAD.Core.Abstractions.Build;
using CPAD.Core.Abstractions.Configuration;
using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Constants;
using CPAD.Core.Enums;
using CPAD.Core.Models;
using CPAD.Infrastructure.Backend;

using Microsoft.Win32;

using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace CPAD;

public partial class MainWindow : Window
{
    private readonly BackendProcessManager _backendProcessManager;
    private readonly IAppConfigurationService _configurationService;
    private readonly IPathService _pathService;
    private readonly IBuildInfo _buildInfo;
    private readonly List<ShellSection> _sections = CreateSections();

    private AppSettings _settings = new();
    private Forms.NotifyIcon? _notifyIcon;
    private bool _allowClose;

    public MainWindow(
        BackendProcessManager backendProcessManager,
        IAppConfigurationService configurationService,
        IPathService pathService,
        IBuildInfo buildInfo)
    {
        _backendProcessManager = backendProcessManager;
        _configurationService = configurationService;
        _pathService = pathService;
        _buildInfo = buildInfo;

        InitializeComponent();

        NavigationList.ItemsSource = _sections;
        NavigationList.SelectedIndex = 0;
        _backendProcessManager.StatusChanged += BackendProcessManager_StatusChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _configurationService.LoadAsync();
        await ApplyThemeAsync(_settings.ThemeMode, persist: false);
        InitializeTrayIcon();
        UpdateSelectedSection();
        UpdateBackendStatus(_backendProcessManager.CurrentStatus);
        UpdateFooter();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _backendProcessManager.StatusChanged -= BackendProcessManager_StatusChanged;
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
            case "system":
                if (_backendProcessManager.CurrentStatus.State == BackendStateKind.Running)
                {
                    await _backendProcessManager.RestartAsync();
                }
                else
                {
                    await _backendProcessManager.StartAsync();
                }

                break;
            case "updates":
                ShowUpdatesPlaceholder();
                break;
            default:
                RestoreFromTray();
                break;
        }
    }

    private void SecondaryPageActionButton_Click(object sender, RoutedEventArgs e)
    {
        var section = NavigationList.SelectedItem as ShellSection;
        if (section?.Key == "about")
        {
            ProcessStartFolder(_pathService.Directories.RootDirectory);
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
        PageBodyText.Text = BuildPageBody(section);
        PageReferenceText.Text = BuildReferenceHint(section);
        PageStatusText.Text = BuildPageStatus(section);

        switch (section.Key)
        {
            case "overview":
            case "system":
                PrimaryPageActionButton.Visibility = Visibility.Visible;
                SecondaryPageActionButton.Visibility = Visibility.Visible;
                PrimaryPageActionButton.Content = _backendProcessManager.CurrentStatus.State == BackendStateKind.Running
                    ? "Restart Backend"
                    : "Start Backend";
                SecondaryPageActionButton.Content = "Open Config Folder";
                break;
            case "updates":
                PrimaryPageActionButton.Visibility = Visibility.Visible;
                SecondaryPageActionButton.Visibility = Visibility.Visible;
                PrimaryPageActionButton.Content = "Check Updates";
                SecondaryPageActionButton.Content = "Open Data Folder";
                break;
            case "about":
                PrimaryPageActionButton.Visibility = Visibility.Collapsed;
                SecondaryPageActionButton.Visibility = Visibility.Visible;
                SecondaryPageActionButton.Content = "Open Data Folder";
                break;
            default:
                PrimaryPageActionButton.Visibility = Visibility.Collapsed;
                SecondaryPageActionButton.Visibility = Visibility.Visible;
                SecondaryPageActionButton.Content = "Open Config Folder";
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

    private string BuildPageBody(ShellSection section)
    {
        var backendStatus = _backendProcessManager.CurrentStatus;

        return section.Key switch
        {
            "overview" =>
                $"This native overview surfaces backend state, data root, and desktop health. Current backend state: {backendStatus.State}. Data root: {_pathService.Directories.RootDirectory}. Live dashboard data is being connected to the audited CPA-UV and Management Center sources.",
            "accounts" =>
                "This route is reserved for OAuth, device flow, cookie import, management keys, and secure credential storage. The native workflow will be mapped to the audited Management Center authentication flows.",
            "quota" =>
                "This route is reserved for quota, request counts, token usage, and model-level usage summaries. The native page will follow the Management Center quota and usage semantics.",
            "config" =>
                "This route is reserved for native configuration editing, validation, save, and round-trip display of backend settings. The managed configuration services are already in place underneath the shell.",
            "logs" =>
                "This route is reserved for log browsing, filtering, request diagnostics, and diagnostics export. Logs stay on their own page rather than returning to an embedded host console.",
            "system" =>
                $"This route is reserved for backend health, model availability, and connectivity. Current backend state is {backendStatus.State}; use the shell action to start or restart the managed process.",
            "tools" =>
                "This route is reserved for official/cpa source switching and desktop tool integration entry points. The page will align to the audited Management Center and CPA-UV references.",
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
            "overview" => "Shell structure follows the BetterGI main window pattern, while overview semantics are being aligned to CPA-UV and the Management Center dashboard.",
            "updates" => "Stable updates are planned against GitHub Releases. Beta remains reserved for a later phase.",
            _ => "This route is reserved in the native shell and will be connected to the audited management APIs without reintroducing the old WebView host."
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
