using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CodexCliPlus.Core.Constants;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Microsoft.Web.WebView2.Core;
using FlaUIApplication = FlaUI.Core.Application;

namespace CodexCliPlus.UiTests;

internal sealed class UiTestRun : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    private readonly List<string> _desktopFilesToDelete = [];
    private readonly string _temporaryRoot;
    private bool _disposed;

    private UiTestRun(string testName)
    {
        _temporaryRoot = Path.Combine(Path.GetTempPath(), $"codexcliplus-ui-{Guid.NewGuid():N}");
        RootDirectory = Path.Combine(_temporaryRoot, "app-root");
        WebViewUserDataDirectory = Path.Combine(_temporaryRoot, "webview2");
        ArtifactDirectory = Path.Combine(
            FindRepositoryRoot(),
            "artifacts",
            "ui-tests",
            SanitizeFileName(testName)
        );

        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(WebViewUserDataDirectory);
        Directory.CreateDirectory(ArtifactDirectory);
    }

    public string RootDirectory { get; }

    public string WebViewUserDataDirectory { get; }

    public string ArtifactDirectory { get; }

    public int RemoteDebuggingPort { get; private set; }

    public int ProcessId { get; private set; }

    public Process Process { get; private set; } = null!;

    public FlaUIApplication Application { get; private set; } = null!;

    public UIA3Automation Automation { get; private set; } = null!;

    public Window MainWindow { get; private set; } = null!;

    public static UiTestRun Start(string testName)
    {
        SkipIfCannotRun();

        var run = new UiTestRun(testName);
        run.RemoteDebuggingPort = FindAvailablePort();
        run.Automation = new UIA3Automation();

        var startInfo = new ProcessStartInfo
        {
            FileName = ApplicationPath,
            WorkingDirectory = Path.GetDirectoryName(ApplicationPath)!,
            UseShellExecute = false,
        };
        startInfo.Environment["CODEXCLIPLUS_APP_ROOT"] = run.RootDirectory;
        startInfo.Environment["CODEXCLIPLUS_UI_TEST_MODE"] = "1";
        startInfo.Environment["CODEXCLIPLUS_WEBVIEW2_USER_DATA_FOLDER"] =
            run.WebViewUserDataDirectory;
        startInfo.Environment["CODEXCLIPLUS_WEBVIEW2_REMOTE_DEBUGGING_PORT"] =
            run.RemoteDebuggingPort.ToString(CultureInfo.InvariantCulture);
        startInfo.Environment["TEMP"] = Path.Combine(run._temporaryRoot, "tmp");
        startInfo.Environment["TMP"] = Path.Combine(run._temporaryRoot, "tmp");
        Directory.CreateDirectory(startInfo.Environment["TEMP"]!);

        run.Process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("CodexCliPlus.exe did not start.");
        run.ProcessId = run.Process.Id;
        run.Application = FlaUIApplication.Attach(run.Process);
        run.MainWindow =
            run.Application.GetMainWindow(run.Automation, TimeSpan.FromSeconds(20))
            ?? throw new InvalidOperationException("CodexCliPlus 主窗口没有出现。");
        return run;
    }

    public AutomationElement WaitForAutomationId(string automationId, TimeSpan timeout)
    {
        return WaitFor(
            () =>
                MainWindow.FindFirstDescendant(condition => condition.ByAutomationId(automationId)),
            timeout,
            $"UI element '{automationId}' was not found."
        );
    }

    public AutomationElement? TryFindAutomationId(string automationId)
    {
        return MainWindow.FindFirstDescendant(condition => condition.ByAutomationId(automationId));
    }

    public void Click(string automationId)
    {
        var element = WaitForAutomationId(automationId, TimeSpan.FromSeconds(12));
        var invokePattern = element.Patterns.Invoke.PatternOrDefault;
        if (invokePattern is not null)
        {
            invokePattern.Invoke();
            return;
        }

        var togglePattern = element.Patterns.Toggle.PatternOrDefault;
        if (togglePattern is not null)
        {
            togglePattern.Toggle();
            return;
        }

        element.AsButton().Click();
    }

    public void Hover(string automationId)
    {
        var element = WaitForAutomationId(automationId, TimeSpan.FromSeconds(12));
        var point = element.GetClickablePoint();
        var target = new System.Drawing.Point(
            (int)Math.Round(Convert.ToDouble(point.X, CultureInfo.InvariantCulture)),
            (int)Math.Round(Convert.ToDouble(point.Y, CultureInfo.InvariantCulture))
        );
        Mouse.MoveTo(target.X, target.Y);
        Thread.Sleep(650);
    }

    public void MoveMouseRelativeToWindow(double x, double y, int settleMilliseconds = 650)
    {
        var bounds = MainWindow.BoundingRectangle;
        var target = new System.Drawing.Point(
            (int)Math.Round(bounds.Left + x),
            (int)Math.Round(bounds.Top + y)
        );
        Mouse.MoveTo(target.X, target.Y);
        Thread.Sleep(settleMilliseconds);
    }

    public void CaptureWindow(string fileName)
    {
        var bounds = MainWindow.BoundingRectangle;
        CaptureScreenRegion(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            fileName,
            padding: 0
        );
    }

    public void CaptureElement(string automationId, string fileName, int padding = 8)
    {
        var element = WaitForAutomationId(automationId, TimeSpan.FromSeconds(12));
        var bounds = element.BoundingRectangle;
        CaptureScreenRegion(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            fileName,
            padding
        );
    }

    public void CaptureElementsUnion(string fileName, int padding, params string[] automationIds)
    {
        var bounds = automationIds
            .Select(automationId =>
                WaitForAutomationId(automationId, TimeSpan.FromSeconds(12)).BoundingRectangle
            )
            .ToArray();

        if (bounds.Length == 0)
        {
            throw new ArgumentException(
                "At least one automation id is required.",
                nameof(automationIds)
            );
        }

        var left = bounds.Min(rectangle => rectangle.Left);
        var top = bounds.Min(rectangle => rectangle.Top);
        var right = bounds.Max(rectangle => rectangle.Left + rectangle.Width);
        var bottom = bounds.Max(rectangle => rectangle.Top + rectangle.Height);
        CaptureScreenRegion(left, top, right - left, bottom - top, fileName, padding);
    }

    public UiElementBounds GetElementBounds(string automationId)
    {
        var bounds = WaitForAutomationId(automationId, TimeSpan.FromSeconds(12)).BoundingRectangle;
        return new UiElementBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
    }

    public void CaptureTooltip(string tooltipText, string fileName, int padding = 8)
    {
        var tooltip = WaitFor(
            () =>
                Automation
                    .GetDesktop()
                    .FindFirstDescendant(condition =>
                        condition
                            .ByControlType(ControlType.ToolTip)
                            .And(condition.ByName(tooltipText))
                    ),
            TimeSpan.FromSeconds(3),
            $"没有显示 Tooltip：{tooltipText}"
        );
        var bounds = tooltip.BoundingRectangle;
        CaptureScreenRegion(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            fileName,
            padding
        );
    }

    public void CaptureOptionalElement(string automationId, string fileName, int padding = 8)
    {
        var element = TryFindAutomationId(automationId);
        if (element is null)
        {
            return;
        }

        var bounds = element.BoundingRectangle;
        CaptureScreenRegion(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            fileName,
            padding
        );
    }

    public void WriteUiaTree(string fileName)
    {
        var root = CreateUiaNode(MainWindow, depth: 0, maxDepth: 7);
        WriteJson(fileName, root);
    }

    public void WriteJson(string fileName, object value)
    {
        var path = Path.Combine(ArtifactDirectory, fileName);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
    }

    public void WriteReview(string fileName, string title, params string[] checklist)
    {
        var builder = new StringBuilder();
        builder.Append("# ");
        builder.AppendLine(title);
        builder.AppendLine();
        builder.AppendLine("## 人工验收清单");
        foreach (var item in checklist)
        {
            builder.Append("- ");
            builder.AppendLine(item);
        }

        builder.AppendLine();
        builder.AppendLine("## 产物");
        foreach (
            var file in Directory
                .EnumerateFiles(ArtifactDirectory)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
        )
        {
            builder.Append("- ");
            builder.AppendLine(Path.GetFileName(file));
        }

        File.WriteAllText(
            Path.Combine(ArtifactDirectory, fileName),
            builder.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
    }

    public void TrackDesktopSecurityKeyFiles()
    {
        foreach (var path in EnumerateDesktopSecurityKeyFiles())
        {
            _desktopFilesToDelete.Add(path);
        }
    }

    public string[] GetNewDesktopSecurityKeyFiles()
    {
        var before = _desktopFilesToDelete.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return EnumerateDesktopSecurityKeyFiles().Where(path => !before.Contains(path)).ToArray();
    }

    public T WaitFor<T>(Func<T?> probe, TimeSpan timeout, string failureMessage)
        where T : class
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (HasProcessExited())
            {
                throw new InvalidOperationException("CodexCliPlus.exe exited early.");
            }

            try
            {
                var result = probe();
                if (result is not null)
                {
                    return result;
                }
            }
            catch (Exception exception)
            {
                lastError = exception;
            }

            Thread.Sleep(150);
        }

        throw new TimeoutException(
            lastError is null ? failureMessage : $"{failureMessage} Last error: {lastError.Message}"
        );
    }

    public void WaitForBackendPortAvailable(TimeSpan timeout)
    {
        var port = AppConstants.DefaultBackendPort;
        WaitFor(
            () => IsLoopbackPortAvailable(port) ? string.Empty : null,
            timeout,
            $"CodexCliPlus backend port {port.ToString(CultureInfo.InvariantCulture)} is still in use."
        );
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var path in GetNewDesktopSecurityKeyFiles())
        {
            TryDeleteFile(path);
        }

        CopyRunDiagnostics();

        try
        {
            if (!HasProcessExited())
            {
                using var process = Process.GetProcessById(ProcessId);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch { }

        Application?.Dispose();
        Automation?.Dispose();

        try
        {
            if (Directory.Exists(_temporaryRoot))
            {
                Directory.Delete(_temporaryRoot, recursive: true);
            }
        }
        catch { }
    }

    private static string ApplicationPath =>
        Path.Combine(AppContext.BaseDirectory, AppConstants.ExecutableName);

    private void CaptureScreenRegion(
        double left,
        double top,
        double width,
        double height,
        string fileName,
        int padding
    )
    {
        var x = (int)Math.Floor(left) - padding;
        var y = (int)Math.Floor(top) - padding;
        var w = (int)Math.Ceiling(width) + padding * 2;
        var h = (int)Math.Ceiling(height) + padding * 2;

        if (x < 0)
        {
            w += x;
            x = 0;
        }

        if (y < 0)
        {
            h += y;
            y = 0;
        }

        w = Math.Max(1, w);
        h = Math.Max(1, h);

        using var bitmap = new Bitmap(w, h);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(x, y, 0, 0, bitmap.Size);
        }

        bitmap.Save(Path.Combine(ArtifactDirectory, fileName), ImageFormat.Png);
    }

    private static UiNode CreateUiaNode(AutomationElement element, int depth, int maxDepth)
    {
        var children =
            depth >= maxDepth
                ? []
                : element
                    .FindAllChildren()
                    .Take(80)
                    .Select(child => CreateUiaNode(child, depth + 1, maxDepth))
                    .ToArray();

        var bounds = element.BoundingRectangle;
        return new UiNode(
            element.Properties.AutomationId.ValueOrDefault ?? string.Empty,
            element.Properties.Name.ValueOrDefault ?? string.Empty,
            element.Properties.ClassName.ValueOrDefault ?? string.Empty,
            element.Properties.ControlType.ValueOrDefault.ToString(),
            new UiBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
            element.Properties.IsOffscreen.ValueOrDefault,
            children
        );
    }

    private bool HasProcessExited()
    {
        try
        {
            using var process = Process.GetProcessById(ProcessId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void SkipIfCannotRun()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("UI 自动化只在 Windows 上运行。");
        }

        if (!Environment.UserInteractive)
        {
            Assert.Skip("UI 自动化需要交互式桌面会话。");
        }

        if (!IsCurrentProcessElevated())
        {
            Assert.Skip(
                "CodexCliPlus 桌面程序要求管理员权限，UI 自动化需要在管理员测试进程中运行。"
            );
        }

        if (!File.Exists(ApplicationPath))
        {
            Assert.Skip($"未找到桌面程序：{ApplicationPath}");
        }

        try
        {
            if (
                string.IsNullOrWhiteSpace(
                    CoreWebView2Environment.GetAvailableBrowserVersionString()
                )
            )
            {
                Assert.Skip("当前系统未安装 WebView2 Runtime。");
            }
        }
        catch (Exception exception)
        {
            Assert.Skip($"无法检测 WebView2 Runtime：{exception.Message}");
        }
    }

    private static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int FindAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static bool IsLoopbackPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateDesktopSecurityKeyFiles()
    {
        var desktopDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory
        );
        return string.IsNullOrWhiteSpace(desktopDirectory) || !Directory.Exists(desktopDirectory)
            ? []
            : Directory.EnumerateFiles(
                desktopDirectory,
                "CodexCliPlus-安全密钥-*.txt",
                SearchOption.TopDirectoryOnly
            );
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }

    private void CopyRunDiagnostics()
    {
        CopyDiagnosticFile(
            Path.Combine(RootDirectory, "config", AppConstants.BackendConfigFileName)
        );
        CopyDiagnosticFile(Path.Combine(RootDirectory, "config", "appsettings.json"));

        var logsDirectory = Path.Combine(RootDirectory, "logs");
        if (!Directory.Exists(logsDirectory))
        {
            return;
        }

        var targetDirectory = Path.Combine(ArtifactDirectory, "logs");
        Directory.CreateDirectory(targetDirectory);
        foreach (
            var path in Directory.EnumerateFiles(logsDirectory, "*", SearchOption.TopDirectoryOnly)
        )
        {
            CopyDiagnosticFile(path, Path.Combine(targetDirectory, Path.GetFileName(path)));
        }
    }

    private void CopyDiagnosticFile(string sourcePath, string? targetPath = null)
    {
        try
        {
            if (File.Exists(sourcePath))
            {
                File.Copy(
                    sourcePath,
                    targetPath ?? Path.Combine(ArtifactDirectory, Path.GetFileName(sourcePath)),
                    overwrite: true
                );
            }
        }
        catch { }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodexCliPlus.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Path.Combine(Path.GetTempPath(), "codexcliplus-ui-artifacts");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return string.Concat(
            value.Select(character => invalid.Contains(character) ? '_' : character)
        );
    }
}

internal sealed record UiNode(
    string AutomationId,
    string Name,
    string ClassName,
    string ControlType,
    UiBounds Bounds,
    bool IsOffscreen,
    IReadOnlyList<UiNode> Children
);

internal sealed record UiBounds(double Left, double Top, double Width, double Height);

internal sealed record UiElementBounds(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

internal static class UiAutomationExtensions
{
    public static bool IsVisibleEnough(this AutomationElement element)
    {
        return !element.Properties.IsOffscreen.ValueOrDefault
            && element.BoundingRectangle.Width > 0
            && element.BoundingRectangle.Height > 0;
    }
}
