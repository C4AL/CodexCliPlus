using DesktopHost.Core.Constants;

using Microsoft.Win32;

namespace DesktopHost.Infrastructure.Platform;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return !string.IsNullOrWhiteSpace(key?.GetValue(AppConstants.ProductName) as string);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("当前进程路径不可用，无法设置开机启动。");
            key.SetValue(AppConstants.ProductName, $"\"{executablePath}\"");
        }
        else
        {
            key.DeleteValue(AppConstants.ProductName, throwOnMissingValue: false);
        }
    }
}
