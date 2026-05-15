using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexCliPlus;

internal static class InstallerCleanupMode
{
    internal const string ModeArgument = "codexcliplus-installer-cleanup";
    internal const string TargetArgument = "codexcliplus-cleanup-target";
    internal const string ParentProcessArgument = "codexcliplus-cleanup-parent";

    private const int DeleteRetryCount = 60;
    private const int DeleteRetryDelayMilliseconds = 1000;
    private const int ParentProcessWaitMilliseconds = 30000;

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (!HasArgument(args, ModeArgument))
        {
            return false;
        }

        try
        {
            var request = ParseRequest(args);
            Run(request);
        }
        catch (Exception exception)
        {
            Trace.TraceError($"[InstallerCleanup] cleanup mode failed: {exception}");
            exitCode = 1;
        }

        return true;
    }

    internal static InstallerCleanupRequest ParseRequest(string[] args)
    {
        string? encodedTarget = GetArgumentValue(args, TargetArgument);
        if (string.IsNullOrWhiteSpace(encodedTarget))
        {
            throw new ArgumentException("[InstallerCleanup] cleanup target argument is empty.");
        }

        string targetPath = NormalizeInstallerPath(DecodeArgument(encodedTarget));
        int parentProcessId = ParseParentProcessId(GetArgumentValue(args, ParentProcessArgument));
        return new InstallerCleanupRequest(targetPath, parentProcessId);
    }

    internal static string EncodeArgument(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static void Run(InstallerCleanupRequest request)
    {
        string currentExecutablePath = GetCurrentExecutablePath();
        if (string.Equals(request.TargetPath, currentExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            Trace.TraceWarning("[InstallerCleanup] refusing to delete cleanup helper itself as target.");
            return;
        }

        if (!IsCodexCliPlusInstaller(request.TargetPath))
        {
            Trace.TraceWarning($"[InstallerCleanup] target is not a CodexCliPlus installer: {request.TargetPath}");
            return;
        }

        WaitForParentProcess(request.ParentProcessId);
        TryDeleteInstaller(request.TargetPath);
    }

    private static string NormalizeInstallerPath(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath))
        {
            throw new ArgumentException("Installer path is empty.", nameof(installerPath));
        }

        string fullPath = Path.GetFullPath(installerPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("[InstallerCleanup] delete target must be an executable.");
        }

        return fullPath;
    }

    private static bool IsCodexCliPlusInstaller(string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return true;
        }

        try
        {
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(targetPath);
            return string.Equals(versionInfo.ProductName, "CodexCliPlus", StringComparison.OrdinalIgnoreCase)
                && ContainsInstallerMarker(versionInfo.FileDescription);
        }
        catch (Exception exception)
        {
            Trace.TraceWarning($"[InstallerCleanup] cannot read target version info: {exception.Message}");
            return false;
        }
    }

    private static bool ContainsInstallerMarker(string? value)
    {
        return value?.Contains("安装程序", StringComparison.OrdinalIgnoreCase) == true
            || value?.Contains("Setup", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void WaitForParentProcess(int parentProcessId)
    {
        if (parentProcessId <= 0)
        {
            return;
        }

        try
        {
            using Process parentProcess = Process.GetProcessById(parentProcessId);
            parentProcess.WaitForExit(ParentProcessWaitMilliseconds);
        }
        catch (ArgumentException)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceWarning($"[InstallerCleanup] cannot wait for parent process: {exception.Message}");
        }

        Thread.Sleep(DeleteRetryDelayMilliseconds);
    }

    private static void TryDeleteInstaller(string targetPath)
    {
        Exception? lastError = null;
        for (int attempt = 1; attempt <= DeleteRetryCount; attempt++)
        {
            try
            {
                if (!File.Exists(targetPath))
                {
                    Trace.TraceInformation($"[InstallerCleanup] installer already deleted: {targetPath}");
                    return;
                }

                File.SetAttributes(targetPath, FileAttributes.Normal);
                File.Delete(targetPath);
                if (!File.Exists(targetPath))
                {
                    Trace.TraceInformation($"[InstallerCleanup] deleted installer: {targetPath}");
                    return;
                }
            }
            catch (Exception exception)
            {
                lastError = exception;
            }

            Thread.Sleep(DeleteRetryDelayMilliseconds);
        }

        Trace.TraceError($"[InstallerCleanup] installer delete failed: {targetPath}. {lastError}");
        TryScheduleDeleteOnReboot(targetPath);
    }

    private static void TryScheduleDeleteOnReboot(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                _ = MoveFileEx(path, null, MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT);
            }
        }
        catch (Exception exception)
        {
            Trace.TraceWarning($"[InstallerCleanup] cannot schedule cleanup on reboot: {exception.Message}");
        }
    }

    private static string GetCurrentExecutablePath()
    {
        try
        {
            using Process currentProcess = Process.GetCurrentProcess();
            string? processPath = currentProcess.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                return Path.GetFullPath(processPath);
            }
        }
        catch (Exception exception)
        {
            Trace.TraceWarning($"[InstallerCleanup] cannot resolve process executable path: {exception.Message}");
        }

        return Path.GetFullPath(Environment.ProcessPath ?? AppContext.BaseDirectory);
    }

    private static bool HasArgument(string[] args, string name)
    {
        string slashName = "/" + name;
        string dashName = "-" + name;
        string doubleDashName = "--" + name;
        foreach (string arg in args)
        {
            if (
                string.Equals(arg, slashName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, dashName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, doubleDashName, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetArgumentValue(string[] args, string name)
    {
        string slashPrefix = "/" + name + "=";
        string dashPrefix = "-" + name + "=";
        string doubleDashPrefix = "--" + name + "=";
        foreach (string arg in args)
        {
            if (arg.StartsWith(slashPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[slashPrefix.Length..];
            }

            if (arg.StartsWith(dashPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[dashPrefix.Length..];
            }

            if (arg.StartsWith(doubleDashPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[doubleDashPrefix.Length..];
            }
        }

        return null;
    }

    private static int ParseParentProcessId(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId)
            ? processId
            : 0;
    }

    private static string DecodeArgument(string value)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(
        string lpExistingFileName,
        string? lpNewFileName,
        MoveFileFlags dwFlags
    );

    [Flags]
    private enum MoveFileFlags
    {
        MOVEFILE_DELAY_UNTIL_REBOOT = 0x4,
    }
}

internal sealed record InstallerCleanupRequest(string TargetPath, int ParentProcessId);
