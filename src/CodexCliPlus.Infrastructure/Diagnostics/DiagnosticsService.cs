using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexCliPlus.Core.Abstractions.Build;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.LocalEnvironment;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Infrastructure.Diagnostics;

public sealed class DiagnosticsService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

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
        DependencyCheckResult dependencyStatus,
        LocalDependencySnapshot? localDependencySnapshot = null
    )
    {
        var builder = new StringBuilder();
        AppendInvariantLine(builder, $"Application version: {_buildInfo.ApplicationVersion}");
        AppendInvariantLine(builder, $"Informational version: {_buildInfo.InformationalVersion}");
        AppendInvariantLine(builder, $"Backend state: {backendStatus.State}");
        AppendInvariantLine(builder, $"Backend message: {backendStatus.Message}");
        AppendInvariantLine(
            builder,
            $"Backend process ID: {backendStatus.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "N/A"}"
        );
        AppendInvariantLine(
            builder,
            $"Backend port: {backendStatus.Runtime?.Port.ToString(CultureInfo.InvariantCulture) ?? "N/A"}"
        );
        AppendInvariantLine(
            builder,
            $"Backend port note: {backendStatus.Runtime?.PortMessage ?? "N/A"}"
        );
        AppendInvariantLine(builder, $"Codex installed: {codexStatus.IsInstalled}");
        AppendInvariantLine(builder, $"Codex version: {codexStatus.Version ?? "N/A"}");
        AppendInvariantLine(builder, $"Codex default profile: {codexStatus.DefaultProfile}");
        AppendInvariantLine(
            builder,
            $"Codex authentication state: {codexStatus.AuthenticationState}"
        );
        AppendInvariantLine(builder, $"Dependency summary: {dependencyStatus.Summary}");
        AppendInvariantLine(
            builder,
            $"Dependency repair mode: {dependencyStatus.RequiresRepairMode}"
        );
        if (localDependencySnapshot is not null)
        {
            AppendInvariantLine(
                builder,
                $"Local environment score: {localDependencySnapshot.ReadinessScore}"
            );
            AppendInvariantLine(
                builder,
                $"Local environment summary: {localDependencySnapshot.Summary}"
            );
        }

        if (dependencyStatus.Issues.Count > 0)
        {
            builder.AppendLine("Dependency issues:");
            foreach (var issue in dependencyStatus.Issues)
            {
                AppendInvariantLine(
                    builder,
                    $"- [{issue.Code}] {issue.Title} | Repair now: {issue.CanRepairNow} | {issue.Detail}"
                );
            }
        }

        AppendInvariantLine(builder, $"App root: {_pathService.Directories.RootDirectory}");
        AppendInvariantLine(builder, $"Logs directory: {_pathService.Directories.LogsDirectory}");
        AppendInvariantLine(
            builder,
            $"Config directory: {_pathService.Directories.ConfigDirectory}"
        );
        AppendInvariantLine(
            builder,
            $"Diagnostics directory: {_pathService.Directories.DiagnosticsDirectory}"
        );
        return SensitiveDataRedactor.Redact(builder.ToString());
    }

    public string ExportPackage(
        BackendStatusSnapshot backendStatus,
        CodexStatusSnapshot codexStatus,
        DependencyCheckResult dependencyStatus,
        LocalDependencySnapshot? localDependencySnapshot = null
    )
    {
        using var packageStream = CreateUniqueArtifactStream(
            _pathService.Directories.DiagnosticsDirectory,
            "diagnostics",
            ".zip",
            out var packagePath
        );
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create);
        WriteArchiveEntry(
            archive,
            "report.txt",
            BuildReport(backendStatus, codexStatus, dependencyStatus, localDependencySnapshot)
        );
        if (localDependencySnapshot is not null)
        {
            WriteArchiveEntry(
                archive,
                "local-environment.json",
                SensitiveDataRedactor.Redact(
                    JsonSerializer.Serialize(localDependencySnapshot, JsonOptions)
                )
            );
        }

        AddFileIfPresent(
            archive,
            "desktop.log",
            Path.Combine(_pathService.Directories.LogsDirectory, AppConstants.DefaultLogFileName)
        );
        AddFileIfPresent(
            archive,
            AppConstants.AppSettingsFileName,
            _pathService.Directories.SettingsFilePath
        );
        AddFileIfPresent(
            archive,
            AppConstants.BackendConfigFileName,
            _pathService.Directories.BackendConfigFilePath
        );

        return packagePath;
    }

    public string CreateErrorSnapshot(
        string title,
        string? detail,
        Exception? exception,
        BackendStatusSnapshot backendStatus,
        CodexStatusSnapshot codexStatus,
        DependencyCheckResult dependencyStatus,
        LocalDependencySnapshot? localDependencySnapshot = null
    )
    {
        using var snapshotStream = CreateUniqueArtifactStream(
            _pathService.Directories.DiagnosticsDirectory,
            "error-snapshot",
            ".txt",
            out var snapshotPath
        );

        var builder = new StringBuilder();
        builder.AppendLine(title);
        AppendInvariantLine(
            builder,
            $"Timestamp: {DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)}"
        );

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
        builder.AppendLine(
            BuildReport(backendStatus, codexStatus, dependencyStatus, localDependencySnapshot)
        );

        using var writer = new StreamWriter(snapshotStream, Utf8NoBom);
        writer.Write(SensitiveDataRedactor.Redact(builder.ToString()));
        return snapshotPath;
    }

    private static FileStream CreateUniqueArtifactStream(
        string directory,
        string prefix,
        string extension,
        out string path
    )
    {
        Directory.CreateDirectory(directory);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var timestamp = DateTimeOffset.Now.ToString(
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture
            );
            var suffix = Guid.NewGuid().ToString("N")[..12];
            path = Path.Combine(directory, $"{prefix}-{timestamp}-{suffix}{extension}");

            try
            {
                return new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException) when (File.Exists(path))
            { }
        }

        throw new IOException($"Failed to create unique diagnostics artifact '{prefix}'.");
    }

    private static void AppendInvariantLine(StringBuilder builder, FormattableString value)
    {
        builder.AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteArchiveEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(
            stream,
            Utf8NoBom
        );
        writer.Write(content);
    }

    private static void AddFileIfPresent(ZipArchive archive, string entryName, string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        try
        {
            WriteArchiveEntry(
                archive,
                entryName,
                SensitiveDataRedactor.Redact(File.ReadAllText(sourcePath))
            );
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
