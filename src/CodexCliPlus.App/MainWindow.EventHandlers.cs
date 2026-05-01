using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CodexCliPlus.Core.Abstractions.Build;
using CodexCliPlus.Core.Abstractions.Configuration;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Persistence;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Abstractions.Updates;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Services;
using CodexCliPlus.Services.Notifications;
using CodexCliPlus.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace CodexCliPlus;

public partial class MainWindow
{
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            MarkStartupPhase("window-loaded");
            _isMainWindowActive = IsActive;
            ShowPreparationStep(5, "正在加载本地配置。", StartupState.Preparing);
            var settingsFileExists = File.Exists(_pathService.Directories.SettingsFilePath);
            _settings = await _appConfigurationService.LoadAsync();
            MarkStartupPhase("settings-loaded");
            ApplyShellTheme(_settings.ThemeMode);
            UpdateShellThemePresentation();
            UpdateShellConnectionPresentation();
            InitializeTrayIcon();

            if (!_settings.SecurityKeyOnboardingCompleted)
            {
                await BeginFirstRunKeyRevealAsync();
                return;
            }

            if (settingsFileExists && IsUpgradeNoticePending())
            {
                await EnsureMinimumPreparationDisplayAsync();
                ShowUpgradeNotice();
                return;
            }

            await ContinueAfterStartupGateAsync();
        }
        catch (Exception exception)
        {
            ShowBlocker(
                "桌面宿主启动失败",
                "无法加载桌面配置或初始化宿主环境。",
                exception.Message
            );
        }
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        await SyncPersistenceBeforeExitAsync();
        _backendProcessManager.StatusChanged -= BackendProcessManager_StatusChanged;
        _notificationService.NotificationRequested -=
            ShellNotificationService_NotificationRequested;
        _changeBroadcastService.DataChanged -= ManagementChangeBroadcastService_DataChanged;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _navigationDockCollapseTimer.Tick -= NavigationDockCollapseTimer_Tick;
        _firstRunConfirmCountdown?.Cancel();
        _firstRunConfirmCountdown?.Dispose();
        _settingsOverviewRefreshCts?.Cancel();
        _settingsOverviewRefreshCts?.Dispose();
        CancelUsageStatsSyncDebounce();
        CancelPostStartupPersistenceImport();
        CloseSettingsWindow();
        TrayIcon.Unregister();
    }

    public void Dispose()
    {
        _backendProcessManager.StatusChanged -= BackendProcessManager_StatusChanged;
        _notificationService.NotificationRequested -=
            ShellNotificationService_NotificationRequested;
        _changeBroadcastService.DataChanged -= ManagementChangeBroadcastService_DataChanged;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _navigationDockCollapseTimer.Tick -= NavigationDockCollapseTimer_Tick;
        _firstRunConfirmCountdown?.Cancel();
        _firstRunConfirmCountdown?.Dispose();
        _settingsOverviewRefreshCts?.Cancel();
        _settingsOverviewRefreshCts?.Dispose();
        CancelUsageStatsSyncDebounce();
        CancelPostStartupPersistenceImport();
        CloseSettingsWindow();
        GC.SuppressFinalize(this);
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        _isMainWindowActive = true;
        UpdateNavigationDockPopupVisibility();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        _isMainWindowActive = false;
        CloseShellDockPopups();
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_settings.ThemeMode != AppThemeMode.System)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            ApplyShellTheme(_settings.ThemeMode);
            UpdateShellThemePresentation();
            PostWebUiCommand(
                new
                {
                    type = "setTheme",
                    theme = ToWebTheme(_settings.ThemeMode),
                    resolvedTheme = ToWebResolvedTheme(_settings.ThemeMode),
                }
            );
        });
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CloseShellDockPopups();
        if (_allowClose)
        {
            return;
        }

        if (_settings.EnableTrayIcon && _settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    private void Window_PlacementChanged(object? sender, EventArgs e)
    {
        if (!CanShowNavigationDockPopup())
        {
            SetNavigationDockPopupOpen(false);
        }

        PositionSettingsWindow();
        RefreshNavigationDockPopupPlacement();
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.SecurityKeyOnboardingCompleted)
        {
            await BeginFirstRunKeyRevealAsync();
            return;
        }

        await InitializeHostAsync(
            restartBackend: _backendProcessManager.CurrentStatus.State != BackendStateKind.Running
        );
    }

    private async void UpgradeContinueButton_Click(object sender, RoutedEventArgs e)
    {
        UpgradeContinueButton.IsEnabled = false;
        try
        {
            _settings.LastSeenApplicationVersion = CurrentApplicationVersion;
            await _appConfigurationService.SaveAsync(_settings);
            await ContinueAfterStartupGateAsync();
        }
        catch (Exception exception)
        {
            ShowBlocker("更新确认失败", "无法保存本次更新确认状态。", exception.Message);
        }
        finally
        {
            UpgradeContinueButton.IsEnabled = true;
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await SignInAsync();
    }

    private async void ManagementKeyPasswordBox_KeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e
    )
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await SignInAsync();
    }

    private void FirstRunCopyKeyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_firstRunManagementKey);
            _notificationService.ShowAuto("安全密钥已复制。");
        }
        catch (Exception exception)
        {
            _notificationService.ShowManual("复制失败", exception.Message);
        }
    }

    private async void FirstRunSaveToDesktopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var filePath = BuildDesktopSecurityKeyFilePath();
            var content = BuildSecurityKeyFileContent(_firstRunManagementKey);
            await File.WriteAllTextAsync(
                filePath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
            _notificationService.ShowAuto($"已保存到桌面：{Path.GetFileName(filePath)}");
        }
        catch (Exception exception)
        {
            _notificationService.ShowManual("保存失败", BuildDesktopSaveErrorMessage(exception));
        }
    }

    private async void FirstRunEnterManagementButton_Click(object sender, RoutedEventArgs e)
    {
        await BeginFirstRunConfirmationAsync();
    }

    private async void FirstRunConfirmContinueButton_Click(object sender, RoutedEventArgs e)
    {
        await CompleteFirstRunAsync();
    }

    private void FirstRunConfirmCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CancelFirstRunConfirmation();
    }

    private async void ForgotSecurityKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var firstConfirm = MessageBox.Show(
            this,
            "重置会清空后端配置、Provider 配置、OAuth/Auth 文件、本机保存的安全密钥和 WebUI 本地登录状态。日志、诊断文件和 backend 资产会保留。",
            "重置安全密钥",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        if (firstConfirm != MessageBoxResult.Yes)
        {
            return;
        }

        var secondConfirm = MessageBox.Show(
            this,
            "确认重置后，现有账号与配置无法通过桌面壳恢复。是否继续？",
            "确认重置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        if (secondConfirm != MessageBoxResult.Yes)
        {
            return;
        }

        await ResetSecurityKeyAsync();
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        CloseShellDockPopups();
        WindowState = WindowState.Minimized;
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShellSidebarButton_Click(object sender, RoutedEventArgs e)
    {
        ExpandNavigationDock(showLabels: true);
    }

    private async void ShellBrandDockButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateShellDockPresentation();
        if (ShellBrandDockPopup.IsOpen)
        {
            await HideShellBrandDockPopupAsync();
            return;
        }

        ShellBrandDockPopup.IsOpen = true;
        if (ShellBrandDockPopup.IsOpen)
        {
            await RefreshShellDockOverviewAsync();
        }
    }

    private void ShellBrandDockPopup_Opened(object sender, EventArgs e)
    {
        AnimateShellBrandDockPopupIn();
    }

    private void ShellBrandDockPopup_Closed(object sender, EventArgs e)
    {
        _isShellBrandDockClosing = false;
        ResetShellBrandDockPopupVisual();
    }

    private void ShellDockCopyBackendAddressButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_shellApiBase))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(_shellApiBase);
            _notificationService.ShowAuto("后端地址已复制。");
        }
        catch (Exception exception)
        {
            _notificationService.ShowManual("复制后端地址失败", exception.Message);
        }
    }

    private async void ShellThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var next = IsEffectiveDarkTheme(_settings.ThemeMode)
            ? AppThemeMode.White
            : AppThemeMode.Dark;

        await SetShellThemeAsync(next);
    }

    private async void ShellSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await HideShellBrandDockPopupAsync();
        if (_settingsOverlayOpen)
        {
            await HideSettingsOverlayAsync();
            return;
        }

        await ShowSettingsOverlayAsync();
    }

    private async void SettingsOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SettingsOverlay))
        {
            await HideSettingsOverlayAsync();
        }
    }

    private void SettingsDialogCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void SilentLoginLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WpfCheckBox checkBox })
        {
            checkBox.IsChecked = checkBox.IsChecked != true;
            e.Handled = true;
        }
    }

    private async void SettingsFollowSystemCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressFollowSystemChange)
        {
            return;
        }

        var followSystem = SettingsFollowSystemCheckBox.IsChecked == true;
        if (followSystem)
        {
            await SetShellThemeAsync(AppThemeMode.System);
            return;
        }

        var explicitTheme = IsEffectiveDarkTheme(AppThemeMode.System)
            ? AppThemeMode.Dark
            : AppThemeMode.White;
        await SetShellThemeAsync(explicitTheme);
    }

    private void SettingsOpenMainRepoButton_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal("https://github.com/router-for-me/CLIProxyAPI");
    }

    private void SettingsOpenWebUiRepoButton_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal("https://github.com/router-for-me/Cli-Proxy-API-Management-Center");
    }

    private void SettingsOpenDocsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal("https://help.router-for.me/");
    }

    private async void SettingsClearLoginButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsClearLoginButton.IsEnabled = false;
        try
        {
            await ResetWebUiLocalAuthStateAsync();
            _settings.ManagementKey = string.Empty;
            _settings.RememberManagementKey = false;
            await _appConfigurationService.SaveAsync(_settings);
            RememberManagementKeyCheckBox.IsChecked = false;
            await HideSettingsOverlayAsync();
            ShowLogin("本地登录信息已清理，请重新输入安全密钥。");
            _notificationService.ShowAuto("本地登录信息已清理。");
        }
        catch (Exception exception)
        {
            _notificationService.ShowManual("清理登录失败", exception.Message);
        }
        finally
        {
            SettingsClearLoginButton.IsEnabled = true;
        }
    }

    private async void SettingsCheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsCheckUpdateButton.IsEnabled = false;
        SettingsApplyUpdateButton.IsEnabled = false;
        SettingsUpdateProgressBar.Visibility = Visibility.Collapsed;
        SetSettingsUpdateStatus("检查中");
        try
        {
            var result = await _updateCheckService.CheckAsync(_buildInfo.ApplicationVersion);
            SetSettingsUpdateStatus(
                result.IsUpdateAvailable
                    ? $"发现版本 {result.LatestVersion ?? "未知"}"
                    : $"已是最新 {CurrentApplicationVersion}"
            );
            SettingsApplyUpdateButton.Tag = result;
            SettingsApplyUpdateButton.IsEnabled = _updateInstallerService.CanPrepareInstaller(
                result
            );
            if (result.IsUpdateAvailable && !SettingsApplyUpdateButton.IsEnabled)
            {
                SetSettingsUpdateStatus("发现版本，但无可用安装包");
            }

            _notificationService.ShowAuto(
                result.IsUpdateAvailable ? "发现新版本。" : "当前已是最新版本。"
            );
        }
        catch (Exception exception)
        {
            SetSettingsUpdateStatus("检查失败");
            _notificationService.ShowManual("检查更新失败", exception.Message);
        }
        finally
        {
            SettingsCheckUpdateButton.IsEnabled = true;
        }
    }

    private async void SettingsApplyUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsApplyUpdateButton.IsEnabled = false;
        SettingsCheckUpdateButton.IsEnabled = false;
        SettingsUpdateProgressBar.Visibility = Visibility.Visible;
        SettingsUpdateProgressBar.IsIndeterminate = true;

        try
        {
            var result = SettingsApplyUpdateButton.Tag as UpdateCheckResult;
            if (result is null || !_updateInstallerService.CanPrepareInstaller(result))
            {
                SetSettingsUpdateStatus("重新检查中");
                result = await _updateCheckService.CheckAsync(_buildInfo.ApplicationVersion);
                SettingsApplyUpdateButton.Tag = result;
            }

            if (!_updateInstallerService.CanPrepareInstaller(result))
            {
                SetSettingsUpdateStatus("无可用安装包");
                _notificationService.ShowManual(
                    "应用更新失败",
                    "当前更新结果不包含可应用的离线更新包。"
                );
                return;
            }

            SetSettingsUpdateStatus("下载校验中");
            var preparedInstaller = await _updateInstallerService.DownloadInstallerAsync(result);
            SetSettingsUpdateStatus("启动更新程序");
            await SyncPersistenceBeforeExitAsync();
            await _updateInstallerService.LaunchInstallerAsync(preparedInstaller);
            _allowClose = true;
            TrayIcon.Unregister();
            await _backendProcessManager.StopAsync();
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            SetSettingsUpdateStatus("应用失败");
            _notificationService.ShowManual("应用更新失败", exception.Message);
        }
        finally
        {
            SettingsUpdateProgressBar.Visibility = Visibility.Collapsed;
            SettingsCheckUpdateButton.IsEnabled = true;
            if (SettingsApplyUpdateButton.Tag is UpdateCheckResult result)
            {
                SettingsApplyUpdateButton.IsEnabled = _updateInstallerService.CanPrepareInstaller(
                    result
                );
            }
        }
    }

    private void SettingsClearUsageButton_Click(object sender, RoutedEventArgs e)
    {
        _ = ClearUsageStatsAsync();
    }

    private void HideToTrayButton_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
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
        if (!_settings.SecurityKeyOnboardingCompleted)
        {
            await BeginFirstRunKeyRevealAsync();
            return;
        }

        if (
            string.IsNullOrWhiteSpace(_settings.ManagementKey)
            && _backendProcessManager.CurrentStatus.State != BackendStateKind.Running
        )
        {
            ShowLogin("请先输入安全密钥。");
            return;
        }

        await InitializeHostAsync(restartBackend: true);
    }

    private async void TrayCheckUpdatesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _updateCheckService.CheckAsync(_buildInfo.ApplicationVersion);
            var title = result.IsUpdateAvailable ? "发现更新" : "检查更新";
            var message = result.IsUpdateAvailable
                ? $"检测到新版本：{result.LatestVersion}{Environment.NewLine}{Environment.NewLine}{result.Detail}"
                : $"{result.Status}{Environment.NewLine}{Environment.NewLine}{result.Detail}";
            MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "检查更新失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private async void TrayExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        TrayIcon.Unregister();
        await SyncPersistenceBeforeExitAsync();
        await _backendProcessManager.StopAsync();
        Close();
    }
}
