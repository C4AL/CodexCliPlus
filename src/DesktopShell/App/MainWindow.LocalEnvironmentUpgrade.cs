using System.Windows;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models.LocalEnvironment;
using CodexCliPlus.Services.Notifications;

namespace CodexCliPlus;

public partial class MainWindow
{
    private static readonly TimeSpan OfflineEnvironmentUpgradeRetryDelay = TimeSpan.FromHours(6);

    private void StartOfflineEnvironmentUpgradeCheck()
    {
        if (_offlineEnvironmentUpgradeCheckStarted)
        {
            return;
        }

        _offlineEnvironmentUpgradeCheckStarted = true;
        _offlineEnvironmentUpgradeCheckCts?.Cancel();
        _offlineEnvironmentUpgradeCheckCts = new CancellationTokenSource();
        var cancellationToken = _offlineEnvironmentUpgradeCheckCts.Token;
        _ = Task.Run(
            () => RunOfflineEnvironmentUpgradeCheckAsync(cancellationToken),
            cancellationToken
        );
    }

    private async Task RunOfflineEnvironmentUpgradeCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await _offlinePackageService.TryReadPendingUpgradeAsync(cancellationToken);
            if (state is null || state.NextAllowedCheckAt > DateTimeOffset.Now)
            {
                return;
            }

            if (!await _offlinePackageService.IsUpgradeNetworkReadyAsync(cancellationToken))
            {
                await _offlinePackageService.PostponePendingUpgradeAsync(
                    OfflineEnvironmentUpgradeRetryDelay,
                    cancellationToken
                );
                return;
            }

            await Dispatcher.InvokeAsync(ShowOfflineEnvironmentUpgradeNotification);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogError("Offline local environment upgrade check failed.", exception);
        }
    }

    private void ShowOfflineEnvironmentUpgradeNotification()
    {
        _notificationService.ShowManual(
            "本地环境可升级",
            "检测到网络已恢复，可将离线安装的 Node.js LTS 和 Codex CLI 升级到最新版本。",
            ShellNotificationLevel.Info,
            [
                new ShellNotificationAction(
                    "upgrade-now",
                    "立即升级",
                    () => _ = BeginOfflineEnvironmentUpgradeAsync()
                ),
                new ShellNotificationAction(
                    "upgrade-later",
                    "稍后",
                    () => _ = PostponeOfflineEnvironmentUpgradeAsync()
                ),
            ]
        );
    }

    private async Task BeginOfflineEnvironmentUpgradeAsync()
    {
        await RunLocalDependencyRepairAsync(
            requestId: null,
            LocalDependencyRepairActionIds.UpgradeBundledEnvInstallLatestCodex
        );
    }

    private async Task PostponeOfflineEnvironmentUpgradeAsync()
    {
        try
        {
            await _offlinePackageService.PostponePendingUpgradeAsync(
                OfflineEnvironmentUpgradeRetryDelay
            );
            _notificationService.ShowAuto("已延后本地环境升级提示。", ShellNotificationLevel.Info);
        }
        catch (Exception exception)
        {
            _logger.LogError("Failed to postpone offline local environment upgrade.", exception);
            _notificationService.ShowManual("延后升级失败", exception.Message, ShellNotificationLevel.Error);
        }
    }

    private async Task<LocalDependencyRepairResult> HandleOfflineEnvironmentUpgradeRepairOutcomeAsync(
        string actionId,
        LocalDependencyRepairResult result,
        LocalDependencySnapshot? snapshot
    )
    {
        if (actionId != LocalDependencyRepairActionIds.UpgradeBundledEnvInstallLatestCodex)
        {
            return result;
        }

        if (result.Succeeded && snapshot is not null && IsRequiredLocalEnvironmentReady(snapshot))
        {
            await _offlinePackageService.ClearPendingUpgradeAsync();
            return result;
        }

        await _offlinePackageService.PostponePendingUpgradeAsync(OfflineEnvironmentUpgradeRetryDelay);
        if (result.Succeeded)
        {
            return new LocalDependencyRepairResult
            {
                ActionId = result.ActionId,
                Succeeded = false,
                ExitCode = result.ExitCode,
                Summary = "升级后健康检查未通过。",
                Detail = snapshot?.Summary ?? "升级完成后未能读取本地环境健康检查结果。",
                FailureKind = result.FailureKind,
                RecommendedFallbackActionId = result.RecommendedFallbackActionId,
                LogPath = result.LogPath,
                DebugReportPath = result.DebugReportPath,
            };
        }

        return result;
    }

    private static bool IsRequiredLocalEnvironmentReady(LocalDependencySnapshot snapshot)
    {
        return snapshot.Items.All(item =>
            item.Severity != LocalDependencySeverity.Required
            || item.Status == LocalDependencyStatus.Ready
        );
    }
}
