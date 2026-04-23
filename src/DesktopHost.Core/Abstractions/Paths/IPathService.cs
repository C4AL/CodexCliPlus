using DesktopHost.Core.Models;

namespace DesktopHost.Core.Abstractions.Paths;

public interface IPathService
{
    AppDirectories Directories { get; }

    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);
}
