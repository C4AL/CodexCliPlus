using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using CodexCliPlus.Core.Abstractions.Paths;

namespace CodexCliPlus.Infrastructure.Platform;

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

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance member is resolved through dependency injection.")]
    public void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance member is resolved through dependency injection.")]
    public string? GetWriteAccessError(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probeFile = Path.Combine(path, $".codexcliplus-write-test-{Guid.NewGuid():N}.tmp");
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
