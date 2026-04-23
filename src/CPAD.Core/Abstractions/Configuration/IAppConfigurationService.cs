using CPAD.Core.Models;

namespace CPAD.Core.Abstractions.Configuration;

public interface IAppConfigurationService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
