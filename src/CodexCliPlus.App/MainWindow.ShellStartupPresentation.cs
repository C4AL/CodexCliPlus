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
        if (_isManagementEntryTransitionActive)
        {
            if (_preparationPanelShownAt is null)
            {
                _preparationPanelShownAt = DateTimeOffset.UtcNow;
            }

            UpdateManagementEntryTransitionStatus(description);
            UpgradeNoticePanel.Visibility = Visibility.Collapsed;
            StartupFlow.Visibility = Visibility.Collapsed;
            BlockerPanel.Visibility = Visibility.Collapsed;
            ManagementContentHost.Visibility = Visibility.Visible;
            SetNavigationDockPopupOpen(false);
            return;
        }

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
        CloseManagementEntryTransitionImmediately();
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
        CloseManagementEntryTransitionImmediately();
        _startupState = StartupState.FirstRunKeyReveal;
        EnterAuthenticationCompactWindowMode();
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.ShowFirstRunKey(
            _firstRunManagementKey,
            rememberPassword: false,
            autoLogin: false
        );
        StartupFlow.Visibility = Visibility.Visible;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
    }

    private void ShowLogin(string? errorMessage = null)
    {
        CloseManagementEntryTransitionImmediately();
        _startupState = StartupState.NativeLogin;
        EnterAuthenticationCompactWindowMode();
        _shellConnectionStatus = "disconnected";
        UpdateShellConnectionPresentation();
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.ShowLogin(errorMessage, _settings.RememberPassword, _settings.AutoLogin);
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
        CloseManagementEntryTransitionImmediately();
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

    private bool ShouldUseManagementEntryTransition()
    {
        return _isAuthenticationCompactWindowMode
            && Math.Abs(Width - AuthenticationCompactWindowWidth) < 1
            && Math.Abs(Height - AuthenticationCompactWindowHeight) < 1;
    }

    private async Task BeginManagementEntryTransitionAsync()
    {
        _isManagementEntryTransitionActive = true;
        _preparationPanelShownAt = DateTimeOffset.UtcNow;
        CloseShellDockPopups();
        SetNavigationDockPopupOpen(false);

        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        ManagementEntryTransitionOverlay.Opacity = 0;
        ManagementEntryTransitionPopup.IsOpen = false;

        var targetState = await RestoreMainWindowForManagementEntryTransitionAsync();
        ShowManagementEntryTransitionPopup();

        if (targetState == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
            RefreshManagementEntryTransitionPopupPlacement();
        }
    }

    private async Task<WindowState> RestoreMainWindowForManagementEntryTransitionAsync()
    {
        var target = ResolveAuthenticationExitLayout();
        _isAuthenticationCompactWindowMode = false;
        _preAuthenticationWindowLayout = null;

        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        ResizeMode = ResizeMode.NoResize;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        MinWidth = 0;
        MinHeight = 0;

        ShellTitleBar.Visibility = Visibility.Collapsed;
        ShellTitleBarRow.Height = new GridLength(0);
        ManagementContentHost.Visibility = Visibility.Collapsed;

        if (ShouldUseWindowTransitionAnimations())
        {
            await Task.WhenAll(
                AnimateWindowDoubleAsync(System.Windows.Window.LeftProperty, target.Left, 260),
                AnimateWindowDoubleAsync(FrameworkElement.WidthProperty, target.Width, 260)
            );
            await Task.WhenAll(
                AnimateWindowDoubleAsync(System.Windows.Window.TopProperty, target.Top, 180),
                AnimateWindowDoubleAsync(FrameworkElement.HeightProperty, target.Height, 180)
            );
        }
        else
        {
            Left = target.Left;
            Top = target.Top;
            Width = target.Width;
            Height = target.Height;
        }

        ApplyMainWindowModeChrome();
        RefreshShellDockPopupPlacements();
        await Dispatcher.Yield(DispatcherPriority.Render);
        return target.WindowState;
    }

    private WindowLayoutSnapshot ResolveAuthenticationExitLayout()
    {
        if (_preAuthenticationWindowLayout is { } layout)
        {
            return new WindowLayoutSnapshot(
                IsFiniteWindowValue(layout.Left) ? layout.Left : ResolveDefaultMainWindowLeft(),
                IsFiniteWindowValue(layout.Top) ? layout.Top : ResolveDefaultMainWindowTop(),
                NormalizeWindowLength(layout.Width, MainWindowMinWidth, MainWindowDefaultWidth),
                NormalizeWindowLength(layout.Height, MainWindowMinHeight, MainWindowDefaultHeight),
                layout.WindowState
            );
        }

        return new WindowLayoutSnapshot(
            ResolveDefaultMainWindowLeft(),
            ResolveDefaultMainWindowTop(),
            MainWindowDefaultWidth,
            MainWindowDefaultHeight,
            WindowState.Normal
        );
    }

    private static double ResolveDefaultMainWindowLeft()
    {
        var workArea = SystemParameters.WorkArea;
        return workArea.Left + Math.Max(0, (workArea.Width - MainWindowDefaultWidth) / 2);
    }

    private static double ResolveDefaultMainWindowTop()
    {
        var workArea = SystemParameters.WorkArea;
        return workArea.Top + Math.Max(0, (workArea.Height - MainWindowDefaultHeight) / 2);
    }

    private static bool ShouldUseWindowTransitionAnimations()
    {
        try
        {
            return SystemParameters.ClientAreaAnimation;
        }
        catch
        {
            return true;
        }
    }

    private Task<bool> AnimateWindowDoubleAsync(
        DependencyProperty property,
        double to,
        int milliseconds
    )
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var animation = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(milliseconds)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) =>
        {
            SetCurrentValue(property, to);
            completion.TrySetResult(true);
        };
        BeginAnimation(property, animation);
        return completion.Task;
    }

    private void ShowManagementEntryTransitionPopup()
    {
        UpdateManagementEntryTransitionStatus("正在打开管理界面");
        ManagementEntryTransitionOverlay.Opacity = 1;
        ManagementEntryTransitionPopup.IsOpen = true;
        RefreshManagementEntryTransitionPopupPlacement();
    }

    private void UpdateManagementEntryTransitionStatus(string description)
    {
        var normalized = description.Trim();
        ManagementEntryTransitionDetailText.Text = string.IsNullOrWhiteSpace(normalized)
            ? "正在接入本地服务"
            : normalized.TrimEnd('。');
    }

    private async Task PrepareManagementWebViewForStableNavigationAsync()
    {
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementContentHost.Visibility = Visibility.Visible;
        ManagementWebView.Visibility = Visibility.Hidden;
        SetNavigationDockPopupOpen(false);
        ManagementContentHost.UpdateLayout();
        ManagementWebView.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.Render);
        ManagementContentHost.UpdateLayout();
        ManagementWebView.UpdateLayout();
    }

    private async Task DispatchManagementWebViewResizeAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.Render);
        ManagementContentHost.UpdateLayout();
        ManagementWebView.UpdateLayout();

        if (!_webViewConfigured || ManagementWebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await ManagementWebView.CoreWebView2.ExecuteScriptAsync(
                "window.dispatchEvent(new Event('resize'));"
            );
        }
        catch (Exception exception)
        {
            _logger.Warn($"管理界面 resize 事件派发失败：{exception.Message}");
        }

        await Dispatcher.Yield(DispatcherPriority.Render);
    }

    private async Task CompleteManagementEntryTransitionAsync()
    {
        _shellConnectionStatus = "connected";
        UpdateShellConnectionPresentation();
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementContentHost.Visibility = Visibility.Visible;
        ManagementWebView.Visibility = Visibility.Visible;
        UpdateNavigationDockPopupVisibility();

        await FadeManagementEntryTransitionOverlayAsync(0, 220);
        ManagementEntryTransitionPopup.IsOpen = false;
        _isManagementEntryTransitionActive = false;
    }

    private Task FadeManagementEntryTransitionOverlayAsync(double opacity, int milliseconds)
    {
        if (!ManagementEntryTransitionPopup.IsOpen)
        {
            ManagementEntryTransitionOverlay.Opacity = opacity;
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var animation = CreateEaseAnimation(opacity, milliseconds);
        animation.FillBehavior = FillBehavior.Stop;
        animation.Completed += (_, _) =>
        {
            ManagementEntryTransitionOverlay.Opacity = opacity;
            completion.TrySetResult(true);
        };
        ManagementEntryTransitionOverlay.BeginAnimation(UIElement.OpacityProperty, animation);
        return completion.Task;
    }

    private void CloseManagementEntryTransitionImmediately()
    {
        _isManagementEntryTransitionActive = false;

        if (ManagementEntryTransitionOverlay is null || ManagementEntryTransitionPopup is null)
        {
            return;
        }

        ManagementEntryTransitionOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        ManagementEntryTransitionOverlay.Opacity = 0;
        ManagementEntryTransitionPopup.IsOpen = false;
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
        ExtendsContentIntoTitleBar = true;
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None;

        MainWindowChromeBehavior.ResizeBorderThickness = new Thickness(0);
        MainWindowChromeBehavior.CornerPreference =
            ControlzEx.Behaviors.WindowCornerPreference.DoNotRound;
        MainWindowChromeBehavior.UseNativeCaptionButtons = false;
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
        RefreshManagementEntryTransitionPopupPlacement();
        RefreshShellBrandDockPopupPlacement();
        RefreshNavigationDockPopupPlacement();
        RefreshShellNotificationPopupPlacements();
    }

    private void RefreshManagementEntryTransitionPopupPlacement()
    {
        if (ManagementEntryTransitionPopup is null || !ManagementEntryTransitionPopup.IsOpen)
        {
            return;
        }

        RefreshDockPopupPlacement(ManagementEntryTransitionPopup);
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
