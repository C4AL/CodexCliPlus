namespace CPAD.Core.Abstractions.Logging;

public interface IAppLogger
{
    string LogFilePath { get; }

    void Info(string message);

    void Warn(string message);

    void LogError(string message, Exception? exception = null);
}
