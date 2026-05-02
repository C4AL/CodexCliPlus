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
    private async Task ShowSettingsOverlayAsync()
    {
        if (_settingsOverlayOpen)
        {
            UpdateSettingsOverlayBaseline();
            Activate();
            return;
        }

        try
        {
            _settingsOverlayOpen = true;
            SetNavigationDockPopupOpen(false);
            UpdateSettingsOverlayBaseline();
            _settingsOverlayCoveredWebView = ManagementWebView.Visibility == Visibility.Visible;
            if (_settingsOverlayCoveredWebView)
            {
                ManagementWebView.Visibility = Visibility.Collapsed;
            }

            SettingsOverlay.Visibility = Visibility.Visible;
            SettingsOverlay.Opacity = 0;

            if (SettingsDialogCard.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = 0.96;
                scale.ScaleY = 0.96;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateEaseAnimation(1, 180));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateEaseAnimation(1, 180));
            }

            SettingsOverlay.BeginAnimation(
                UIElement.OpacityProperty,
                CreateEaseAnimation(1, 180)
            );
            Activate();
            await RefreshSettingsOverlayAsync();
        }
        catch (Exception exception)
        {
            _settingsOverlayOpen = false;
            CloseSettingsOverlayImmediately();
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
        SettingsOverlay.BeginAnimation(UIElement.OpacityProperty, CreateEaseAnimation(0, 140));
        if (SettingsDialogCard.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateEaseAnimation(0.96, 140));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateEaseAnimation(0.96, 140));
        }

        await Task.Delay(TimeSpan.FromMilliseconds(150));
        if (!_settingsOverlayOpen)
        {
            CloseSettingsOverlayImmediately();
            UpdateNavigationDockPopupVisibility();
        }
    }

    private void CloseSettingsOverlayImmediately()
    {
        SettingsOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        SettingsOverlay.Opacity = 0;
        SettingsOverlay.Visibility = Visibility.Collapsed;
        if (SettingsDialogCard.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 0.96;
            scale.ScaleY = 0.96;
        }

        RestoreManagementWebViewAfterSettingsOverlay();
    }

    private void RestoreManagementWebViewAfterSettingsOverlay()
    {
        if (!_settingsOverlayCoveredWebView)
        {
            return;
        }

        _settingsOverlayCoveredWebView = false;
        if (
            _startupState == StartupState.LoadingManagement
            && UpgradeNoticePanel.Visibility != Visibility.Visible
            && StartupFlow.Visibility != Visibility.Visible
            && BlockerPanel.Visibility != Visibility.Visible
        )
        {
            ManagementWebView.Visibility = Visibility.Visible;
        }
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
                    SettingsProviderOverviewText.Text = "提供商概览：正在同步...";
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
        SetSettingsUpdateStatus($"当前版本 {CurrentApplicationVersion}");
    }

    private void SetSettingsUpdateStatus(string status)
    {
        SettingsUpdateStatusText.Text = $"更新状态：{status}。";
    }

    private string ShellConnectionStatusLabel =>
        _shellConnectionStatus switch
        {
            "connected" => "已连接",
            "connecting" => "连接中",
            "error" => "异常",
            _ => "未连接",
        };
}
