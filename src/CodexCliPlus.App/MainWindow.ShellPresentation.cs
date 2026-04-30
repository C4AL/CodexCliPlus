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

    private void ShowPreparationStep(
        string step,
        double progress,
        string description,
        StartupState state
    )
    {
        _startupState = state;
        if (LoadingPanel.Visibility != Visibility.Visible || _preparationPanelShownAt is null)
        {
            _preparationPanelShownAt = DateTimeOffset.UtcNow;
        }

        LoadingTitleText.Text =
            state == StartupState.LoadingManagement ? "正在进入管理界面" : "正在准备桌面管理界面";
        LoadingDescriptionText.Text = description;
        LoadingStatusText.Text = BuildPreparationStatus(progress, state);
        PreparationStepText.Text = $"当前步骤：{step}";
        var normalizedProgress = Math.Clamp(progress, 0, 100);
        PreparationProgressBar.BeginAnimation(
            RangeBase.ValueProperty,
            new DoubleAnimation(normalizedProgress, new Duration(TimeSpan.FromMilliseconds(320)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            }
        );
        PreparationProgressFillScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(
                normalizedProgress / 100,
                new Duration(TimeSpan.FromMilliseconds(420))
            )
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            }
        );

        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
    }

    private void ShowUpgradeNotice()
    {
        _startupState = StartupState.UpgradeNotice;
        _preparationPanelShownAt = null;
        var previousVersion = string.IsNullOrWhiteSpace(_settings.LastSeenApplicationVersion)
            ? "旧版本"
            : _settings.LastSeenApplicationVersion.Trim();
        UpgradeNoticeVersionText.Text =
            $"已从 {previousVersion} 升级到 {CurrentApplicationVersion}";

        UpgradeNoticePanel.Visibility = Visibility.Visible;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
    }

    private void ShowFirstRunKeyReveal()
    {
        _startupState = StartupState.FirstRunKeyReveal;
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        FirstRunKeyPanel.Visibility = Visibility.Visible;
        LoginPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
        FirstRunSecurityKeyTextBox.Focus();
    }

    private void ShowLogin(string? errorMessage = null)
    {
        _startupState = StartupState.NativeLogin;
        _shellConnectionStatus = "disconnected";
        UpdateShellConnectionPresentation();
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
        LoginButton.IsEnabled = true;
        ForgotSecurityKeyButton.IsEnabled = true;
        RememberManagementKeyCheckBox.IsChecked = _settings.RememberManagementKey;
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            LoginErrorText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ShowLoginError(errorMessage);
        }

        ManagementKeyPasswordBox.Focus();
    }

    private void ShowLoginError(string message)
    {
        LoginErrorText.Text = message;
        LoginErrorText.Visibility = Visibility.Visible;
    }

    private void ShowBlocker(string title, string description, string detail)
    {
        _startupState = StartupState.Blocked;
        _shellConnectionStatus = "error";
        UpdateShellConnectionPresentation();
        _preparationPanelShownAt = null;
        BlockerTitleText.Text = title;
        BlockerDescriptionText.Text = description;
        BlockerDetailText.Text = detail;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Collapsed;
        SetNavigationDockPopupOpen(false);
    }

    private void ShowWebView()
    {
        _shellConnectionStatus = "connected";
        UpdateShellConnectionPresentation();
        _preparationPanelShownAt = null;
        UpgradeNoticePanel.Visibility = Visibility.Collapsed;
        FirstRunKeyPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        BlockerPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ManagementWebView.Visibility = Visibility.Visible;
        UpdateNavigationDockPopupVisibility();
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

        ShellNavigationDockPopup.IsOpen = false;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (CanShowNavigationDockPopup())
                {
                    ShellNavigationDockPopup.IsOpen = true;
                }
            },
            DispatcherPriority.Background
        );
    }

    private void UpdateShellConnectionPresentation()
    {
        UpdateShellDockPresentation();

        if (_settingsOverlayOpen)
        {
            UpdateSettingsOverlayBaseline();
        }
    }

    private void UpdateShellDockPresentation()
    {
        var statusBrush = GetConnectionStatusBrush();
        ShellBrandStatusDot.Fill = statusBrush;
        ShellDockConnectionStatusDot.Fill = statusBrush;
        if (ShellBrandStatusDotGlow is DropShadowEffect glow)
        {
            glow.Color = GetConnectionStatusGlowColor();
        }

        ShellDockAppVersionText.Text = CurrentApplicationVersion;
        ShellDockCoreVersionText.Text = FormatUnknown(_shellBackendVersion);
        ShellDockConnectionStatusText.Text = ShellConnectionStatusLabel;
        ShellDockBackendAddressText.Text = string.IsNullOrWhiteSpace(_shellApiBase)
            ? "-"
            : _shellApiBase;
        ShellDockCopyBackendAddressButton.IsEnabled = !string.IsNullOrWhiteSpace(_shellApiBase);
    }

    private void AnimateShellBrandDockPopupIn()
    {
        _isShellBrandDockClosing = false;
        ShellBrandDockCard.Opacity = 0;
        ShellBrandDockScaleTransform.ScaleX = 0.98;
        ShellBrandDockScaleTransform.ScaleY = 0.98;
        ShellBrandDockTranslateTransform.Y = -6;

        ShellBrandDockCard.BeginAnimation(UIElement.OpacityProperty, CreateEaseAnimation(1, 150));
        ShellBrandDockScaleTransform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            CreateEaseAnimation(1, 180)
        );
        ShellBrandDockScaleTransform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            CreateEaseAnimation(1, 180)
        );
        ShellBrandDockTranslateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            CreateEaseAnimation(0, 180)
        );
    }

    private async Task HideShellBrandDockPopupAsync()
    {
        if (!ShellBrandDockPopup.IsOpen || _isShellBrandDockClosing)
        {
            return;
        }

        _isShellBrandDockClosing = true;
        ShellBrandDockCard.BeginAnimation(UIElement.OpacityProperty, CreateEaseAnimation(0, 120));
        ShellBrandDockScaleTransform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            CreateEaseAnimation(0.985, 130)
        );
        ShellBrandDockScaleTransform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            CreateEaseAnimation(0.985, 130)
        );
        ShellBrandDockTranslateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            CreateEaseAnimation(-5, 130)
        );

        await Task.Delay(TimeSpan.FromMilliseconds(135));
        ShellBrandDockPopup.IsOpen = false;
    }

    private void ResetShellBrandDockPopupVisual()
    {
        ShellBrandDockCard.BeginAnimation(UIElement.OpacityProperty, null);
        ShellBrandDockScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ShellBrandDockScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ShellBrandDockTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
        ShellBrandDockCard.Opacity = 0;
        ShellBrandDockScaleTransform.ScaleX = 0.98;
        ShellBrandDockScaleTransform.ScaleY = 0.98;
        ShellBrandDockTranslateTransform.Y = -6;
    }

    private SolidColorBrush GetConnectionStatusBrush()
    {
        return _shellConnectionStatus switch
        {
            "connected" => new SolidColorBrush(WpfColor.FromRgb(20, 184, 166)),
            "connecting" => new SolidColorBrush(WpfColor.FromRgb(245, 158, 11)),
            "error" => new SolidColorBrush(WpfColor.FromRgb(244, 63, 94)),
            _ => new SolidColorBrush(WpfColor.FromRgb(148, 163, 184)),
        };
    }

    private WpfColor GetConnectionStatusGlowColor()
    {
        return _shellConnectionStatus switch
        {
            "connected" => WpfColor.FromRgb(20, 184, 166),
            "connecting" => WpfColor.FromRgb(245, 158, 11),
            "error" => WpfColor.FromRgb(244, 63, 94),
            _ => WpfColor.FromRgb(148, 163, 184),
        };
    }

    private async Task SetShellThemeAsync(AppThemeMode themeMode)
    {
        _settings.ThemeMode = themeMode;
        ApplyShellTheme(themeMode);
        UpdateShellThemePresentation();
        await _appConfigurationService.SaveAsync(_settings);
        PostWebUiCommand(
            new
            {
                type = "setTheme",
                theme = ToWebTheme(themeMode),
                resolvedTheme = ToWebResolvedTheme(themeMode),
            }
        );
    }

    private void ApplyShellTheme(AppThemeMode themeMode)
    {
        var dark = IsEffectiveDarkTheme(themeMode);
        if (dark)
        {
            SetBrushResource("ApplicationBackgroundBrush", "#111317");
            SetBrushResource("SurfaceBrush", "#181B20");
            SetBrushResource("SurfaceAltBrush", "#23272F");
            SetBrushResource("AccentBrush", "#2DD4BF");
            SetBrushResource("AccentSoftBrush", "#123A36");
            SetBrushResource("PrimaryTextBrush", "#EEF2F7");
            SetBrushResource("SecondaryTextBrush", "#AAB4C0");
            SetBrushResource("BorderBrush", "#343A46");
            SetBrushResource("ShellDockGlassBrush", "#D01E242D");
            SetBrushResource("ShellDockGlassBorderBrush", "#55FFFFFF");
            SetBrushResource("ShellDockGlassHighlightBrush", "#75FFFFFF");
            SetBrushResource("NavigationDockPanelBrush", "#E61B2028");
            SetBrushResource("NavigationDockBorderBrush", "#60707A86");
            SetBrushResource("NavigationDockInnerHighlightBrush", "#3DFFFFFF");
            SetBrushResource("NavigationDockRailBrush", "#E68A96A3");
            SetBrushResource("NavigationDockRailTrackBrush", "#3039434F");
            SetBrushResource("NavigationDockButtonHoverBrush", "#34343C46");
        }
        else
        {
            SetBrushResource("ApplicationBackgroundBrush", "#F4F6FA");
            SetBrushResource("SurfaceBrush", "#FFFFFF");
            SetBrushResource("SurfaceAltBrush", "#EEF2F7");
            SetBrushResource("AccentBrush", "#0F766E");
            SetBrushResource("AccentSoftBrush", "#D9F1EE");
            SetBrushResource("PrimaryTextBrush", "#17202B");
            SetBrushResource("SecondaryTextBrush", "#526070");
            SetBrushResource("BorderBrush", "#D7DCE5");
            SetBrushResource("ShellDockGlassBrush", "#EAF8FAFC");
            SetBrushResource("ShellDockGlassBorderBrush", "#BFFFFFFF");
            SetBrushResource("ShellDockGlassHighlightBrush", "#FFFFFFFF");
            SetBrushResource("NavigationDockPanelBrush", "#F2F8FAFC");
            SetBrushResource("NavigationDockBorderBrush", "#A6CBD5E1");
            SetBrushResource("NavigationDockInnerHighlightBrush", "#FFFFFFFF");
            SetBrushResource("NavigationDockRailBrush", "#D9707884");
            SetBrushResource("NavigationDockRailTrackBrush", "#35CBD5E1");
            SetBrushResource("NavigationDockButtonHoverBrush", "#E6E9EEF5");
        }
    }

    private void UpdateShellThemePresentation()
    {
        _shellTheme = ToWebTheme(_settings.ThemeMode);
        _shellResolvedTheme = ToWebResolvedTheme(_settings.ThemeMode);
        var label = _settings.ThemeMode switch
        {
            AppThemeMode.White => "纯白",
            AppThemeMode.Dark => "暗色",
            _ => "跟随系统",
        };

        ShellThemeButton.ToolTip = $"主题：{label}";
        _suppressFollowSystemChange = true;
        SettingsFollowSystemCheckBox.IsChecked = _settings.ThemeMode == AppThemeMode.System;
        _suppressFollowSystemChange = false;
    }

    private void ShellNavigationDockHost_MouseEnter(
        object sender,
        System.Windows.Input.MouseEventArgs e
    )
    {
        if (!CanShowNavigationDockPopup())
        {
            SetNavigationDockPopupOpen(false);
            return;
        }

        if (IsPointerInDockEdgeIntent(e))
        {
            ShowNavigationDockIconsFromEdgeIntent();
        }
    }

    private void ShellNavigationDockHost_MouseMove(
        object sender,
        System.Windows.Input.MouseEventArgs e
    )
    {
        if (!CanShowNavigationDockPopup())
        {
            SetNavigationDockPopupOpen(false);
            return;
        }

        if (_navigationDockState == NavigationDockVisualState.Expanded)
        {
            return;
        }

        if (_navigationDockState == NavigationDockVisualState.Icons || IsPointerInDockEdgeIntent(e))
        {
            ExpandNavigationDock(showLabels: ShellNavigationPanel.IsMouseOver);
        }
    }

    private void ShellNavigationPanel_MouseEnter(
        object sender,
        System.Windows.Input.MouseEventArgs e
    )
    {
        if (!CanShowNavigationDockPopup())
        {
            SetNavigationDockPopupOpen(false);
            return;
        }

        ExpandNavigationDock(showLabels: true);
    }

    private void ShellNavigationPanel_MouseLeave(
        object sender,
        System.Windows.Input.MouseEventArgs e
    )
    {
        if (ShellNavigationDockHost.IsMouseOver)
        {
            ExpandNavigationDock(showLabels: false);
        }
    }

    private void ShellNavigationDockHost_MouseLeave(
        object sender,
        System.Windows.Input.MouseEventArgs e
    )
    {
        CollapseNavigationDockWithDelay(shortDelay: false);
    }

    private void NavigationDockCollapseTimer_Tick(object? sender, EventArgs e)
    {
        _navigationDockCollapseTimer.Stop();
        if (!CanShowNavigationDockPopup())
        {
            SetNavigationDockPopupOpen(false);
            return;
        }

        if (ShellNavigationDockHost.IsMouseOver)
        {
            ExpandNavigationDock(showLabels: ShellNavigationPanel.IsMouseOver);
            return;
        }

        AnimateNavigationDock(NavigationDockVisualState.Resting);
    }

    private void ExpandNavigationDock(bool showLabels)
    {
        if (!CanShowNavigationDockPopup())
        {
            SetNavigationDockPopupOpen(false);
            return;
        }

        _navigationDockCollapseTimer.Stop();
        AnimateNavigationDock(
            showLabels ? NavigationDockVisualState.Expanded : NavigationDockVisualState.Icons
        );
    }

    private void ShowNavigationDockIconsFromEdgeIntent()
    {
        if (!CanShowNavigationDockPopup())
        {
            SetNavigationDockPopupOpen(false);
            return;
        }

        ExpandNavigationDock(showLabels: false);
    }

    private static bool IsPointerInputActive()
    {
        return Mouse.LeftButton == MouseButtonState.Pressed
            || Mouse.RightButton == MouseButtonState.Pressed
            || Mouse.MiddleButton == MouseButtonState.Pressed
            || Mouse.XButton1 == MouseButtonState.Pressed
            || Mouse.XButton2 == MouseButtonState.Pressed;
    }

    private bool IsPointerInDockEdgeIntent(System.Windows.Input.MouseEventArgs e)
    {
        if (IsPointerInputActive())
        {
            return false;
        }

        var point = e.GetPosition(ShellNavigationDockHost);
        return point.X >= 0 && point.X <= NavigationDockEdgeIntentWidth;
    }

    private void CollapseNavigationDockWithDelay(bool shortDelay)
    {
        if (!CanShowNavigationDockPopup())
        {
            SetNavigationDockPopupOpen(false);
            return;
        }

        _navigationDockCollapseTimer.Stop();
        _navigationDockCollapseTimer.Interval = TimeSpan.FromMilliseconds(shortDelay ? 110 : 170);
        _navigationDockCollapseTimer.Start();
    }

    private bool CanShowNavigationDockPopup()
    {
        return _isMainWindowActive
            && IsVisible
            && WindowState != WindowState.Minimized
            && IsNavigationDockInitialized()
            && ManagementWebView.Visibility == Visibility.Visible
            && !_settingsOverlayOpen;
    }

    private bool IsNavigationDockInitialized()
    {
        return ShellNavigationDockPopup is not null
            && ShellNavigationDockHost is not null
            && ShellNavigationPanel is not null
            && ManagementWebView is not null;
    }

    private void CloseShellDockPopups()
    {
        if (!IsNavigationDockInitialized())
        {
            return;
        }

        _navigationDockCollapseTimer.Stop();
        AnimateNavigationDock(NavigationDockVisualState.Resting);
        if (ShellNavigationDockPopup.IsOpen)
        {
            ShellNavigationDockPopup.IsOpen = false;
        }

        if (ShellBrandDockPopup.IsOpen)
        {
            _isShellBrandDockClosing = false;
            ShellBrandDockPopup.IsOpen = false;
            ResetShellBrandDockPopupVisual();
        }
    }

    private void AnimateNavigationDock(NavigationDockVisualState state)
    {
        if (_navigationDockState == state)
        {
            ShellNavigationPanel.IsHitTestVisible = state != NavigationDockVisualState.Resting;
            ApplyNavigationDockLabelState(
                expanded: state == NavigationDockVisualState.Expanded,
                durationMilliseconds: 0
            );
            return;
        }

        _navigationDockState = state;
        var expandedLabelWidth = ResolveNavigationDockLabelExpandedWidth();
        var panelExpandedWidth = NavigationDockPanelIconsWidth + expandedLabelWidth + 20;
        var hostExpandedWidth = Math.Min(NavigationDockExpandedWidth, panelExpandedWidth + 54);
        var hostWidth = state switch
        {
            NavigationDockVisualState.Expanded => hostExpandedWidth,
            NavigationDockVisualState.Icons => NavigationDockIconsWidth,
            _ => NavigationDockRestingWidth,
        };
        var panelWidth =
            state == NavigationDockVisualState.Expanded
                ? Math.Min(NavigationDockPanelExpandedWidth, panelExpandedWidth)
                : NavigationDockPanelIconsWidth;
        var panelHeight =
            state == NavigationDockVisualState.Resting
                ? NavigationDockPanelRestingHeight
                : NavigationDockPanelOpenHeight;
        var panelOpacity = state == NavigationDockVisualState.Resting ? 0 : 1;
        var railOpacity = state == NavigationDockVisualState.Resting ? 1 : 0;
        var railTrackOpacity = state == NavigationDockVisualState.Resting ? 0.58 : 0;
        var panelOffset = state == NavigationDockVisualState.Resting ? -6 : 0;
        var duration = state == NavigationDockVisualState.Resting ? 130 : 240;
        var panelDuration = state == NavigationDockVisualState.Resting ? 110 : 210;
        ShellNavigationPanel.IsHitTestVisible = state != NavigationDockVisualState.Resting;
        ApplyNavigationDockLabelState(
            expanded: state == NavigationDockVisualState.Expanded,
            durationMilliseconds: panelDuration
        );
        ShellNavigationDockHost.BeginAnimation(
            FrameworkElement.WidthProperty,
            new DoubleAnimation(hostWidth, new Duration(TimeSpan.FromMilliseconds(duration)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            }
        );
        ShellNavigationPanel.BeginAnimation(
            FrameworkElement.WidthProperty,
            new DoubleAnimation(panelWidth, new Duration(TimeSpan.FromMilliseconds(duration)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            }
        );
        ShellNavigationPanel.BeginAnimation(
            FrameworkElement.HeightProperty,
            new DoubleAnimation(panelHeight, new Duration(TimeSpan.FromMilliseconds(duration)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            }
        );
        ShellNavigationPanel.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(
                panelOpacity,
                new Duration(TimeSpan.FromMilliseconds(panelDuration))
            )
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            }
        );
        ShellNavigationRail.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(railOpacity, new Duration(TimeSpan.FromMilliseconds(panelDuration)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            }
        );
        ShellNavigationRailTrack.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(
                railTrackOpacity,
                new Duration(TimeSpan.FromMilliseconds(panelDuration))
            )
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            }
        );
        if (ShellNavigationPanel.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(panelOffset, new Duration(TimeSpan.FromMilliseconds(duration)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                }
            );
        }
    }

    private void ApplyNavigationDockLabelState(bool expanded, int durationMilliseconds)
    {
        var expandedLabelWidth = ResolveNavigationDockLabelExpandedWidth();
        foreach (var button in ShellNavigationItemsHost.Children.OfType<WpfButton>())
        {
            button.HorizontalContentAlignment = expanded
                ? System.Windows.HorizontalAlignment.Left
                : System.Windows.HorizontalAlignment.Center;
        }

        foreach (var label in FindVisualChildren<TextBlock>(ShellNavigationItemsHost))
        {
            label.Margin = expanded ? new Thickness(14, 0, 0, 0) : new Thickness(0);
            if (!expanded || durationMilliseconds <= 0)
            {
                label.BeginAnimation(FrameworkElement.WidthProperty, null);
                label.BeginAnimation(UIElement.OpacityProperty, null);
                label.Width = expanded ? expandedLabelWidth : 0;
                label.Opacity = expanded ? 1 : 0;
                continue;
            }

            label.BeginAnimation(
                FrameworkElement.WidthProperty,
                new DoubleAnimation(
                    expanded ? expandedLabelWidth : 0,
                    new Duration(TimeSpan.FromMilliseconds(durationMilliseconds))
                )
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                }
            );
            label.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(
                    expanded ? 1 : 0,
                    new Duration(TimeSpan.FromMilliseconds(durationMilliseconds))
                )
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                }
            );
        }
    }

    private double ResolveNavigationDockLabelExpandedWidth()
    {
        var maxWidth = 0d;
        foreach (var label in FindVisualChildren<TextBlock>(ShellNavigationItemsHost))
        {
            label.Measure(
                new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity)
            );
            maxWidth = Math.Max(maxWidth, label.DesiredSize.Width);
        }

        if (maxWidth <= 0)
        {
            return NavigationDockLabelExpandedWidth;
        }

        return Math.Min(
            NavigationDockMeasuredLabelWidthLimit,
            Math.Max(NavigationDockLabelExpandedWidth, Math.Ceiling(maxWidth) + 4)
        );
    }

    private void UpdateNavigationActiveState()
    {
        foreach (
            var button in new[]
            {
                ShellNavDashboardButton,
                ShellNavConsoleButton,
                ShellNavConfigButton,
                ShellNavAccountButton,
                ShellNavAuthFilesButton,
                ShellNavQuotaButton,
                ShellNavUsageButton,
                ShellNavLogsButton,
            }
        )
        {
            if (button.CommandParameter is not string path)
            {
                continue;
            }

            button.Tag = IsRouteActive(path, _activeWebUiPath) ? "Active" : null;
        }
    }

    private static bool IsRouteActive(string route, string pathname)
    {
        var normalizedRoute = NormalizeRoutePath(route);
        var normalizedPath = NormalizeRoutePath(pathname);
        if (normalizedRoute == "/")
        {
            return normalizedPath == "/";
        }

        return normalizedPath.Equals(normalizedRoute, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoute + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRoutePath(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        if (normalized.Length > 1 && normalized.EndsWith('/'))
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized.Equals("/dashboard", StringComparison.OrdinalIgnoreCase)
            ? "/"
            : normalized;
    }

    private async Task ShowSettingsOverlayAsync()
    {
        if (_settingsOverlayOpen)
        {
            PositionSettingsWindow();
            _settingsWindow?.Activate();
            UpdateSettingsOverlayBaseline();
            return;
        }

        try
        {
            _settingsOverlayOpen = true;
            SetNavigationDockPopupOpen(false);
            UpdateSettingsOverlayBaseline();
            EnsureSettingsWindow();
            PositionSettingsWindow();
            SettingsOverlay.Visibility = Visibility.Collapsed;
            if (_settingsWindowRoot is not null)
            {
                _settingsWindowRoot.Opacity = 0;
            }

            if (SettingsDialogCard.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = 0.96;
                scale.ScaleY = 0.96;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateEaseAnimation(1, 180));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateEaseAnimation(1, 180));
            }

            _settingsWindow?.Show();
            _settingsWindow?.Activate();
            _settingsWindowRoot?.BeginAnimation(
                UIElement.OpacityProperty,
                CreateEaseAnimation(1, 180)
            );
            await RefreshSettingsOverlayAsync();
        }
        catch (Exception exception)
        {
            _settingsOverlayOpen = false;
            CloseSettingsWindow();
            UpdateNavigationDockPopupVisibility();
            _notificationService.ShowManual("设置打开失败", exception.Message);
        }
    }

    private async Task HideSettingsOverlayAsync()
    {
        if (!_settingsOverlayOpen && SettingsOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        _settingsOverlayOpen = false;
        CloseShellDockPopups();
        CancelSettingsOverviewRefresh();
        _settingsWindowRoot?.BeginAnimation(UIElement.OpacityProperty, CreateEaseAnimation(0, 140));
        if (SettingsDialogCard.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateEaseAnimation(0.96, 140));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateEaseAnimation(0.96, 140));
        }

        await Task.Delay(TimeSpan.FromMilliseconds(150));
        if (!_settingsOverlayOpen)
        {
            CloseSettingsWindow();
            UpdateNavigationDockPopupVisibility();
        }
    }

    private void EnsureSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            return;
        }

        if (SettingsDialogCard.Parent is System.Windows.Controls.Panel currentParent)
        {
            currentParent.Children.Remove(SettingsDialogCard);
        }

        _settingsWindowRoot = new Grid
        {
            Background = new SolidColorBrush(WpfColor.FromArgb(0x55, 0, 0, 0)),
            Opacity = 0,
        };
        _settingsWindowRoot.MouseLeftButtonDown += SettingsWindowRoot_MouseLeftButtonDown;
        _settingsWindowRoot.Children.Add(SettingsDialogCard);

        _settingsWindow = new Window
        {
            Owner = this,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Content = _settingsWindowRoot,
            Topmost = false,
        };
        _settingsWindow.Closed += SettingsWindow_Closed;
    }

    private async void SettingsWindowRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, _settingsWindowRoot))
        {
            await HideSettingsOverlayAsync();
        }
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        if (_settingsWindowRoot is not null)
        {
            _settingsWindowRoot.MouseLeftButtonDown -= SettingsWindowRoot_MouseLeftButtonDown;
            if (SettingsDialogCard.Parent == _settingsWindowRoot)
            {
                _settingsWindowRoot.Children.Remove(SettingsDialogCard);
            }
        }

        if (!SettingsOverlay.Children.Contains(SettingsDialogCard))
        {
            SettingsOverlay.Children.Add(SettingsDialogCard);
        }

        _settingsWindow = null;
        _settingsWindowRoot = null;
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void PositionSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        _settingsWindow.Left = Left;
        _settingsWindow.Top = Top;
        _settingsWindow.Width = Math.Max(ActualWidth, MinWidth);
        _settingsWindow.Height = Math.Max(ActualHeight, MinHeight);
    }

    private void CloseSettingsWindow()
    {
        CloseShellDockPopups();
        if (_settingsWindow is null)
        {
            return;
        }

        _settingsWindow.Closed -= SettingsWindow_Closed;
        var window = _settingsWindow;
        _settingsWindow = null;
        window.Content = null;

        if (_settingsWindowRoot is not null)
        {
            _settingsWindowRoot.MouseLeftButtonDown -= SettingsWindowRoot_MouseLeftButtonDown;
            if (SettingsDialogCard.Parent == _settingsWindowRoot)
            {
                _settingsWindowRoot.Children.Remove(SettingsDialogCard);
            }
        }

        if (!SettingsOverlay.Children.Contains(SettingsDialogCard))
        {
            SettingsOverlay.Children.Add(SettingsDialogCard);
        }

        _settingsWindowRoot = null;
        window.Close();
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private async Task RefreshShellDockOverviewAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var overview = await _managementOverviewService.GetShellStatusAsync(
                cancellationToken: cts.Token
            );
            _shellBackendVersion = ResolveBackendVersion(
                overview.Value.ServerVersion,
                overview.Metadata.Version
            );
        }
        catch
        {
            _shellBackendVersion = string.IsNullOrWhiteSpace(_shellBackendVersion)
                ? BackendReleaseMetadata.Version
                : _shellBackendVersion;
        }
        finally
        {
            UpdateShellDockPresentation();
        }
    }

    private async Task RefreshSettingsOverlayAsync()
    {
        CancelSettingsOverviewRefresh();
        var requestId = ++_settingsOverviewRefreshRequestId;
        _settingsOverviewRefreshCts = new CancellationTokenSource();
        var cancellationToken = _settingsOverviewRefreshCts.Token;

        UpdateSettingsOverlayBaseline();
        SettingsModelOverviewText.Text = "可用模型：未加载";
        SettingsProviderOverviewText.Text = "提供商概览：正在加载...";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    SettingsModelOverviewText.Text = "可用模型：未加载";
                    SettingsProviderOverviewText.Text = "提供商概览：正在刷新...";
                    await Task.Delay(TimeSpan.FromMilliseconds(420 * attempt), cancellationToken);
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
                var overview = await _managementOverviewService.GetSettingsSummaryAsync(
                    forceRefresh: attempt > 0,
                    cancellationToken: timeoutCts.Token
                );
                if (!IsCurrentSettingsOverviewRequest(requestId, cancellationToken))
                {
                    return;
                }

                _settingsOverview = overview.Value;
                var backendVersion = string.IsNullOrWhiteSpace(overview.Value.ServerVersion)
                    ? overview.Metadata.Version
                    : overview.Value.ServerVersion;
                _shellBackendVersion = ResolveBackendVersion(backendVersion, null);
                UpdateShellDockPresentation();
                SettingsModelOverviewText.Text = "可用模型：未加载";
                SettingsProviderOverviewText.Text =
                    $"代理密钥 {overview.Value.ApiKeyCount} / 认证文件 {overview.Value.AuthFileCount} / Gemini {overview.Value.GeminiKeyCount} / Codex {overview.Value.CodexKeyCount} / Claude {overview.Value.ClaudeKeyCount} / Vertex {overview.Value.VertexKeyCount} / OpenAI 兼容 {overview.Value.OpenAiCompatibilityCount}";

                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception) when (attempt < 2)
            {
                if (!IsCurrentSettingsOverviewRequest(requestId, cancellationToken))
                {
                    return;
                }

                SettingsModelOverviewText.Text = "可用模型：未加载";
                SettingsProviderOverviewText.Text = "提供商概览：正在重试";
            }
            catch (Exception)
            {
                if (!IsCurrentSettingsOverviewRequest(requestId, cancellationToken))
                {
                    return;
                }

                _settingsOverview = null;
                _shellBackendVersion = string.IsNullOrWhiteSpace(_shellBackendVersion)
                    ? BackendReleaseMetadata.Version
                    : _shellBackendVersion;
                UpdateShellDockPresentation();
                SettingsModelOverviewText.Text = "可用模型：未加载";
                SettingsProviderOverviewText.Text = "提供商概览：未加载";
                return;
            }
        }
    }

    private void CancelSettingsOverviewRefresh()
    {
        _settingsOverviewRefreshCts?.Cancel();
        _settingsOverviewRefreshCts?.Dispose();
        _settingsOverviewRefreshCts = null;
    }

    private bool IsCurrentSettingsOverviewRequest(
        int requestId,
        CancellationToken cancellationToken
    )
    {
        return _settingsOverlayOpen
            && !cancellationToken.IsCancellationRequested
            && requestId == _settingsOverviewRefreshRequestId;
    }

    private void UpdateSettingsOverlayBaseline()
    {
        UpdateShellThemePresentation();
        var persistence = _persistenceService.GetStatus();
        var fallbackSuffix = persistence.UsesFallbackDirectory
            ? "持久化目录已降级到可写应用数据目录。"
            : "持久化目录使用安装目录。";
        var keeperStatus = persistence.UsesKeeperDatabase
            ? $"统计库：{persistence.UsageEventCount} 条事件，最近同步：{FormatStatusTime(persistence.LastUsageSyncAt)}，数据库：{persistence.UsageDatabasePath}"
            : $"统计库不可用：{FormatUnknown(persistence.LastPersistenceError)}";
        SettingsUpdateStatusText.Text =
            $"当前版本：{CurrentApplicationVersion}。{fallbackSuffix}{Environment.NewLine}{keeperStatus}";
    }

    private string ShellConnectionStatusLabel =>
        _shellConnectionStatus switch
        {
            "connected" => "已连接",
            "connecting" => "连接中",
            "error" => "异常",
            _ => "未连接",
        };

    private void ShellNotificationService_NotificationRequested(
        object? sender,
        ShellNotificationRequest request
    )
    {
        Dispatcher.Invoke(() =>
        {
            if (request.Placement == ShellNotificationPlacement.BottomCenterAuto)
            {
                ShowAutoNotification(request.Message);
                return;
            }

            ShowManualNotification(request.Title, request.Message);
        });
    }

    private void ShowAutoNotification(string message)
    {
        var card = CreateNotificationCard(message, title: null, showCloseButton: false);
        card.Opacity = 0;
        card.RenderTransform = new TranslateTransform(0, 18);
        AutoNotificationStack.Children.Add(card);

        var progress = (Border)card.Tag;
        card.Loaded += async (_, _) =>
        {
            AnimateNotificationIn(card);
            progress.Width = card.ActualWidth;
            progress.BeginAnimation(
                FrameworkElement.WidthProperty,
                new DoubleAnimation(0, new Duration(TimeSpan.FromSeconds(2)))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                }
            );
            await Task.Delay(TimeSpan.FromSeconds(2.15));
            await FadeOutAndRemoveAsync(AutoNotificationStack, card);
        };
    }

    private void ShowManualNotification(string title, string message)
    {
        var card = CreateNotificationCard(message, title, showCloseButton: true);
        card.Opacity = 0;
        card.RenderTransform = new TranslateTransform(18, 0);
        ManualNotificationStack.Children.Add(card);
        card.Loaded += (_, _) => AnimateNotificationIn(card);
    }

    private Border CreateNotificationCard(string message, string? title, bool showCloseButton)
    {
        var accent = ResolveNotificationAccent(title, showCloseButton);
        var progress = new Border
        {
            Height = 2,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Background = new SolidColorBrush(accent),
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(
            progress,
            showCloseButton ? "ManualNotificationProgress" : "AutoNotificationProgress"
        );

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var contentGrid = new Grid { Margin = new Thickness(14, 12, 14, 12) };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentGrid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
        );
        if (showCloseButton)
        {
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        var iconText = new TextBlock
        {
            Text = showCloseButton ? "!" : "✓",
            Width = 26,
            Height = 26,
            Margin = new Thickness(0, 1, 10, 0),
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(accent),
            Background = new SolidColorBrush(WpfColor.FromArgb(0x18, accent.R, accent.G, accent.B)),
        };
        contentGrid.Children.Add(iconText);

        var textStack = new StackPanel();
        Grid.SetColumn(textStack, 1);
        if (!string.IsNullOrWhiteSpace(title))
        {
            textStack.Children.Add(
                new TextBlock
                {
                    Text = title,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (WpfBrush)FindResource("PrimaryTextBrush"),
                }
            );
        }

        var messageText = new TextBlock
        {
            Text = message,
            Margin = string.IsNullOrWhiteSpace(title)
                ? new Thickness(0)
                : new Thickness(0, 6, 0, 0),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (WpfBrush)FindResource("SecondaryTextBrush"),
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(
            messageText,
            showCloseButton ? "ManualNotificationMessage" : "AutoNotificationMessage"
        );
        System.Windows.Automation.AutomationProperties.SetName(messageText, message);
        textStack.Children.Add(messageText);
        contentGrid.Children.Add(textStack);

        if (showCloseButton)
        {
            var closeButton = new WpfButton
            {
                Content = "×",
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(10, -4, -6, 0),
                ToolTip = "关闭",
            };
            Grid.SetColumn(closeButton, 2);
            contentGrid.Children.Add(closeButton);
        }

        root.Children.Add(contentGrid);
        Grid.SetRow(progress, 1);
        root.Children.Add(progress);

        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Background = (WpfBrush)FindResource("SurfaceBrush"),
            BorderBrush = (WpfBrush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                Direction = 270,
                Color = WpfColor.FromArgb(0x22, accent.R, accent.G, accent.B),
                Opacity = 1,
                ShadowDepth = 2,
            },
            Child = root,
            Tag = progress,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(
            card,
            showCloseButton ? "ManualNotificationCard" : "AutoNotificationCard"
        );
        System.Windows.Automation.AutomationProperties.SetName(
            card,
            string.IsNullOrWhiteSpace(title) ? message : $"{title} {message}"
        );

        if (
            showCloseButton
            && contentGrid.Children.OfType<WpfButton>().FirstOrDefault() is { } button
        )
        {
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                button,
                "ManualNotificationCloseButton"
            );
            System.Windows.Automation.AutomationProperties.SetName(button, "关闭");
            button.Click += async (_, _) =>
                await FadeOutAndRemoveAsync(ManualNotificationStack, card);
        }

        return card;
    }

    private static WpfColor ResolveNotificationAccent(string? title, bool manual)
    {
        if (
            manual
            && (
                title?.Contains("失败", StringComparison.Ordinal) == true
                || title?.Contains("错误", StringComparison.Ordinal) == true
                || title?.Contains("不可用", StringComparison.Ordinal) == true
            )
        )
        {
            return WpfColor.FromRgb(225, 29, 72);
        }

        return manual ? WpfColor.FromRgb(245, 158, 11) : WpfColor.FromRgb(20, 184, 166);
    }

    private static void AnimateNotificationIn(Border card)
    {
        card.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(180)))
        );
        if (card.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(180)))
            );
            transform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(180)))
            );
        }
    }

    private static async Task FadeOutAndRemoveAsync(
        System.Windows.Controls.Panel owner,
        Border card
    )
    {
        if (!owner.Children.Contains(card))
        {
            return;
        }

        card.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(180)))
        );
        await Task.Delay(TimeSpan.FromMilliseconds(190));
        owner.Children.Remove(card);
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

    private void HideToTray()
    {
        CloseShellDockPopups();
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

    private bool IsUpgradeNoticePending()
    {
        return !string.Equals(
            _settings.LastSeenApplicationVersion?.Trim(),
            CurrentApplicationVersion,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private string CurrentApplicationVersion =>
        string.IsNullOrWhiteSpace(_buildInfo.ApplicationVersion)
            ? "当前版本"
            : _buildInfo.ApplicationVersion.Trim();

    private static string FormatUnknown(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "未知" : value.Trim();
    }

    private static string FormatStatusTime(DateTimeOffset? value)
    {
        return value is null
            ? "从未"
            : value
                .Value.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string ResolveBackendVersion(string? preferredVersion, string? fallbackVersion)
    {
        if (!string.IsNullOrWhiteSpace(preferredVersion))
        {
            return preferredVersion.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallbackVersion))
        {
            return fallbackVersion.Trim();
        }

        return BackendReleaseMetadata.Version;
    }

    private static string NormalizeConnectionStatus(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "connected" => "connected",
            "connecting" => "connecting",
            "error" => "error",
            _ => "disconnected",
        };
    }

    private static string NormalizeWebTheme(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "dark" => "dark",
            "white" or "light" => "white",
            _ => "auto",
        };
    }

    private static string NormalizeResolvedTheme(string? value)
    {
        return value?.Trim().ToLowerInvariant() == "dark" ? "dark" : "light";
    }

    private static string ToWebTheme(AppThemeMode themeMode)
    {
        return themeMode switch
        {
            AppThemeMode.White => "white",
            AppThemeMode.Dark => "dark",
            _ => "auto",
        };
    }

    private static string ToWebResolvedTheme(AppThemeMode themeMode)
    {
        return IsEffectiveDarkTheme(themeMode) ? "dark" : "light";
    }

    private static bool IsEffectiveDarkTheme(AppThemeMode themeMode)
    {
        return themeMode == AppThemeMode.Dark
            || (themeMode == AppThemeMode.System && IsSystemDarkTheme());
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
            );
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return false;
        }
    }

    private static DoubleAnimation CreateEaseAnimation(double to, int milliseconds)
    {
        return new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(milliseconds)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void SetThemeButtonState(WpfButton button, bool active)
    {
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        button.Opacity = active ? 1 : 0.72;
    }

    private void SetBrushResource(string key, string color)
    {
        if (WpfColorConverter.ConvertFromString(color) is WpfColor parsed)
        {
            Resources[key] = new SolidColorBrush(parsed);
            System.Windows.Application.Current.Resources[key] = new SolidColorBrush(parsed);
        }
    }

    private static void OpenExternal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (
            !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }
}
