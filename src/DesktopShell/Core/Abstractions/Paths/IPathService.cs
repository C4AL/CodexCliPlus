using CodexCliPlus.Core.Models;

namespace CodexCliPlus.Core.Abstractions.Paths;

public interface IPathService
{
    AppDirectories Directories { get; }

    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);
}
