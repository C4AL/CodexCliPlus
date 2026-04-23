using CPAD.Core.Models;

namespace CPAD.Core.Abstractions.Updates;

public interface IUpdateInstallerService
{
    bool CanPrepareInstaller(UpdateCheckResult updateCheckResult);

    Task<PreparedUpdateInstaller> DownloadInstallerAsync(
        UpdateCheckResult updateCheckResult,
        CancellationToken cancellationToken = default);

    Task LaunchInstallerAsync(
        PreparedUpdateInstaller installer,
        CancellationToken cancellationToken = default);
}
