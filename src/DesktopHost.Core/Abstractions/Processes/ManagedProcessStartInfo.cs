namespace DesktopHost.Core.Abstractions.Processes;

public sealed record ManagedProcessStartInfo(
    string FileName,
    string Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables = null,
    bool CaptureOutput = true,
    bool CreateNoWindow = true);
