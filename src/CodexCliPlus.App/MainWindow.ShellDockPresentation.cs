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
        ShellBrandDockScaleTransform.ScaleX = 1;
        ShellBrandDockScaleTransform.ScaleY = 0.88;
        ShellBrandDockTranslateTransform.Y = -8;

        ShellBrandDockCard.BeginAnimation(UIElement.OpacityProperty, CreateEaseAnimation(1, 150));
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
        ShellBrandDockCard.Opacity = 1;
        ShellBrandDockScaleTransform.ScaleY = 1;
        ShellBrandDockTranslateTransform.Y = 0;
        ShellBrandDockCard.BeginAnimation(UIElement.OpacityProperty, CreateEaseAnimation(0, 120));
        ShellBrandDockScaleTransform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            CreateEaseAnimation(0.88, 130)
        );
        ShellBrandDockTranslateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            CreateEaseAnimation(-8, 130)
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
        ShellBrandDockScaleTransform.ScaleX = 1;
        ShellBrandDockScaleTransform.ScaleY = 0.88;
        ShellBrandDockTranslateTransform.Y = -8;
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
        var panelOffset =
            state == NavigationDockVisualState.Resting
                ? NavigationDockPanelRestingOffset
                : NavigationDockPanelOpenOffset;
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
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
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
                ShellNavRuntimeOverviewButton,
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

        if (normalized.Equals("/dashboard", StringComparison.OrdinalIgnoreCase))
        {
            return "/";
        }

        return normalized.Equals("/console", StringComparison.OrdinalIgnoreCase)
            ? "/dashboard/overview"
            : normalized;
    }
}
