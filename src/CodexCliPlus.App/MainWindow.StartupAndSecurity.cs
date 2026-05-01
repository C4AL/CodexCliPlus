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
    private void DragRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        e.Handled = true;

        if (e.ClickCount == 2)
        {
            WindowState =
                WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException) { }
    }

    private async Task ContinueAfterStartupGateAsync()
    {
        StartupFlow.RememberManagementKey = _settings.RememberManagementKey;

        if (_settings.RememberManagementKey && !string.IsNullOrWhiteSpace(_settings.ManagementKey))
        {
            if (_backendConfigWriter.VerifyManagementKey(_settings.ManagementKey))
            {
                await InitializeHostAsync(restartBackend: false);
                return;
            }

            _settings.ManagementKey = string.Empty;
            await EnsureMinimumPreparationDisplayAsync();
            ShowLogin("本机保存的安全密钥无法通过验证，请重新输入。");
            return;
        }

        await EnsureMinimumPreparationDisplayAsync();
        ShowLogin();
    }

    private async Task BeginFirstRunKeyRevealAsync()
    {
        ShowPreparationStep(35, "正在生成首次安全密钥。", StartupState.Preparing);

        _firstRunManagementKey = GenerateSecurityKey();
        _settings.ManagementKey = _firstRunManagementKey;
        _settings.RememberManagementKey = false;
        _settings.SecurityKeyOnboardingCompleted = false;

        await _backendConfigWriter.WriteAsync(
            _settings,
            new BackendConfigWriteOptions
            {
                AllowManagementKeyRotation = true,
                ValidatePort = false,
            }
        );

        await EnsureMinimumPreparationDisplayAsync();
        ShowFirstRunKeyReveal();
    }

    private async Task BeginFirstRunConfirmationAsync()
    {
        if (string.IsNullOrWhiteSpace(_firstRunManagementKey))
        {
            _notificationService.ShowManual("安全密钥尚未生成", "请重试。");
            return;
        }

        StartupFlow.SetFirstRunConfirmVisible(
            visible: true,
            buttonText: $"确认 ({FirstRunConfirmationSeconds})",
            canConfirm: false
        );

        _firstRunConfirmCountdown?.Cancel();
        _firstRunConfirmCountdown?.Dispose();
        _firstRunConfirmCountdown = new CancellationTokenSource();
        var token = _firstRunConfirmCountdown.Token;

        try
        {
            for (var seconds = FirstRunConfirmationSeconds; seconds > 0; seconds--)
            {
                StartupFlow.SetFirstRunConfirmVisible(
                    visible: true,
                    buttonText: $"确认 ({seconds})",
                    canConfirm: false
                );
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }

            StartupFlow.SetFirstRunConfirmVisible(
                visible: true,
                buttonText: "确认",
                canConfirm: true
            );
        }
        catch (TaskCanceledException) { }
    }

    private async Task CompleteFirstRunAsync()
    {
        if (string.IsNullOrWhiteSpace(_firstRunManagementKey))
        {
            _notificationService.ShowManual("安全密钥尚未生成", "请重试。");
            return;
        }

        StartupFlow.SetFirstRunConfirmActions(canConfirm: false, canClose: false);

        try
        {
            _firstRunConfirmCountdown?.Cancel();
            _settings.ManagementKey = _firstRunManagementKey;
            _settings.RememberManagementKey = StartupFlow.FirstRunRememberManagementKey;
            _settings.SecurityKeyOnboardingCompleted = true;
            _settings.LastSeenApplicationVersion = CurrentApplicationVersion;
            await _appConfigurationService.SaveAsync(_settings);

            StartupFlow.RememberManagementKey = _settings.RememberManagementKey;
            StartupFlow.SetFirstRunConfirmVisible(
                visible: false,
                buttonText: "确认",
                canConfirm: false
            );

            _firstRunManagementKey = string.Empty;
            StartupFlow.ClearFirstRunKey();
            if (_settings.RememberManagementKey)
            {
                await InitializeHostAsync(restartBackend: false);
                return;
            }

            _settings.ManagementKey = string.Empty;
            await _appConfigurationService.SaveAsync(_settings);
            ShowLogin("初始化已完成，请输入安全密钥登录。");
        }
        catch (Exception exception)
        {
            StartupFlow.SetFirstRunConfirmActions(canConfirm: true, canClose: true);
            _notificationService.ShowManual("初始化失败", exception.Message);
            StartupFlow.SetFirstRunConfirmVisible(
                visible: false,
                buttonText: "确认",
                canConfirm: false
            );
        }
    }

    private void CancelFirstRunConfirmation()
    {
        _firstRunConfirmCountdown?.Cancel();
        StartupFlow.SetFirstRunConfirmVisible(
            visible: false,
            buttonText: "确认",
            canConfirm: false
        );
    }

    private async Task SignInAsync()
    {
        var managementKey = StartupFlow.ManagementKey;
        if (string.IsNullOrWhiteSpace(managementKey))
        {
            ShowLoginError("请输入安全密钥。");
            return;
        }

        StartupFlow.SetLoginBusy(true);
        StartupFlow.SetLoginError(null);

        try
        {
            if (!_backendConfigWriter.HasExistingManagementKeyHash())
            {
                ShowLoginError("未找到后端安全密钥配置，请重置后重新初始化。");
                return;
            }

            if (!_backendConfigWriter.VerifyManagementKey(managementKey))
            {
                ShowLoginError("安全密钥不正确。");
                return;
            }

            _settings.ManagementKey = managementKey;
            _settings.RememberManagementKey = StartupFlow.RememberManagementKey;
            _settings.SecurityKeyOnboardingCompleted = true;
            await _appConfigurationService.SaveAsync(_settings);
            await InitializeHostAsync(restartBackend: false);
        }
        catch (Exception exception)
        {
            ShowLoginError(exception.Message);
        }
        finally
        {
            if (StartupFlow.IsLoginVisible)
            {
                StartupFlow.SetLoginBusy(false);
            }
        }
    }

    private async Task ResetSecurityKeyAsync()
    {
        StartupFlow.SetLoginBusy(true);
        ShowPreparationStep(20, "正在重置安全密钥和本地认证状态。", StartupState.Preparing);

        try
        {
            var configuredAuthDirectory = TryReadConfiguredAuthDirectory();

            await SyncPersistenceBeforeExitAsync();
            await _backendProcessManager.StopAsync();
            await ResetWebUiLocalAuthStateAsync();

            await _credentialStore.DeleteSecretAsync(_settings.ManagementKeyReference);
            await _credentialStore.DeleteSecretAsync(AppConstants.DefaultManagementKeyReference);

            DeleteFileIfExistsInsideRoot(_pathService.Directories.BackendConfigFilePath);
            DeleteFileIfExistsInsideRoot(_pathService.Directories.SettingsFilePath);
            DeleteDirectoryIfExistsInsideRoot(
                Path.Combine(
                    _pathService.Directories.ConfigDirectory,
                    AppConstants.SecretsDirectoryName
                )
            );
            DeleteDirectoryIfExistsInsideRoot(
                Path.Combine(_pathService.Directories.BackendDirectory, "auth")
            );
            if (
                !string.IsNullOrWhiteSpace(configuredAuthDirectory)
                && IsPathInsideAppRoot(configuredAuthDirectory)
            )
            {
                DeleteDirectoryIfExistsInsideRoot(configuredAuthDirectory);
            }

            _firstRunManagementKey = string.Empty;
            StartupFlow.ClearLoginPassword();
            _settings = await _appConfigurationService.LoadAsync();
            await BeginFirstRunKeyRevealAsync();
        }
        catch (Exception exception)
        {
            ShowBlocker("安全密钥重置失败", "未能完成本地配置和认证状态清理。", exception.Message);
        }
    }

    private static string GenerateSecurityKey()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    }

    private static string BuildSecurityKeyFileContent(string securityKey)
    {
        return "CodexCliPlus 安全密钥"
            + Environment.NewLine
            + Environment.NewLine
            + securityKey
            + Environment.NewLine
            + Environment.NewLine
            + "请妥善保存。完整安全密钥只会在首次初始化页面显示一次。"
            + Environment.NewLine
            + "不要把此文件发送给不受信任的人。"
            + Environment.NewLine;
    }

    private static string BuildDesktopSecurityKeyFilePath()
    {
        var desktopDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory
        );
        if (string.IsNullOrWhiteSpace(desktopDirectory))
        {
            throw new InvalidOperationException("无法定位桌面目录。");
        }

        string normalizedDesktopDirectory;
        try
        {
            normalizedDesktopDirectory = Path.GetFullPath(desktopDirectory);
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException("桌面目录路径无效。", exception);
        }

        if (!Directory.Exists(normalizedDesktopDirectory))
        {
            throw new InvalidOperationException($"桌面目录不可用：{normalizedDesktopDirectory}");
        }

        var fileName = $"CodexCliPlus-安全密钥-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
        return Path.Combine(normalizedDesktopDirectory, fileName);
    }

    private static string BuildDesktopSaveErrorMessage(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => $"无法写入系统桌面目录。{exception.Message}",
            IOException => $"无法写入系统桌面目录。{exception.Message}",
            _ => exception.Message,
        };
    }

    private async Task EnsureMinimumPreparationDisplayAsync()
    {
        if (_preparationPanelShownAt is not { } shownAt)
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - shownAt;
        var remaining = MinimumPreparationDisplayDuration - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }
    }

    private static string BuildPreparationStatus(double progress, StartupState state)
    {
        if (state == StartupState.LoadingManagement || progress >= 90)
        {
            return "正在接入管理界面";
        }

        if (progress >= 65)
        {
            return "正在启动本地后端";
        }

        if (progress >= 35)
        {
            return "正在校验运行资源";
        }

        return "正在启动桌面宿主";
    }

    private async Task ResetWebUiLocalAuthStateAsync()
    {
        if (!_webViewConfigured || ManagementWebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            ManagementWebView.CoreWebView2.CookieManager.DeleteAllCookies();
            await ManagementWebView.CoreWebView2.ExecuteScriptAsync(
                """
                (() => {
                  try {
                    [
                      'codexcliplus-auth',
                      'cli-proxy-auth',
                      'isLoggedIn',
                      'apiBase',
                      'apiUrl',
                      'managementKey'
                    ].forEach((key) => localStorage.removeItem(key));
                    sessionStorage.clear();
                  } catch {
                  }
                })();
                """
            );
        }
        catch { }
    }

    private void DeleteFileIfExistsInsideRoot(string filePath)
    {
        EnsurePathInsideAppRoot(filePath);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private void DeleteDirectoryIfExistsInsideRoot(string directoryPath)
    {
        EnsurePathInsideAppRoot(directoryPath);
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private void EnsurePathInsideAppRoot(string path)
    {
        if (!IsPathInsideAppRoot(path))
        {
            throw new InvalidOperationException("拒绝清理应用数据目录之外的路径。");
        }
    }

    private bool IsPathInsideAppRoot(string path)
    {
        var root = Path.GetFullPath(_pathService.Directories.RootDirectory);
        var normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(path);

        return target.Equals(root, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private string? TryReadConfiguredAuthDirectory()
    {
        try
        {
            if (!File.Exists(_pathService.Directories.BackendConfigFilePath))
            {
                return null;
            }

            var yaml = File.ReadAllText(_pathService.Directories.BackendConfigFilePath);
            var match = Regex.Match(yaml, "(?m)^auth-dir:\\s*\"(?<path>(?:\\\\.|[^\"])*)\"");
            if (!match.Success)
            {
                return null;
            }

            var authDirectory = match
                .Groups["path"]
                .Value.Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal)
                .Trim();

            return string.IsNullOrWhiteSpace(authDirectory) ? null : authDirectory;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureWebView2Runtime()
    {
        var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new WebView2RuntimeNotFoundException();
        }
    }
}
