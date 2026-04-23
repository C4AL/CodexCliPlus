using DesktopHost.Core.Models;

namespace DesktopHost.Core.Abstractions.Configuration;

public interface IDesktopConfigurationService
{
    Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(DesktopSettings settings, CancellationToken cancellationToken = default);
}
