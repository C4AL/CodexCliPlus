using System.IO.Compression;
using System.Text;

using CPAD.Core.Abstractions.Build;
using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Constants;
using CPAD.Core.Models;
using CPAD.Infrastructure.Security;

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
        DependencyCheckResult dependencyStatus)
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
        builder.AppendLine($"Dependency summary: {dependencyStatus.Summary}");
        builder.AppendLine($"Dependency repair mode: {dependencyStatus.RequiresRepairMode}");

        if (dependencyStatus.Issues.Count > 0)
        {
            builder.AppendLine("Dependency issues:");
            foreach (var issue in dependencyStatus.Issues)
            {
                builder.AppendLine(
                    $"- [{issue.Code}] {issue.Title} | Repair now: {issue.CanRepairNow} | {issue.Detail}");
            }
        }

        builder.AppendLine($"App root: {_pathService.Directories.RootDirectory}");
        builder.AppendLine($"Logs directory: {_pathService.Directories.LogsDirectory}");
        builder.AppendLine($"Config directory: {_pathService.Directories.ConfigDirectory}");
        builder.AppendLine($"Diagnostics directory: {_pathService.Directories.DiagnosticsDirectory}");
        return SensitiveDataRedactor.Redact(builder.ToString());
    }

    public string ExportPackage(
        BackendStatusSnapshot backendStatus,
        CodexStatusSnapshot codexStatus,
        DependencyCheckResult dependencyStatus)
    {
        Directory.CreateDirectory(_pathService.Directories.DiagnosticsDirectory);

        var packagePath = Path.Combine(
            _pathService.Directories.DiagnosticsDirectory,
            $"diagnostics-{DateTimeOffset.Now:yyyyMMddHHmmss}.zip");

        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        WriteArchiveEntry(archive, "report.txt", BuildReport(backendStatus, codexStatus, dependencyStatus));
        AddFileIfPresent(archive, "desktop.log", Path.Combine(_pathService.Directories.LogsDirectory, AppConstants.DefaultLogFileName));
        AddFileIfPresent(archive, "desktop.json", _pathService.Directories.SettingsFilePath);
        AddFileIfPresent(archive, "cliproxyapi.yaml", _pathService.Directories.BackendConfigFilePath);

        return packagePath;
    }

    public string CreateErrorSnapshot(
        string title,
        string? detail,
        Exception? exception,
        BackendStatusSnapshot backendStatus,
        CodexStatusSnapshot codexStatus,
        DependencyCheckResult dependencyStatus)
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
            builder.AppendLine(SensitiveDataRedactor.Redact(detail));
        }

        if (exception is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Exception:");
            builder.AppendLine(SensitiveDataRedactor.Redact(exception.ToString()));
        }

        builder.AppendLine();
        builder.AppendLine("Environment report:");
        builder.AppendLine(BuildReport(backendStatus, codexStatus, dependencyStatus));

        File.WriteAllText(snapshotPath, SensitiveDataRedactor.Redact(builder.ToString()), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return snapshotPath;
    }

    private static void WriteArchiveEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static void AddFileIfPresent(ZipArchive archive, string entryName, string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        WriteArchiveEntry(archive, entryName, SensitiveDataRedactor.Redact(File.ReadAllText(sourcePath)));
    }
}
