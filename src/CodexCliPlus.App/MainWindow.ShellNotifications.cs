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
using WpfPanel = System.Windows.Controls.Panel;

namespace CodexCliPlus;

public partial class MainWindow
{
    private const int MaxVisibleShellNotifications = 3;
    private const double AutoNotificationBottomOffset = 28;
    private const double ManualNotificationRightOffset = 24;
    private const double ManualNotificationBottomOffset = 24;

    private readonly HashSet<Border> _removingShellNotifications = [];

    private void ShellNotificationService_NotificationRequested(
        object? sender,
        ShellNotificationRequest request
    )
    {
        Dispatcher.Invoke(() =>
        {
            if (request.Placement == ShellNotificationPlacement.BottomCenterAuto)
            {
                ShowAutoNotification(request.Message, request.Level);
                return;
            }

            ShowManualNotification(request.Title, request.Message, request.Level);
        });
    }

    private void ShowAutoNotification(string message, ShellNotificationLevel level)
    {
        var card = CreateNotificationCard(message, null, false, level);
        card.Opacity = 0;
        card.RenderTransform = new TranslateTransform(0, 18);
        EnforceShellNotificationCapacity(AutoNotificationStack);
        AutoNotificationStack.Children.Add(card);
        UpdateShellNotificationPopupVisibility();

        var progress = (Border)card.Tag;
        card.Loaded += Card_Loaded;

        async void Card_Loaded(object sender, RoutedEventArgs e)
        {
            card.Loaded -= Card_Loaded;
            UpdateShellNotificationPopupVisibility();
            AnimateNotificationIn(card);
            if (progress.RenderTransform is ScaleTransform progressScale)
            {
                progressScale.ScaleX = 1;
                progressScale.BeginAnimation(
                    ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0, new Duration(TimeSpan.FromSeconds(2)))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                    }
                );
            }

