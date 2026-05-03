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
    private void MarkStartupPhase(string phase)
    {
        var elapsed = _startupStopwatch.ElapsedMilliseconds;
        var delta = elapsed - _lastStartupMarkMilliseconds;
        _lastStartupMarkMilliseconds = elapsed;
        _logger.Info(
            string.Create(
                CultureInfo.InvariantCulture,
                $"startup phase={phase} elapsedMs={elapsed} deltaMs={delta}"
            )
        );
    }

    private void ShowPreparationStep(double progress, string description, StartupState state)
    {
        _startupState = state;
        ExitAuthenticationCompactWindowMode();
        if (!StartupFlow.IsLoadingVisible || _preparationPanelShownAt is null)
        {
            _preparationPanelShownAt = DateTimeOffset.UtcNow;
        }

        var title =
            state == StartupState.LoadingManagement ? "正在进入管理界面" : "正在准备桌面管理界面";
        StartupFlow.ShowLoading(
            progress,
            title,
            description,
            BuildPreparationStatus(progress, state)
        );

        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.Visibility = Visibility.Visible;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementContentHost.Visibility = Visibility.Visible;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
    }

    private void ShowUpgradeNotice()
    {
        _startupState = StartupState.UpgradeNotice;
        ExitAuthenticationCompactWindowMode();
        _preparationPanelShownAt = null;
        var previousVersion = string.IsNullOrWhiteSpace(_settings.LastSeenApplicationVersion)
            ? "旧版本"
            : _settings.LastSeenApplicationVersion.Trim();
        UpgradeNoticeVersionText.Text =
            $"已从 {previousVersion} 升级到 {CurrentApplicationVersion}";

        UpgradeNoticePanel.Visibility = Visibility.Visible;
        StartupFlow.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementContentHost.Visibility = Visibility.Visible;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
    }

    private void ShowFirstRunKeyReveal()
    {
        _startupState = StartupState.FirstRunKeyReveal;
        EnterAuthenticationCompactWindowMode();
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.ShowFirstRunKey(_firstRunManagementKey, remember: false);
        StartupFlow.Visibility = Visibility.Visible;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
    }

    private void ShowLogin(string? errorMessage = null)
    {
        _startupState = StartupState.NativeLogin;
        EnterAuthenticationCompactWindowMode();
        _shellConnectionStatus = "disconnected";
        UpdateShellConnectionPresentation();
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.ShowLogin(errorMessage, _settings.RememberManagementKey);
        StartupFlow.Visibility = Visibility.Visible;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
    }

    private void ShowLoginError(string message)
    {
        StartupFlow.SetLoginError(message);
    }

    private void ShowBlocker(string title, string description, string detail)
    {
        _startupState = StartupState.Blocked;
        ExitAuthenticationCompactWindowMode();
        _shellConnectionStatus = "error";
        UpdateShellConnectionPresentation();
        _preparationPanelShownAt = null;
        BlockerTitleText.Text = title;
        BlockerDescriptionText.Text = description;
        BlockerDetailText.Text = detail;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Visible;
        ManagementContentHost.Visibility = Visibility.Visible;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
    }

    private void ShowWebView()
    {
        ExitAuthenticationCompactWindowMode();
        _shellConnectionStatus = "connected";
        UpdateShellConnectionPresentation();
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementContentHost.Visibility = Visibility.Visible;
        ManagementWebView.Visibility = Visibility.Visible;
        UpdateNavigationDockPopupVisibility();
    }

    private void EnterAuthenticationCompactWindowMode()
    {
        if (!_isAuthenticationCompactWindowMode)
        {
            _preAuthenticationWindowLayout = CaptureCurrentWindowLayout();
        }

        _isAuthenticationCompactWindowMode = true;
        CloseShellDockPopups();
        SetNavigationDockPopupOpen(false);

        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        ResizeMode = ResizeMode.NoResize;
        MinWidth = AuthenticationCompactWindowWidth;
        MinHeight = AuthenticationCompactWindowHeight;
        MaxWidth = AuthenticationCompactWindowWidth;
        MaxHeight = AuthenticationCompactWindowHeight;
        Width = AuthenticationCompactWindowWidth;
        Height = AuthenticationCompactWindowHeight;
        ExtendsContentIntoTitleBar = false;
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None;

        MainWindowChromeBehavior.ResizeBorderThickness = new Thickness(0);
        MainWindowChromeBehavior.CornerPreference =
            ControlzEx.Behaviors.WindowCornerPreference.DoNotRound;
        MainWindowChromeBehavior.UseNativeCaptionButtons = true;
        MainWindowGlowBehavior.GlowDepth = 0;

        ShellTitleBar.Visibility = Visibility.Collapsed;
        ShellTitleBarRow.Height = new GridLength(0);
        ManagementContentHost.Visibility = Visibility.Collapsed;
        CenterAuthenticationCompactWindow();
    }

    private void ExitAuthenticationCompactWindowMode()
    {
        ApplyMainWindowModeChrome();

        if (!_isAuthenticationCompactWindowMode)
        {
            return;
        }

        _isAuthenticationCompactWindowMode = false;
        var windowLayout = _preAuthenticationWindowLayout;
        _preAuthenticationWindowLayout = null;

        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        if (windowLayout is { } layout)
        {
            Width = NormalizeWindowLength(layout.Width, MainWindowMinWidth, MainWindowDefaultWidth);
            Height = NormalizeWindowLength(
                layout.Height,
                MainWindowMinHeight,
                MainWindowDefaultHeight
            );
            if (IsFiniteWindowValue(layout.Left) && IsFiniteWindowValue(layout.Top))
            {
                Left = layout.Left;
                Top = layout.Top;
            }

            if (layout.WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        else
        {
            Width = MainWindowDefaultWidth;
            Height = MainWindowDefaultHeight;
        }

        RefreshShellDockPopupPlacements();
    }

    private void ApplyMainWindowModeChrome()
    {
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        MinWidth = MainWindowMinWidth;
        MinHeight = MainWindowMinHeight;
        ResizeMode = ResizeMode.CanResize;
        ExtendsContentIntoTitleBar = true;
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Auto;

        MainWindowChromeBehavior.ResizeBorderThickness = new Thickness(8);
        MainWindowChromeBehavior.CornerPreference = ControlzEx.Behaviors.WindowCornerPreference.Round;
        MainWindowChromeBehavior.UseNativeCaptionButtons = false;
        MainWindowGlowBehavior.GlowDepth = 8;

        ShellTitleBarRow.Height = new GridLength(ShellTitleBarHeight);
        ShellTitleBar.Visibility = Visibility.Visible;
        ManagementContentHost.Visibility = Visibility.Visible;
    }

    private WindowLayoutSnapshot CaptureCurrentWindowLayout()
    {
        var state = WindowState;
        var bounds = state == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return new WindowLayoutSnapshot(
            bounds.Left,
            bounds.Top,
            NormalizeWindowLength(bounds.Width, MainWindowMinWidth, MainWindowDefaultWidth),
            NormalizeWindowLength(bounds.Height, MainWindowMinHeight, MainWindowDefaultHeight),
            state
        );
    }

    private void CenterAuthenticationCompactWindow()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + Math.Max(0, (workArea.Width - AuthenticationCompactWindowWidth) / 2);
        Top =
            workArea.Top
            + Math.Max(0, (workArea.Height - AuthenticationCompactWindowHeight) / 2);
    }

    private static double NormalizeWindowLength(double value, double minimum, double fallback)
    {
        return IsFiniteWindowValue(value) && value >= minimum ? value : fallback;
    }

    private static bool IsFiniteWindowValue(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private void UpdateNavigationDockPopupVisibility()
    {
        SetNavigationDockPopupOpen(CanShowNavigationDockPopup());
    }

    private void SetNavigationDockPopupOpen(bool isOpen)
    {
        if (!IsNavigationDockInitialized())
        {
            return;
        }

        if (isOpen && !CanShowNavigationDockPopup())
        {
            isOpen = false;
        }

        if (!isOpen)
        {
            _navigationDockCollapseTimer.Stop();
            AnimateNavigationDock(NavigationDockVisualState.Resting);
        }

        if (ShellNavigationDockPopup.IsOpen != isOpen)
        {
            ShellNavigationDockPopup.IsOpen = isOpen;
        }
    }

    private void RefreshShellDockPopupPlacements()
    {
        RefreshShellBrandDockPopupPlacement();
        RefreshNavigationDockPopupPlacement();
    }

    private void RefreshNavigationDockPopupPlacement()
    {
        if (!CanShowNavigationDockPopup())
        {
            SetNavigationDockPopupOpen(false);
            return;
        }

        if (!ShellNavigationDockPopup.IsOpen)
        {
            return;
        }

        RefreshDockPopupPlacement(ShellNavigationDockPopup);
    }

    private void RefreshShellBrandDockPopupPlacement()
    {
        if (ShellBrandDockPopup is null || !ShellBrandDockPopup.IsOpen)
        {
            return;
        }

        RefreshDockPopupPlacement(ShellBrandDockPopup);
    }

    private static void RefreshDockPopupPlacement(Popup popup)
    {
        var horizontalOffset = popup.HorizontalOffset;
        popup.HorizontalOffset = horizontalOffset + 0.01;
        popup.HorizontalOffset = horizontalOffset;
    }
}
