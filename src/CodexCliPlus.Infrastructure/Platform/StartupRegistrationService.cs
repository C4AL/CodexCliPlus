using System.Diagnostics.CodeAnalysis;
using CodexCliPlus.Core.Constants;
using Microsoft.Win32;

namespace CodexCliPlus.Infrastructure.Platform;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Instance member is resolved through dependency injection."
    )]
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return !string.IsNullOrWhiteSpace(key?.GetValue(AppConstants.ProductKey) as string);
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Instance member is resolved through dependency injection."
    )]
    public void SetEnabled(bool enabled)
    {
        using var key =
            Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            var executablePath =
                Environment.ProcessPath
                ?? throw new InvalidOperationException(
                    "Could not resolve the current executable path."
                );
            key.SetValue(AppConstants.ProductKey, $"\"{executablePath}\"");
        }
        else
        {
            key.DeleteValue(AppConstants.ProductKey, throwOnMissingValue: false);
        }
    }
}
