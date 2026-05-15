using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
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
using Wpf.Ui.Appearance;
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

    private readonly record struct ShellThemePalette(
        string ApplicationBackground,
        string Surface,
        string SurfaceAlt,
        string Accent,
        string AccentSoft,
        string PrimaryText,
        string SecondaryText,
        string Border,
        string ShellDockGlass,
        string ShellDockGlassBorder,
        string ShellDockGlassHighlight,
        string NavigationDockPanel,
        string NavigationDockBorder,
        string NavigationDockInnerHighlight,
        string NavigationDockRail,
        string NavigationDockRailTrack,
        string NavigationDockButtonHover
    );

    private readonly record struct ShellThemeSnapshot(
        AppThemeMode ThemeMode,
        string WebTheme,
        string ResolvedTheme,
        bool Dark,
        int TransitionMilliseconds,
        ShellThemePalette Palette
    );

    private static readonly ShellThemePalette LightShellThemePalette = new(
        ApplicationBackground: "#FFFFFF",
        Surface: "#FFFFFF",
        SurfaceAlt: "#F6F6F6",
        Accent: "#8B8680",
        AccentSoft: "#EFEDEA",
        PrimaryText: "#2D2A26",
        SecondaryText: "#6D6760",
        Border: "#E5E5E5",
        ShellDockGlass: "#EAFFFFFF",
        ShellDockGlassBorder: "#BFFFFFFF",
        ShellDockGlassHighlight: "#FFFFFFFF",
        NavigationDockPanel: "#F2FFFFFF",
        NavigationDockBorder: "#A6D9D9D9",
        NavigationDockInnerHighlight: "#FFFFFFFF",
        NavigationDockRail: "#D98B8680",
        NavigationDockRailTrack: "#35D9D9D9",
        NavigationDockButtonHover: "#E6F6F6F6"
    );

    private static readonly ShellThemePalette DarkShellThemePalette = new(
        ApplicationBackground: "#151412",
        Surface: "#1D1B18",
        SurfaceAlt: "#262320",
        Accent: "#8B8680",
        AccentSoft: "#2E2A26",
        PrimaryText: "#F6F4F1",
        SecondaryText: "#C9C3BB",
        Border: "#3A3530",
        ShellDockGlass: "#D01D1B18",
        ShellDockGlassBorder: "#664A443D",
        ShellDockGlassHighlight: "#33FFFFFF",
        NavigationDockPanel: "#E61D1B18",
        NavigationDockBorder: "#704A443D",
        NavigationDockInnerHighlight: "#2EFFFFFF",
        NavigationDockRail: "#D99C958D",
        NavigationDockRailTrack: "#453A3530",
        NavigationDockButtonHover: "#402E2A26"
    );

    private async Task SetShellThemeAsync(AppThemeMode themeMode)
    {
        var themePersistenceVersion = Interlocked.Increment(ref _shellThemePersistenceVersion);
        var snapshot = CreateShellThemeSnapshot(
            themeMode,
            ShellThemeTransitionMilliseconds
        );

        _settings.ThemeMode = snapshot.ThemeMode;
        ApplyShellThemeSnapshot(snapshot, postWebCommand: true);
        await PersistLatestShellThemeSelectionAsync(themePersistenceVersion);
    }

    private async Task PersistLatestShellThemeSelectionAsync(long themePersistenceVersion)
    {
        await _shellThemePersistenceLock.WaitAsync();
        try
        {
            if (themePersistenceVersion != Volatile.Read(ref _shellThemePersistenceVersion))
            {
                return;
            }

            await _appConfigurationService.SaveAsync(_settings);
        }
        finally
        {
            _shellThemePersistenceLock.Release();
        }
    }

    private void PostShellThemeCommand(ShellThemeSnapshot snapshot)
    {
        PostWebUiCommand(
            new
            {
                type = "setTheme",
                theme = snapshot.WebTheme,
                resolvedTheme = snapshot.ResolvedTheme,
                transitionMs = snapshot.TransitionMilliseconds,
            }
        );
    }

    private void ApplyCurrentShellTheme(int transitionMilliseconds, bool postWebCommand)
    {
        var snapshot = CreateShellThemeSnapshot(_settings.ThemeMode, transitionMilliseconds);
        ApplyShellThemeSnapshot(snapshot, postWebCommand);
    }

    private static ShellThemeSnapshot CreateShellThemeSnapshot(
        AppThemeMode themeMode,
        int transitionMilliseconds
    )
    {
        var webTheme = ToWebTheme(themeMode);
        var resolvedTheme = ToWebResolvedTheme(themeMode);
        var dark = resolvedTheme == "dark";
        return new ShellThemeSnapshot(
            themeMode,
            webTheme,
            resolvedTheme,
            dark,
            transitionMilliseconds,
            dark ? DarkShellThemePalette : LightShellThemePalette
        );
    }

    private void ApplyShellThemeSnapshot(ShellThemeSnapshot snapshot, bool postWebCommand)
    {
        ApplyShellTheme(snapshot.Dark, snapshot.Palette, snapshot.TransitionMilliseconds);
        UpdateShellThemePresentation(snapshot);
        if (postWebCommand)
        {
            PostShellThemeCommand(snapshot);
        }
    }

    private void ApplyShellTheme(
        bool dark,
        ShellThemePalette palette,
        int transitionMilliseconds
    )
    {
        ApplicationThemeManager.Apply(
            dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            Wpf.Ui.Controls.WindowBackdropType.Auto,
            false
        );

        SetBrushResource(
            "ApplicationBackgroundBrush",
            palette.ApplicationBackground,
            transitionMilliseconds
        );
        SetBrushResource("SurfaceBrush", palette.Surface, transitionMilliseconds);
        SetBrushResource("SurfaceAltBrush", palette.SurfaceAlt, transitionMilliseconds);
        SetBrushResource("AccentBrush", palette.Accent, transitionMilliseconds);
        SetBrushResource("AccentSoftBrush", palette.AccentSoft, transitionMilliseconds);
        SetBrushResource("PrimaryTextBrush", palette.PrimaryText, transitionMilliseconds);
        SetBrushResource("SecondaryTextBrush", palette.SecondaryText, transitionMilliseconds);
        SetBrushResource("BorderBrush", palette.Border, transitionMilliseconds);
        SetBrushResource("ShellDockGlassBrush", palette.ShellDockGlass, transitionMilliseconds);
        SetBrushResource(
            "ShellDockGlassBorderBrush",
            palette.ShellDockGlassBorder,
            transitionMilliseconds
        );
        SetBrushResource(
            "ShellDockGlassHighlightBrush",
            palette.ShellDockGlassHighlight,
            transitionMilliseconds
        );
        SetBrushResource(
            "NavigationDockPanelBrush",
            palette.NavigationDockPanel,
            transitionMilliseconds
        );
        SetBrushResource(
            "NavigationDockBorderBrush",
            palette.NavigationDockBorder,
            transitionMilliseconds
        );
        SetBrushResource(
            "NavigationDockInnerHighlightBrush",
            palette.NavigationDockInnerHighlight,
            transitionMilliseconds
        );
        SetBrushResource(
            "NavigationDockRailBrush",
            palette.NavigationDockRail,
            transitionMilliseconds
        );
        SetBrushResource(
            "NavigationDockRailTrackBrush",
            palette.NavigationDockRailTrack,
            transitionMilliseconds
        );
        SetBrushResource(
            "NavigationDockButtonHoverBrush",
            palette.NavigationDockButtonHover,
            transitionMilliseconds
        );
    }

    private void UpdateShellThemePresentation(ShellThemeSnapshot snapshot)
    {
        var label = snapshot.ThemeMode switch
        {
            AppThemeMode.White => "纯白",
            AppThemeMode.Dark => "暗色",
            _ => "跟随系统",
        };

        ShellThemeButton.ToolTip = $"主题：{label}";
        _suppressFollowSystemChange = true;
        SettingsFollowSystemCheckBox.IsChecked = snapshot.ThemeMode == AppThemeMode.System;
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
            && ShellNavigationItemsHost is not null
            && ManagementContentHost is not null
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
            if (state != NavigationDockVisualState.Resting)
            {
                UpdateNavigationDockPanelHeightForCurrentState();
            }

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
                : ResolveNavigationDockPanelMeasuredOpenHeight();
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

    private void UpdateNavigationDockPanelHeightForCurrentState()
    {
        if (
            !IsNavigationDockInitialized()
            || _navigationDockState == NavigationDockVisualState.Resting
        )
        {
            return;
        }

        ShellNavigationPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
        ShellNavigationPanel.Height = ResolveNavigationDockPanelMeasuredOpenHeight();
    }

    private double ResolveNavigationDockPanelMeasuredOpenHeight()
    {
        var chromeHeight =
            ShellNavigationPanel.Padding.Top
            + ShellNavigationPanel.Padding.Bottom
            + ShellNavigationPanel.BorderThickness.Top
            + ShellNavigationPanel.BorderThickness.Bottom;
        var desiredHeight = Math.Max(
            NavigationDockPanelRestingHeight,
            Math.Ceiling(ResolveVisibleNavigationItemsHeight() + chromeHeight)
        );
        var availableHeight = ResolveNavigationDockPanelAvailableHeight();
        return IsFiniteLength(availableHeight)
            ? Math.Min(desiredHeight, availableHeight)
            : desiredHeight;
    }

    private double ResolveVisibleNavigationItemsHeight()
    {
        var measureWidth = Math.Max(
            NavigationDockPanelIconsWidth,
            NavigationDockPanelExpandedWidth
                - ShellNavigationPanel.Padding.Left
                - ShellNavigationPanel.Padding.Right
                - ShellNavigationPanel.BorderThickness.Left
                - ShellNavigationPanel.BorderThickness.Right
        );
        var contentHeight = 0d;
        foreach (var button in ShellNavigationItemsHost.Children.OfType<WpfButton>())
        {
            if (button.Visibility != Visibility.Visible)
            {
                continue;
            }

            button.Measure(new System.Windows.Size(measureWidth, double.PositiveInfinity));
            var buttonHeight = button.DesiredSize.Height;
            if (buttonHeight <= 0)
            {
                buttonHeight = button.ActualHeight + button.Margin.Top + button.Margin.Bottom;
            }

            contentHeight += buttonHeight;
        }

        if (contentHeight > 0)
        {
            return contentHeight;
        }

        ShellNavigationItemsHost.Measure(
            new System.Windows.Size(measureWidth, double.PositiveInfinity)
        );
        return ShellNavigationItemsHost.DesiredSize.Height;
    }

    private double ResolveNavigationDockPanelAvailableHeight()
    {
        var hostHeight = ManagementContentHost.ActualHeight;
        if (!IsPositiveFiniteLength(hostHeight))
        {
            hostHeight = ShellNavigationDockHost.ActualHeight;
        }

        if (!IsPositiveFiniteLength(hostHeight))
        {
            return double.PositiveInfinity;
        }

        var margin = ShellNavigationPanel.Margin;
        return Math.Max(0, Math.Floor(hostHeight - margin.Top - margin.Bottom));
    }

    private static bool IsPositiveFiniteLength(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsFiniteLength(double value)
    {
        return value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
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
                ShellNavDashboardOverviewButton,
                ShellNavCodexConfigButton,
                ShellNavConfigButton,
                ShellNavAccountButton,
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

        if (
            normalized.Equals("/ai-providers", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("/ai-providers/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/auth-files", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("/auth-files/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/oauth", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/quota", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "/accounts";
        }

        return normalized.Equals("/console", StringComparison.OrdinalIgnoreCase)
            ? "/dashboard/overview"
            : normalized;
    }
}