            await Task.Delay(TimeSpan.FromSeconds(2.15));
            await FadeOutAndRemoveAsync(AutoNotificationStack, card);
        }
    }

    private void ShowManualNotification(string title, string message, ShellNotificationLevel level)
    {
        var card = CreateNotificationCard(message, title, true, level);
        card.Opacity = 0;
        card.RenderTransform = new TranslateTransform(18, 0);
        EnforceShellNotificationCapacity(ManualNotificationStack);
        ManualNotificationStack.Children.Add(card);
        UpdateShellNotificationPopupVisibility();
        card.Loaded += Card_Loaded;

        void Card_Loaded(object sender, RoutedEventArgs e)
        {
            card.Loaded -= Card_Loaded;
            UpdateShellNotificationPopupVisibility();
            AnimateNotificationIn(card);
        }
    }

    private void EnforceShellNotificationCapacity(WpfPanel owner)
    {
        while (CountActiveShellNotifications(owner) >= MaxVisibleShellNotifications)
        {
            var oldest = owner
                .Children.OfType<Border>()
                .FirstOrDefault(card => !_removingShellNotifications.Contains(card));
            if (oldest is null)
            {
                return;
            }

            _ = FadeOutAndRemoveAsync(owner, oldest);
        }

        UpdateShellNotificationPopupVisibility();
    }

    private int CountActiveShellNotifications(WpfPanel owner)
    {
        return owner
            .Children.OfType<Border>()
            .Count(card => !_removingShellNotifications.Contains(card));
    }

    private Border CreateNotificationCard(
        string message,
        string? title,
        bool showCloseButton,
        ShellNotificationLevel level
    )
    {
        var accent = ResolveNotificationAccent(level, title, showCloseButton);
        var progress = new Border
        {
            Height = 2,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(accent),
            RenderTransformOrigin = new System.Windows.Point(1, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
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
            Text = ResolveNotificationIcon(level, showCloseButton),
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

        var contentCard = new Border
        {
            Background = (WpfBrush)FindResource("SurfaceBrush"),
            BorderBrush = (WpfBrush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Child = root,
        };

        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Background = (WpfBrush)FindResource("SurfaceBrush"),
            CornerRadius = new CornerRadius(10),
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                Direction = 270,
                Color = WpfColor.FromArgb(0x22, accent.R, accent.G, accent.B),
                Opacity = 1,
                ShadowDepth = 2,
            },
            Child = contentCard,
            Tag = progress,
        };
        card.SizeChanged += (_, _) => UpdateNotificationCardClip(card);
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

    private static void UpdateNotificationCardClip(Border card)
    {
        var clipTarget = card.Child as Border ?? card;
        if (clipTarget.ActualWidth <= 0 || clipTarget.ActualHeight <= 0)
        {
            return;
        }

        var radius = Math.Max(clipTarget.CornerRadius.TopLeft, clipTarget.CornerRadius.TopRight);
        radius = Math.Max(radius, clipTarget.CornerRadius.BottomLeft);
        radius = Math.Max(radius, clipTarget.CornerRadius.BottomRight);
        clipTarget.Clip = new RectangleGeometry(
            new Rect(0, 0, clipTarget.ActualWidth, clipTarget.ActualHeight),
            radius,
            radius
        );
    }

    private static string ResolveNotificationIcon(
        ShellNotificationLevel level,
        bool manual
    ) =>
        level switch
        {
            ShellNotificationLevel.Success => "✓",
            ShellNotificationLevel.Info => "i",
            ShellNotificationLevel.Warning => "!",
            ShellNotificationLevel.Error => "!",
            _ => manual ? "!" : "✓",
        };

    private static WpfColor ResolveNotificationAccent(
        ShellNotificationLevel level,
        string? title,
        bool manual
    )
    {
        return level switch
        {
            ShellNotificationLevel.Error => WpfColor.FromRgb(225, 29, 72),
            ShellNotificationLevel.Warning => WpfColor.FromRgb(245, 158, 11),
            ShellNotificationLevel.Success => WpfColor.FromRgb(22, 163, 74),
            ShellNotificationLevel.Info => WpfColor.FromRgb(20, 184, 166),
            _ => ResolveNotificationAccentFromLegacyTitle(title, manual),
        };
    }

    private static WpfColor ResolveNotificationAccentFromLegacyTitle(string? title, bool manual)
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

    private async Task FadeOutAndRemoveAsync(WpfPanel owner, Border card)
    {
        if (!owner.Children.Contains(card))
        {
            return;
        }

        if (!_removingShellNotifications.Add(card))
        {
            return;
        }

        try
        {
            card.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(180)))
            );
            await Task.Delay(TimeSpan.FromMilliseconds(190));
            if (owner.Children.Contains(card))
            {
                owner.Children.Remove(card);
            }
        }
        finally
        {
            _removingShellNotifications.Remove(card);
            UpdateShellNotificationPopupVisibility();
        }
    }

    private void ShellNotificationStack_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateShellNotificationPopupVisibility();
    }

    private void ManagementContentHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshShellDockPopupPlacements();
        UpdateSettingsOverlayPopupVisibility();
        UpdateShellNotificationPopupVisibility();
    }

    private void UpdateShellNotificationPopupVisibility()
    {
        if (
            AutoNotificationPopup is null
            || AutoNotificationStack is null
            || ManualNotificationPopup is null
            || ManualNotificationStack is null
        )
        {
            return;
        }

        UpdateShellNotificationPopup(
            AutoNotificationPopup,
            AutoNotificationStack,
            bottomCenter: true
        );
        UpdateShellNotificationPopup(
            ManualNotificationPopup,
            ManualNotificationStack,
            bottomCenter: false
        );
    }

    private void RefreshShellNotificationPopupPlacements()
    {
        UpdateShellNotificationPopupVisibility();
    }

    private void UpdateShellNotificationPopup(
        Popup popup,
        WpfPanel owner,
        bool bottomCenter
    )
    {
        var shouldOpen = owner.Children.Count > 0 && CanShowShellNotificationPopups();
        if (shouldOpen)
        {
            UpdateShellNotificationPopupPlacement(popup, owner, bottomCenter);
        }

        if (popup.IsOpen != shouldOpen)
        {
            popup.IsOpen = shouldOpen;
        }

        if (shouldOpen)
        {
            RefreshDockPopupPlacement(popup);
        }
    }

    private bool CanShowShellNotificationPopups()
    {
        return IsVisible
            && WindowState != WindowState.Minimized
            && ManagementContentHost is not null
            && ManagementContentHost.ActualWidth > 0
            && ManagementContentHost.ActualHeight > 0;
    }

    private void UpdateShellNotificationPopupPlacement(
        Popup popup,
        FrameworkElement owner,
        bool bottomCenter
    )
    {
        var hostWidth = ManagementContentHost.ActualWidth;
        var hostHeight = ManagementContentHost.ActualHeight;
        var ownerWidth = ResolvePopupElementWidth(owner);
        var ownerHeight = ResolvePopupElementHeight(owner);

        popup.HorizontalOffset = bottomCenter
            ? Math.Max(0, (hostWidth - ownerWidth) / 2)
            : Math.Max(0, hostWidth - ownerWidth - ManualNotificationRightOffset);
        popup.VerticalOffset = Math.Max(
            0,
            hostHeight
                - ownerHeight
                - (bottomCenter ? AutoNotificationBottomOffset : ManualNotificationBottomOffset)
        );
    }

    private static double ResolvePopupElementWidth(FrameworkElement element)
    {
        if (element.ActualWidth > 0)
        {
            return element.ActualWidth;
        }

        return !double.IsNaN(element.Width) && element.Width > 0 ? element.Width : 0;
    }

    private static double ResolvePopupElementHeight(FrameworkElement element)
    {
        if (element.ActualHeight > 0)
        {
            return element.ActualHeight;
        }

        return element.DesiredSize.Height > 0 ? element.DesiredSize.Height : 0;
    }

    private void CloseShellNotificationPopups()
    {
        if (AutoNotificationPopup is not null)
        {
            AutoNotificationPopup.IsOpen = false;
        }

        if (ManualNotificationPopup is not null)
        {
            ManualNotificationPopup.IsOpen = false;
        }
    }
}
