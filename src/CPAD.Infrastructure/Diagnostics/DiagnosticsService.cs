using System.Text;

using CPAD.Core.Abstractions.Build;
using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Models;

namespace CPAD.Infrastructure.Diagnostics;

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
        builder.AppendLine($"Application version: {_buildInfo.ApplicationVersion}");
        builder.AppendLine($"Informational version: {_buildInfo.InformationalVersion}");
        builder.AppendLine($"Backend state: {backendStatus.State}");
        builder.AppendLine($"Backend message: {backendStatus.Message}");
        builder.AppendLine($"Backend process ID: {backendStatus.ProcessId?.ToString() ?? "N/A"}");
        builder.AppendLine($"Backend port: {backendStatus.Runtime?.Port.ToString() ?? "N/A"}");
        builder.AppendLine($"Backend port note: {backendStatus.Runtime?.PortMessage ?? "N/A"}");
        builder.AppendLine($"Codex installed: {codexStatus.IsInstalled}");
        builder.AppendLine($"Codex version: {codexStatus.Version ?? "N/A"}");
        builder.AppendLine($"Codex default profile: {codexStatus.DefaultProfile}");
        builder.AppendLine($"Codex authentication state: {codexStatus.AuthenticationState}");
        builder.AppendLine($"Dependency summary: {webViewRuntimeStatus.Summary}");
        builder.AppendLine($"App root: {_pathService.Directories.RootDirectory}");
        builder.AppendLine($"Logs directory: {_pathService.Directories.LogsDirectory}");
        builder.AppendLine($"Config directory: {_pathService.Directories.ConfigDirectory}");
        builder.AppendLine($"Diagnostics directory: {_pathService.Directories.DiagnosticsDirectory}");
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
        Directory.CreateDirectory(_pathService.Directories.DiagnosticsDirectory);

        var snapshotPath = Path.Combine(
            _pathService.Directories.DiagnosticsDirectory,
            $"error-snapshot-{DateTimeOffset.Now:yyyyMMddHHmmss}.txt");

        var builder = new StringBuilder();
        builder.AppendLine(title);
        builder.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");

        if (!string.IsNullOrWhiteSpace(detail))
        {
            builder.AppendLine();
            builder.AppendLine("Detail:");
            builder.AppendLine(detail);
        }

        if (exception is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Exception:");
            builder.AppendLine(exception.ToString());
        }

        builder.AppendLine();
        builder.AppendLine("Environment report:");
        builder.AppendLine(BuildReport(backendStatus, codexStatus, webViewRuntimeStatus));

        File.WriteAllText(snapshotPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return snapshotPath;
    }
}
