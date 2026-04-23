using System.Diagnostics;

using CPAD.Core.Abstractions.Paths;

namespace CPAD.Infrastructure.Platform;

public sealed class DirectoryAccessService
{
    private readonly IPathService _pathService;

    public DirectoryAccessService(IPathService pathService)
    {
        _pathService = pathService;
    }

    public string GetLogsDirectory() => _pathService.Directories.LogsDirectory;

    public string GetConfigDirectory() => _pathService.Directories.ConfigDirectory;

    public string GetBackendDirectory() => _pathService.Directories.BackendDirectory;

    public void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public string? GetWriteAccessError(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probeFile = Path.Combine(path, $".cpad-write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probeFile, "probe");
            File.Delete(probeFile);
            return null;
        }
        catch (Exception exception)
        {
            return $"Directory is not writable: {exception.Message}";
        }
    }
}
