using System.Text;

using CPAD.Core.Abstractions.Logging;
using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Constants;
using CPAD.Infrastructure.Security;

namespace CPAD.Infrastructure.Logging;

public sealed class FileAppLogger : IAppLogger
{
    private readonly object _gate = new();

    public FileAppLogger(IPathService pathService)
    {
        Directory.CreateDirectory(pathService.Directories.LogsDirectory);
        LogFilePath = Path.Combine(pathService.Directories.LogsDirectory, AppConstants.DefaultLogFileName);
    }

    public string LogFilePath { get; }

    public void Info(string message)
    {
        Write("INF", message);
    }

    public void Warn(string message)
    {
        Write("WRN", message);
    }

    public void LogError(string message, Exception? exception = null)
    {
        var fullMessage = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Write("ERR", fullMessage);
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] {SensitiveDataRedactor.Redact(message)}{Environment.NewLine}";
        lock (_gate)
        {
            File.AppendAllText(LogFilePath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
