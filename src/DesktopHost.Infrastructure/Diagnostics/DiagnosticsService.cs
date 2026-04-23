using System.Text;

using DesktopHost.Core.Abstractions.Build;
using DesktopHost.Core.Abstractions.Paths;
using DesktopHost.Core.Models;

namespace DesktopHost.Infrastructure.Diagnostics;

public sealed class DiagnosticsService
{
    private readonly IPathService _pathService;
    private readonly IBuildInfo _buildInfo;

    public DiagnosticsService(IPathService pathService, IBuildInfo buildInfo)
    {
        _pathService = pathService;
        _buildInfo = buildInfo;
    }

    public string BuildReport(
        BackendStatusSnapshot backendStatus,
        CodexStatusSnapshot codexStatus,
        DependencyCheckResult webViewRuntimeStatus)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"应用版本：{_buildInfo.ApplicationVersion}");
        builder.AppendLine($"信息版本：{_buildInfo.InformationalVersion}");
        builder.AppendLine($"后端状态：{backendStatus.State}");
        builder.AppendLine($"后端说明：{backendStatus.Message}");
        builder.AppendLine($"后端进程：{backendStatus.ProcessId?.ToString() ?? "无"}");
        builder.AppendLine($"后端端口：{backendStatus.Runtime?.Port.ToString() ?? "未知"}");
        builder.AppendLine($"端口说明：{backendStatus.Runtime?.PortMessage ?? "无"}");
        builder.AppendLine($"Codex 已安装：{(codexStatus.IsInstalled ? "是" : "否")}");
        builder.AppendLine($"Codex 版本：{codexStatus.Version ?? "未知"}");
        builder.AppendLine($"Codex 默认 profile：{codexStatus.DefaultProfile}");
        builder.AppendLine($"Codex 认证状态：{codexStatus.AuthenticationState}");
        builder.AppendLine($"WebView2：{webViewRuntimeStatus.Summary}");
        builder.AppendLine($"应用数据目录：{_pathService.Directories.RootDirectory}");
        builder.AppendLine($"日志目录：{_pathService.Directories.LogsDirectory}");
        builder.AppendLine($"配置目录：{_pathService.Directories.ConfigDirectory}");

        return builder.ToString();
    }

    public string CreateErrorSnapshot(
        string title,
        string? detail,
        Exception? exception,
        BackendStatusSnapshot backendStatus,
        CodexStatusSnapshot codexStatus,
        DependencyCheckResult webViewRuntimeStatus)
    {
        Directory.CreateDirectory(_pathService.Directories.LogsDirectory);

        var snapshotPath = Path.Combine(
            _pathService.Directories.LogsDirectory,
            $"error-snapshot-{DateTimeOffset.Now:yyyyMMddHHmmss}.txt");

        var builder = new StringBuilder();
        builder.AppendLine(title);
        builder.AppendLine($"时间：{DateTimeOffset.Now:O}");

        if (!string.IsNullOrWhiteSpace(detail))
        {
            builder.AppendLine();
            builder.AppendLine("详细信息：");
            builder.AppendLine(detail);
        }

        if (exception is not null)
        {
            builder.AppendLine();
            builder.AppendLine("异常：");
            builder.AppendLine(exception.ToString());
        }

        builder.AppendLine();
        builder.AppendLine("诊断摘要：");
        builder.AppendLine(BuildReport(backendStatus, codexStatus, webViewRuntimeStatus));

        File.WriteAllText(snapshotPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return snapshotPath;
    }
}
