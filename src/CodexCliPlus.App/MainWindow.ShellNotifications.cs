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
}
