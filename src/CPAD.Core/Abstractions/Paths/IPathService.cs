using CPAD.Core.Models;

namespace CPAD.Core.Abstractions.Paths;

public interface IPathService
{
    AppDirectories Directories { get; }

    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);
}
