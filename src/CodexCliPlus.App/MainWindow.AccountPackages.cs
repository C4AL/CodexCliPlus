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
    private async Task ClearUsageStatsAsync()
    {
        SettingsClearUsageButton.IsEnabled = false;
        try
        {
            CancelUsageStatsSyncDebounce();
            await _persistenceService.ClearUsageSnapshotAsync();
            PostWebUiCommand(new { type = "clearUsageStats" });
            _notificationService.ShowAuto("使用统计已清除。");
        }
        catch (Exception exception)
        {
            _notificationService.ShowManual("清除统计失败", exception.Message);
        }
        finally
        {
            SettingsClearUsageButton.IsEnabled = true;
        }
    }

    private async Task ImportAccountConfigAsync(string? mode)
    {
        if (string.Equals(mode, "sac", StringComparison.OrdinalIgnoreCase))
        {
            await ImportSacPackageAsync();
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入账号配置",
            Filter =
                "账号配置|*.json;*.yaml;*.yml;*.sac|JSON 配置|*.json|YAML 配置|*.yaml;*.yml|安全包|*.sac",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var extension = Path.GetExtension(dialog.FileName);
        if (string.Equals(extension, ".sac", StringComparison.OrdinalIgnoreCase))
        {
            await ImportSacPackageFromPathAsync(dialog.FileName);
            return;
        }

        try
        {
            if (
                string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase)
            )
            {
                if (!ConfirmAccountConfigImport())
                {
                    return;
                }

                var yaml = await File.ReadAllTextAsync(dialog.FileName);
                await _managementConfigurationService.PutConfigYamlAsync(yaml);
                _notificationService.ShowAuto("账号配置已导入。");
                PostWebUiCommand(new { type = "refreshAll" });
                return;
            }

            if (!ConfirmAccountConfigImport())
            {
                return;
            }

            var payload = await SecureAccountPackageService.ReadPlainPackageAsync(dialog.FileName);
            await ApplyAccountPackagePayloadAsync(payload);
        }
        catch (Exception exception)
        {
            _notificationService.ShowManual("导入配置失败", exception.Message);
        }
    }

    private async Task ExportAccountConfigAsync(string? mode)
    {
        if (string.Equals(mode, "sac", StringComparison.OrdinalIgnoreCase))
        {
            await ExportSacPackageAsync();
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出账号配置",
            Filter = "JSON 配置|*.json",
            FileName = $"CodexCliPlus.AccountConfig.{DateTimeOffset.Now:yyyyMMddHHmmss}.json",
            AddExtension = true,
            DefaultExt = ".json",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var payload = await BuildAccountPackagePayloadAsync();
            await SecureAccountPackageService.WritePlainPackageAsync(payload, dialog.FileName);
            _notificationService.ShowAuto("账号配置已导出。");
        }
        catch (Exception exception)
        {
            _notificationService.ShowManual("导出配置失败", exception.Message);
        }
    }

    private async Task ImportSacPackageAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入安全包",
            Filter = "安全包|*.sac",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await ImportSacPackageFromPathAsync(dialog.FileName);
    }

    private async Task ImportSacPackageFromPathAsync(string packagePath)
    {
        var password = ShowPasswordPrompt(
            "导入安全包",
            "输入安全包密码以解密账号配置。",
            confirmPassword: false
        );
        if (password is null)
        {
            return;
        }

        try
        {
            if (!ConfirmAccountConfigImport())
            {
                return;
            }

            var payload = await SecureAccountPackageService.ReadEncryptedPackageAsync(
                packagePath,
                password
            );
            await ApplyAccountPackagePayloadAsync(payload);
        }
        catch (Exception exception)
        {
            _notificationService.ShowManual("导入安全包失败", exception.Message);
        }
    }

    private async Task ExportSacPackageAsync()
    {
        var password = ShowPasswordPrompt(
            "导出安全包",
            "设置安全包密码。密码不会被保存，丢失后无法恢复。",
            confirmPassword: true
        );
        if (password is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出安全包",
            Filter = "安全包|*.sac",
            FileName = $"CodexCliPlus.AccountConfig.{DateTimeOffset.Now:yyyyMMddHHmmss}.sac",
            AddExtension = true,
            DefaultExt = ".sac",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var payload = await BuildAccountPackagePayloadAsync();
            await SecureAccountPackageService.WriteEncryptedPackageAsync(
                payload,
                password,
                dialog.FileName
            );
            _notificationService.ShowAuto("安全包已导出。");
        }
        catch (Exception exception)
        {
            _notificationService.ShowManual("导出安全包失败", exception.Message);
        }
    }

    private async Task<SecureAccountPackagePayload> BuildAccountPackagePayloadAsync()
    {
        var configYaml = await _managementConfigurationService.GetConfigYamlAsync();
        var authFiles = await _managementAuthService.GetAuthFilesAsync();
        var excludedModels = await _managementAuthService.GetOAuthExcludedModelsAsync();
        var modelAliases = await _managementAuthService.GetOAuthModelAliasesAsync();
        var payload = new SecureAccountPackagePayload
        {
            ConfigYaml = configYaml.Value,
            OAuthExcludedModels = excludedModels.Value.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToList(),
                StringComparer.OrdinalIgnoreCase
            ),
            OAuthModelAliases = modelAliases.Value.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToList(),
                StringComparer.OrdinalIgnoreCase
            ),
        };

        foreach (var file in authFiles.Value)
        {
            if (string.IsNullOrWhiteSpace(file.Name) || file.RuntimeOnly || file.Unavailable)
            {
                continue;
            }

            var content = await _managementAuthService.DownloadAuthFileAsync(file.Name);
            payload.AuthFiles.Add(
                new SecureAccountPackageAuthFile { Name = file.Name, Content = content.Value }
            );
        }

        return payload;
    }

    private async Task ApplyAccountPackagePayloadAsync(SecureAccountPackagePayload payload)
    {
        if (!string.IsNullOrWhiteSpace(payload.ConfigYaml))
        {
            await _managementConfigurationService.PutConfigYamlAsync(payload.ConfigYaml);
        }

        if (payload.AuthFiles.Count > 0)
        {
            var files = payload
                .AuthFiles.Where(file =>
                    !string.IsNullOrWhiteSpace(file.Name)
                    && !string.IsNullOrWhiteSpace(file.Content)
                )
                .Select(file => new ManagementAuthFileUpload
                {
                    FileName = file.Name,
                    Content = Encoding.UTF8.GetBytes(file.Content),
                    ContentType = "application/json",
                })
                .ToArray();
            await _managementAuthService.UploadAuthFilesAsync(files);
        }

        if (payload.OAuthExcludedModels.Count > 0)
        {
            await _managementAuthService.ReplaceOAuthExcludedModelsAsync(
                payload.OAuthExcludedModels.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value,
                    StringComparer.OrdinalIgnoreCase
                )
            );
        }

        if (payload.OAuthModelAliases.Count > 0)
        {
            await _managementAuthService.ReplaceOAuthModelAliasesAsync(
                payload.OAuthModelAliases.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<ManagementOAuthModelAliasEntry>)pair.Value,
                    StringComparer.OrdinalIgnoreCase
                )
            );
        }

        PostWebUiCommand(new { type = "refreshAll" });
        _notificationService.ShowAuto("账号配置已导入。");
    }

    private bool ConfirmAccountConfigImport()
    {
        return MessageBox.Show(
                this,
                "导入会覆盖当前账号配置，并写入认证文件与 OAuth 相关配置。是否继续？",
                "导入账号配置",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning
            ) == MessageBoxResult.OK;
    }

    private string? ShowPasswordPrompt(string title, string message, bool confirmPassword)
    {
        var passwordBox = new PasswordBox { MinWidth = 280 };
        var confirmBox = new PasswordBox { MinWidth = 280 };
        var errorText = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.Firebrick,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var okButton = new WpfButton
        {
            Content = "确认",
            Width = 86,
            Height = 32,
            IsDefault = true,
        };
        var content = BuildPasswordPromptContent(
            message,
            passwordBox,
            confirmBox,
            errorText,
            okButton,
            confirmPassword
        );
        var window = new Window
        {
            Owner = this,
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = System.Windows.Media.Brushes.White,
            Content = content,
        };

        okButton.Click += (_, _) =>
        {
            var password = passwordBox.Password;
            if (string.IsNullOrWhiteSpace(password))
            {
                errorText.Text = "密码不能为空。";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            if (
                confirmPassword
                && !string.Equals(password, confirmBox.Password, StringComparison.Ordinal)
            )
            {
                errorText.Text = "两次输入的密码不一致。";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            window.DialogResult = true;
        };

        passwordBox.Focus();
        return window.ShowDialog() == true ? passwordBox.Password : null;
    }

    private static StackPanel BuildPasswordPromptContent(
        string message,
        PasswordBox passwordBox,
        PasswordBox confirmBox,
        TextBlock errorText,
        WpfButton okButton,
        bool confirmPassword
    )
    {
        var root = new StackPanel { Margin = new Thickness(22), Width = 360 };
        root.Children.Add(
            new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
            }
        );
        root.Children.Add(new TextBlock { Text = "密码", Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(passwordBox);

        if (confirmPassword)
        {
            root.Children.Add(
                new TextBlock { Text = "确认密码", Margin = new Thickness(0, 12, 0, 6) }
            );
            root.Children.Add(confirmBox);
        }

        root.Children.Add(errorText);

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var cancelButton = new WpfButton
        {
            Content = "取消",
            Width = 86,
            Height = 32,
            IsCancel = true,
            Margin = new Thickness(0, 0, 8, 0),
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);
        root.Children.Add(buttons);
        return root;
    }

    private async Task SyncPersistenceBeforeExitAsync()
    {
        CancelUsageStatsSyncDebounce();
        try
        {
            await _persistenceService.SyncUsageSnapshotAsync();
            _lastUsageSnapshotSyncAt = DateTimeOffset.UtcNow;
            await _persistenceService.SyncLogsSnapshotAsync();
        }
        catch { }
    }

    private void ScheduleUsageStatsRefreshedSync(bool force = false)
    {
        if (_backendProcessManager.CurrentStatus.State != BackendStateKind.Running)
        {
            return;
        }

        if (!force && DateTimeOffset.UtcNow - _lastUsageSnapshotSyncAt < UsageSnapshotSyncCooldown)
        {
            return;
        }

        CancelUsageStatsSyncDebounce();
        var cts = new CancellationTokenSource();
        _usageStatsSyncDebounceCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1200), cts.Token);
                await _persistenceService.SyncUsageSnapshotAsync(cts.Token);
                _lastUsageSnapshotSyncAt = DateTimeOffset.UtcNow;
                MarkStartupPhase("usage-snapshot-synced");
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                _logger.Warn($"Usage snapshot sync failed: {exception.Message}");
            }
            finally
            {
                if (ReferenceEquals(_usageStatsSyncDebounceCts, cts))
                {
                    _usageStatsSyncDebounceCts = null;
                }

                cts.Dispose();
            }
        });
    }

    private void CancelUsageStatsSyncDebounce()
    {
        var cts = _usageStatsSyncDebounceCts;
        _usageStatsSyncDebounceCts = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    private void StartPostStartupPersistenceImport()
    {
        CancelPostStartupPersistenceImport();
        var cts = new CancellationTokenSource();
        _postStartupPersistenceCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cts.Token);
                await _persistenceService.ImportNewerUsageSnapshotAsync(cts.Token);
                MarkStartupPhase("usage-import-finished");
                await Dispatcher.InvokeAsync(() => PostWebUiCommand(new { type = "refreshUsage" }));
                ScheduleUsageStatsRefreshedSync(force: true);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                _logger.Warn($"Startup usage import failed: {exception.Message}");
            }
            finally
            {
                if (ReferenceEquals(_postStartupPersistenceCts, cts))
                {
                    _postStartupPersistenceCts = null;
                }

                cts.Dispose();
            }
        });
    }

    private void CancelPostStartupPersistenceImport()
    {
        var cts = _postStartupPersistenceCts;
        _postStartupPersistenceCts = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException) { }
    }
}
