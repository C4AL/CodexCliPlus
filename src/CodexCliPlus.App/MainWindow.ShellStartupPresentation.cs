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
    private enum ManagementEntryTransitionPhase
    {
        Idle,
        ExpandingWindow,
        WelcomeFadeIn,
        LoadingWebView,
        WelcomeFadeOut,
    }

    private ManagementEntryTransitionPhase _managementEntryTransitionPhase =
        ManagementEntryTransitionPhase.Idle;

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

    private void PrepareHiddenAuthenticationStartupWindow()
    {
        _isAuthenticationCompactWindowMode = true;
        _preAuthenticationWindowLayout = null;
        Opacity = 0;
        ShowInTaskbar = false;

        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        ApplyAuthenticationCompactWindowChrome();
        CenterAuthenticationCompactWindow();
    }

    private void ShowPreparationStep(double progress, string description, StartupState state)
    {
        _startupState = state;
        if (_preparationPanelShownAt is null)
        {
            _preparationPanelShownAt = DateTimeOffset.UtcNow;
        }

        if (_isManagementEntryTransitionActive)
        {
            UpdateManagementEntryTransitionStatus(description);
        }

        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.Visibility =
            _isAuthenticationCompactWindowMode && !_isManagementEntryTransitionActive
                ? Visibility.Visible
                : Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementContentHost.Visibility =
            _isAuthenticationCompactWindowMode && !_isManagementEntryTransitionActive
                ? Visibility.Collapsed
                : Visibility.Visible;
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
        RevealInitialPresentation();
    }

    private void ShowFirstRunKeyReveal()
    {
        CloseManagementEntryTransitionImmediately();
        _startupState = StartupState.FirstRunKeyReveal;
        EnterAuthenticationCompactWindowMode();
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.ShowFirstRunKey(_firstRunManagementKey);
        StartupFlow.Visibility = Visibility.Visible;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
        RevealInitialPresentation();
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
        RevealInitialPresentation();
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
        RevealInitialPresentation();
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
        RevealInitialPresentation();
    }

    private void RevealInitialPresentation()
    {
        if (_initialPresentationRevealed)
        {
            return;
        }

        _initialPresentationRevealed = true;
        Opacity = 1;
        ShowInTaskbar = true;
    }

    private static bool ShouldUseManagementEntryTransition()
    {
        return true;
    }

    private async Task BeginManagementEntryTransitionAsync()
    {
        var shouldExpandFromAuthentication = _isAuthenticationCompactWindowMode;
        _isManagementEntryTransitionActive = true;
        SetManagementEntryTransitionPhase(
            shouldExpandFromAuthentication
                ? ManagementEntryTransitionPhase.ExpandingWindow
                : ManagementEntryTransitionPhase.WelcomeFadeIn
        );
        _preparationPanelShownAt = DateTimeOffset.UtcNow;
        CloseShellDockPopups();
        SetNavigationDockPopupOpen(false);

        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        ResetManagementEntryTransitionWelcomeVisuals();
        ManagementEntryTransitionPopup.IsOpen = false;

        if (shouldExpandFromAuthentication)
        {
            var targetState = await RestoreMainWindowForManagementEntryTransitionAsync();
            if (targetState == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
                RefreshManagementEntryTransitionPopupPlacement();
            }
        }
        else
        {
            ApplyMainWindowModeChrome();
            ManagementContentHost.Visibility = Visibility.Visible;
            await Dispatcher.Yield(DispatcherPriority.Render);
        }

        await ShowManagementEntryTransitionPopupAsync();
        RevealInitialPresentation();
        SetManagementEntryTransitionPhase(ManagementEntryTransitionPhase.LoadingWebView);
    }

    private async Task<WindowState> RestoreMainWindowForManagementEntryTransitionAsync()
    {
        var target = ResolveCenteredManagementEntryTargetLayout(ResolveAuthenticationExitLayout());
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
            await AnimateWindowHorizontalExpansionFromCenterAsync(target, 260);
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

    private WindowLayoutSnapshot ResolveCenteredManagementEntryTargetLayout(
        WindowLayoutSnapshot target
    )
    {
        var expansionCenterX = Left + Width / 2;
        var centeredLeft = expansionCenterX - target.Width / 2;
        return new WindowLayoutSnapshot(
            centeredLeft,
            target.Top,
            target.Width,
            target.Height,
            target.WindowState
        );
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
        var animation = new DoubleAnimation(
            to,
            new Duration(TimeSpan.FromMilliseconds(milliseconds))
        )
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

    private async Task AnimateWindowHorizontalExpansionFromCenterAsync(
        WindowLayoutSnapshot target,
        int milliseconds
    )
    {
        var expansionCenterX = Left + Width / 2;
        var centeredTargetLeft = expansionCenterX - target.Width / 2;
        await Task.WhenAll(
            AnimateWindowDoubleAsync(
                System.Windows.Window.LeftProperty,
                centeredTargetLeft,
                milliseconds
            ),
            AnimateWindowDoubleAsync(FrameworkElement.WidthProperty, target.Width, milliseconds)
        );
    }

    private async Task ShowManagementEntryTransitionPopupAsync()
    {
        SetManagementEntryTransitionPhase(ManagementEntryTransitionPhase.WelcomeFadeIn);
        UpdateManagementEntryTransitionStatus("正在打开管理界面");
        ResetManagementEntryTransitionWelcomeVisuals();
        ManagementEntryTransitionPopup.IsOpen = true;
        RefreshManagementEntryTransitionPopupPlacement();
        await Dispatcher.Yield(DispatcherPriority.Render);
        await Task.WhenAll(
            FadeManagementEntryTransitionOverlayAsync(1, 180),
            FadeManagementEntryTransitionWelcomeAsync(1, 0, 220)
        );
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
        SetManagementEntryTransitionPhase(ManagementEntryTransitionPhase.LoadingWebView);
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
        SetManagementEntryTransitionPhase(ManagementEntryTransitionPhase.WelcomeFadeOut);
        _shellConnectionStatus = "connected";
        UpdateShellConnectionPresentation();
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        StartupFlow.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementContentHost.Visibility = Visibility.Visible;
        ManagementWebView.Visibility = Visibility.Visible;
        ManagementContentHost.UpdateLayout();
        ManagementWebView.UpdateLayout();
        UpdateNavigationDockPopupVisibility();
        await Dispatcher.Yield(DispatcherPriority.Render);

        await Task.WhenAll(
            FadeManagementEntryTransitionOverlayAsync(0, 220),
            FadeManagementEntryTransitionWelcomeAsync(0, -6, 180)
        );
        ManagementEntryTransitionPopup.IsOpen = false;
        _isManagementEntryTransitionActive = false;
        SetManagementEntryTransitionPhase(ManagementEntryTransitionPhase.Idle);
    }

    private Task FadeManagementEntryTransitionOverlayAsync(double opacity, int milliseconds)
    {
        if (!ShouldUseWindowTransitionAnimations())
        {
            ManagementEntryTransitionOverlay.Opacity = opacity;
            return Task.CompletedTask;
        }

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

    private async Task FadeManagementEntryTransitionWelcomeAsync(
        double opacity,
        double translateY,
        int milliseconds
    )
    {
        if (!ShouldUseWindowTransitionAnimations())
        {
            ManagementEntryTransitionWelcomePanel.Opacity = opacity;
            ManagementEntryTransitionWelcomeTranslateTransform.Y = translateY;
            return;
        }

        await Task.WhenAll(
            AnimateTransitionVisualAsync(
                ManagementEntryTransitionWelcomePanel,
                UIElement.OpacityProperty,
                opacity,
                milliseconds
            ),
            AnimateTransitionVisualAsync(
                ManagementEntryTransitionWelcomeTranslateTransform,
                TranslateTransform.YProperty,
                translateY,
                milliseconds
            )
        );
    }

    private static Task<bool> AnimateTransitionVisualAsync(
        DependencyObject visual,
        DependencyProperty property,
        double to,
        int milliseconds
    )
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var animation = new DoubleAnimation(
            to,
            new Duration(TimeSpan.FromMilliseconds(milliseconds))
        )
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) =>
        {
            visual.SetValue(property, to);
            completion.TrySetResult(true);
        };
        if (visual is UIElement element)
        {
            element.BeginAnimation(property, animation);
        }
        else if (visual is Animatable animatable)
        {
            animatable.BeginAnimation(property, animation);
        }
        else
        {
            visual.SetValue(property, to);
            completion.TrySetResult(true);
        }

        return completion.Task;
    }

    private void ResetManagementEntryTransitionWelcomeVisuals()
    {
        ManagementEntryTransitionOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        ManagementEntryTransitionWelcomePanel.BeginAnimation(UIElement.OpacityProperty, null);
        ManagementEntryTransitionWelcomeTranslateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            null
        );
        ManagementEntryTransitionOverlay.Opacity = 0;
        ManagementEntryTransitionWelcomePanel.Opacity = 0;
        ManagementEntryTransitionWelcomeTranslateTransform.Y = 10;
    }

    private void SetManagementEntryTransitionPhase(ManagementEntryTransitionPhase phase)
    {
        _managementEntryTransitionPhase = phase;
    }

    private void CloseManagementEntryTransitionImmediately()
    {
        _isManagementEntryTransitionActive = false;
        SetManagementEntryTransitionPhase(ManagementEntryTransitionPhase.Idle);

        if (ManagementEntryTransitionOverlay is null || ManagementEntryTransitionPopup is null)
        {
            return;
        }

        ManagementEntryTransitionOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        ManagementEntryTransitionWelcomePanel.BeginAnimation(UIElement.OpacityProperty, null);
        ManagementEntryTransitionWelcomeTranslateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            null
        );
        ManagementEntryTransitionOverlay.Opacity = 0;
        ManagementEntryTransitionWelcomePanel.Opacity = 0;
        ManagementEntryTransitionWelcomeTranslateTransform.Y = 10;
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
        ApplyAuthenticationCompactWindowChrome();
        CenterAuthenticationCompactWindow();
    }

    private void ApplyAuthenticationCompactWindowChrome()
    {
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
        MainWindowChromeBehavior.CornerPreference = ControlzEx
            .Behaviors
            .WindowCornerPreference
            .DoNotRound;
        MainWindowChromeBehavior.UseNativeCaptionButtons = false;
        MainWindowGlowBehavior.GlowDepth = 0;

        ShellTitleBar.Visibility = Visibility.Collapsed;
        ShellTitleBarRow.Height = new GridLength(0);
        ManagementContentHost.Visibility = Visibility.Collapsed;
    }

    private void ExitAuthenticationCompactWindowMode()
    {
        ApplyMainWindowModeChrome();

        if (!_isAuthenticationCompactWindowMode)
        {
            return;
        }

        _isAuthenticationCompactWindowMode = false;
        var windowLayout = ResolveAuthenticationExitLayout();
        _preAuthenticationWindowLayout = null;

        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        Width = windowLayout.Width;
        Height = windowLayout.Height;
        if (IsFiniteWindowValue(windowLayout.Left) && IsFiniteWindowValue(windowLayout.Top))
        {
            Left = windowLayout.Left;
            Top = windowLayout.Top;
        }

        if (windowLayout.WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
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
        MainWindowChromeBehavior.CornerPreference = ControlzEx
            .Behaviors
            .WindowCornerPreference
            .Round;
        MainWindowChromeBehavior.UseNativeCaptionButtons = false;
        MainWindowGlowBehavior.GlowDepth = 0;

        ShellTitleBarRow.Height = new GridLength(ShellTitleBarHeight);
        ShellTitleBar.Visibility = Visibility.Visible;
        ManagementContentHost.Visibility = Visibility.Visible;
    }

    private WindowLayoutSnapshot CaptureCurrentWindowLayout()
    {
        var state = WindowState;
        var bounds =
            state == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
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
        Top = workArea.Top + Math.Max(0, (workArea.Height - AuthenticationCompactWindowHeight) / 2);
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
        UpdateNavigationDockPanelHeightForCurrentState();
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
