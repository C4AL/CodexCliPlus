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
        CloseShellNotificationPopups();
        ShowInTaskbar = false;
        Hide();
        UpdateSettingsOverlayPopupVisibility();
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
        UpdateSettingsOverlayPopupVisibility();
        UpdateShellNotificationPopupVisibility();
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
