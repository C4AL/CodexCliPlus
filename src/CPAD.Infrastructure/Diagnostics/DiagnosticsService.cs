using System.Globalization;
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
        AppendInvariantLine(builder, $"Application version: {_buildInfo.ApplicationVersion}");
        AppendInvariantLine(builder, $"Informational version: {_buildInfo.InformationalVersion}");
        AppendInvariantLine(builder, $"Backend state: {backendStatus.State}");
        AppendInvariantLine(builder, $"Backend message: {backendStatus.Message}");
        AppendInvariantLine(builder, $"Backend process ID: {backendStatus.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "N/A"}");
        AppendInvariantLine(builder, $"Backend port: {backendStatus.Runtime?.Port.ToString(CultureInfo.InvariantCulture) ?? "N/A"}");
        AppendInvariantLine(builder, $"Backend port note: {backendStatus.Runtime?.PortMessage ?? "N/A"}");
        AppendInvariantLine(builder, $"Codex installed: {codexStatus.IsInstalled}");
        AppendInvariantLine(builder, $"Codex version: {codexStatus.Version ?? "N/A"}");
        AppendInvariantLine(builder, $"Codex default profile: {codexStatus.DefaultProfile}");
        AppendInvariantLine(builder, $"Codex authentication state: {codexStatus.AuthenticationState}");
        AppendInvariantLine(builder, $"Dependency summary: {dependencyStatus.Summary}");
        AppendInvariantLine(builder, $"Dependency repair mode: {dependencyStatus.RequiresRepairMode}");

        if (dependencyStatus.Issues.Count > 0)
        {
            builder.AppendLine("Dependency issues:");
            foreach (var issue in dependencyStatus.Issues)
            {
                AppendInvariantLine(
                    builder,
                    $"- [{issue.Code}] {issue.Title} | Repair now: {issue.CanRepairNow} | {issue.Detail}");
            }
        }

        AppendInvariantLine(builder, $"App root: {_pathService.Directories.RootDirectory}");
        AppendInvariantLine(builder, $"Logs directory: {_pathService.Directories.LogsDirectory}");
        AppendInvariantLine(builder, $"Config directory: {_pathService.Directories.ConfigDirectory}");
        AppendInvariantLine(builder, $"Diagnostics directory: {_pathService.Directories.DiagnosticsDirectory}");
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
            $"diagnostics-{DateTimeOffset.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}.zip");

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
            $"error-snapshot-{DateTimeOffset.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}.txt");

        var builder = new StringBuilder();
        builder.AppendLine(title);
        AppendInvariantLine(builder, $"Timestamp: {DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)}");

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

    private static void AppendInvariantLine(StringBuilder builder, FormattableString value)
    {
        builder.AppendLine(value.ToString(CultureInfo.InvariantCulture));
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
