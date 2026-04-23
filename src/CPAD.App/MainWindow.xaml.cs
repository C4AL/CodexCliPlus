using System.Windows;
using System.Windows.Media;

using CPAD.Core.Abstractions.Build;
using CPAD.Core.Abstractions.Configuration;
using CPAD.Core.Abstractions.Management;
using CPAD.Core.Abstractions.Updates;
using CPAD.Core.Enums;
using CPAD.Core.Models;
using CPAD.Infrastructure.Backend;
using CPAD.ViewModels;
using CPAD.Views.Pages;

using Microsoft.Win32;

using Wpf.Ui;
using Wpf.Ui.Abstractions;

using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;

namespace CPAD;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow, INavigationWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly INavigationService _navigationService;
    private readonly BackendProcessManager _backendProcessManager;
    private readonly IManagementSessionService _sessionService;
    private readonly IManagementConfigurationService _configurationServiceApi;
    private readonly IAppConfigurationService _appConfigurationService;
    private readonly IBuildInfo _buildInfo;
    private readonly IManagementNavigationService _managementNavigationService;
    private readonly IUnsavedChangesGuard _unsavedChangesGuard;
    private readonly IUpdateCheckService _updateCheckService;

    private AppSettings _settings = new();
    private bool _allowClose;
    private bool _suppressSelectionChange;
    private string _lastSelectedRouteKey = "dashboard";

    public MainWindow(
        MainWindowViewModel viewModel,
        NotifyIconViewModel notifyIconViewModel,
        INavigationService navigationService,
        ISnackbarService snackbarService,
        INavigationViewPageProvider navigationViewPageProvider,
        BackendProcessManager backendProcessManager,
        IManagementSessionService sessionService,
        IManagementConfigurationService configurationServiceApi,
        IAppConfigurationService appConfigurationService,
        IBuildInfo buildInfo,
        IManagementNavigationService managementNavigationService,
        IUnsavedChangesGuard unsavedChangesGuard,
        IUpdateCheckService updateCheckService)
    {
        _viewModel = viewModel;
        _navigationService = navigationService;
        _backendProcessManager = backendProcessManager;
        _sessionService = sessionService;
        _configurationServiceApi = configurationServiceApi;
        _appConfigurationService = appConfigurationService;
        _buildInfo = buildInfo;
        _managementNavigationService = managementNavigationService;
        _unsavedChangesGuard = unsavedChangesGuard;
        _updateCheckService = updateCheckService;

        DataContext = _viewModel;
        InitializeComponent();

        TrayMenu.DataContext = notifyIconViewModel;
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        navigationService.SetNavigationControl(RootNavigation);
        RootNavigation.SetPageProviderService(navigationViewPageProvider);

        _backendProcessManager.StatusChanged += BackendProcessManager_StatusChanged;
    }

    public Wpf.Ui.Controls.INavigationView GetNavigation() => RootNavigation;

    public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        RootNavigation.SetServiceProvider(serviceProvider);
    }

    public void SetPageService(INavigationViewPageProvider navigationViewPageProvider)
    {
        RootNavigation.SetPageProviderService(navigationViewPageProvider);
    }

    public void ShowWindow() => Show();

    public void CloseWindow() => Close();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _appConfigurationService.LoadAsync();
        await ApplyThemeAsync(_settings.ThemeMode, persist: false);
        InitializeTrayIcon();
        SelectRoute("dashboard", typeof(DashboardPage));
        await RefreshShellAsync();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _backendProcessManager.StatusChanged -= BackendProcessManager_StatusChanged;
        TrayIcon.Unregister();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            if (!_unsavedChangesGuard.ConfirmLeave("退出应用"))
            {
                e.Cancel = true;
            }

            return;
        }

        if (_settings.EnableTrayIcon && _settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (!_unsavedChangesGuard.ConfirmLeave("关闭窗口"))
        {
            e.Cancel = true;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshShellAsync();
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
        UpdateFooter(_backendProcessManager.CurrentStatus, VersionStatusText.Text);
    }

    private async void BackendActionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_backendProcessManager.CurrentStatus.State == BackendStateKind.Running)
            {
                await _backendProcessManager.RestartAsync();
            }
            else
            {
                await _backendProcessManager.StartAsync();
            }

            await RefreshShellAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "后端操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RootNavigation_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectionChange)
        {
            return;
        }

        if (RootNavigation.SelectedItem is not Wpf.Ui.Controls.NavigationViewItem item ||
            item.Content is not string label ||
            item.Tag is not string routeKey)
        {
            return;
        }

        if (string.Equals(routeKey, _lastSelectedRouteKey, StringComparison.OrdinalIgnoreCase))
        {
            UpdateSubtitle(label);
            return;
        }

        if (!_unsavedChangesGuard.ConfirmLeave(label))
        {
            RevertNavigationSelection();
            return;
        }

        _managementNavigationService.TryNavigate(routeKey);
        _lastSelectedRouteKey = routeKey;
        UpdateSubtitle(label);
    }

    private async void BackendProcessManager_StatusChanged(object? sender, BackendStatusSnapshot snapshot)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            UpdateHeader(snapshot, HeaderConnectionText.Text);
            UpdateFooter(snapshot, VersionStatusText.Text);
        });
    }

    private void TrayIcon_LeftDoubleClick(object sender, RoutedEventArgs e)
    {
        RestoreFromTray();
    }

    private void TrayOpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RestoreFromTray();
    }

    private async void TrayRestartBackendMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_backendProcessManager.CurrentStatus.State == BackendStateKind.Running)
            {
                await _backendProcessManager.RestartAsync();
            }
            else
            {
                await _backendProcessManager.StartAsync();
            }

            await RefreshShellAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "后端操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TrayCheckUpdatesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _updateCheckService.CheckAsync(_buildInfo.ApplicationVersion);
            var title = result.IsUpdateAvailable ? "发现更新" : "检查更新";
            var message = result.IsUpdateAvailable
                ? $"检测到新版本：{result.LatestVersion}\n\n{result.Detail}"
                : $"{result.Status}\n\n{result.Detail}";
            MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "检查更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TrayExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!_unsavedChangesGuard.ConfirmLeave("退出应用"))
        {
            return;
        }

        _allowClose = true;
        TrayIcon.Unregister();
        await _backendProcessManager.StopAsync();
        Close();
    }

    private async Task RefreshShellAsync()
    {
        var snapshot = _backendProcessManager.CurrentStatus;
        var connectionSummary = "未连接";
        var versionSummary = $"桌面版本：{_buildInfo.ApplicationVersion}";

        try
        {
            var connection = await _sessionService.GetConnectionAsync();
            connectionSummary = connection.ManagementApiBaseUrl;

            var config = await _configurationServiceApi.GetConfigAsync();
            if (!string.IsNullOrWhiteSpace(config.Metadata.Version))
            {
                versionSummary = $"服务版本：{config.Metadata.Version}";
            }

            snapshot = _backendProcessManager.CurrentStatus;
        }
        catch (Exception exception)
        {
            if (snapshot.State == BackendStateKind.Error)
            {
                connectionSummary = exception.Message;
            }
        }

        UpdateHeader(snapshot, connectionSummary);
        UpdateFooter(snapshot, versionSummary);
    }

    private async Task ApplyThemeAsync(AppThemeMode themeMode, bool persist)
    {
        var effectiveTheme = ResolveEffectiveTheme(themeMode);
        var isDark = effectiveTheme == AppThemeMode.Dark;

        SetBrush("ApplicationBackgroundBrush", isDark ? "#111827" : "#F4F6FA");
        SetBrush("SurfaceBrush", isDark ? "#16202B" : "#FFFFFF");
        SetBrush("SurfaceAltBrush", isDark ? "#1E2A38" : "#EEF2F7");
        SetBrush("AccentBrush", isDark ? "#34D3C2" : "#0F766E");
        SetBrush("AccentSoftBrush", isDark ? "#123A36" : "#D9F1EE");
        SetBrush("PrimaryTextBrush", isDark ? "#E5EEF7" : "#17202B");
        SetBrush("SecondaryTextBrush", isDark ? "#A8B4C5" : "#526070");
        SetBrush("BorderBrush", isDark ? "#314153" : "#D7DCE5");

        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
            isDark
                ? Wpf.Ui.Appearance.ApplicationTheme.Dark
                : Wpf.Ui.Appearance.ApplicationTheme.Light);

        ThemeButton.ToolTip = $"切换主题，当前为 {GetThemeLabel(themeMode)}";
        ThemeStatusText.Text = $"主题：{GetThemeLabel(themeMode)}";

        if (persist)
        {
            await _appConfigurationService.SaveAsync(_settings);
        }
    }

    private void InitializeTrayIcon()
    {
        if (!_settings.EnableTrayIcon)
        {
            TrayIcon.Unregister();
            return;
        }

        TrayIcon.Register();
    }

    private void UpdateHeader(BackendStatusSnapshot snapshot, string connectionSummary)
    {
        HeaderConnectionText.Text = snapshot.State switch
        {
            BackendStateKind.Running => "已连接",
            BackendStateKind.Starting => "连接中",
            BackendStateKind.Error => "连接异常",
            _ => "未启动"
        };

        ConnectionDot.Fill = snapshot.State switch
        {
            BackendStateKind.Running => CreateBrush("#16A34A"),
            BackendStateKind.Starting => CreateBrush("#CA8A04"),
            BackendStateKind.Error => CreateBrush("#DC2626"),
            _ => CreateBrush("#64748B")
        };

        ConnectionStatusText.Text = $"管理地址：{connectionSummary}";
        BackendActionButton.Content = snapshot.State == BackendStateKind.Running ? "重启后端" : "启动后端";
    }

    private void UpdateFooter(BackendStatusSnapshot snapshot, string versionText)
    {
        BackendStatusText.Text = $"后端状态：{FormatBackendState(snapshot.State)}";
        ThemeStatusText.Text = $"主题：{GetThemeLabel(_settings.ThemeMode)}";
        VersionStatusText.Text = versionText;
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

    private void SelectRoute(string routeKey, Type pageType)
    {
        _suppressSelectionChange = true;
        _lastSelectedRouteKey = routeKey;
        _managementNavigationService.TryNavigate(routeKey);
        RootNavigation.Navigate(pageType);
        UpdateSubtitle(FindNavigationLabel(routeKey));
        _suppressSelectionChange = false;
    }

    private void RevertNavigationSelection()
    {
        _suppressSelectionChange = true;
        RootNavigation.Navigate(GetPageType(_lastSelectedRouteKey));
        _suppressSelectionChange = false;
    }

    private static Type GetPageType(string routeKey)
    {
        return routeKey switch
        {
            "config" => typeof(ConfigPage),
            "ai-providers" => typeof(AiProvidersPage),
            "auth-files" => typeof(AuthFilesPage),
            "oauth" => typeof(OAuthPage),
            "quota" => typeof(QuotaPage),
            "usage" => typeof(UsagePage),
            "logs" => typeof(LogsPage),
            "system" => typeof(SystemPage),
            _ => typeof(DashboardPage)
        };
    }

    private string FindNavigationLabel(string routeKey)
    {
        return RootNavigation.MenuItems
            .OfType<Wpf.Ui.Controls.NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, routeKey, StringComparison.OrdinalIgnoreCase))
            ?.Content?.ToString() ?? "仪表盘";
    }

    private void UpdateSubtitle(string label)
    {
        _viewModel.Subtitle = $"{label} · CPAD 原生管理中心";
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
            AppThemeMode.Light => "浅色",
            AppThemeMode.Dark => "深色",
            _ => "跟随系统"
        };
    }

    private static string FormatBackendState(BackendStateKind state)
    {
        return state switch
        {
            BackendStateKind.Starting => "启动中",
            BackendStateKind.Running => "运行中",
            BackendStateKind.Error => "异常",
            _ => "已停止"
        };
    }

    private static void SetBrush(string resourceKey, string hex)
    {
        Application.Current.Resources[resourceKey] = CreateBrush(hex);
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}
