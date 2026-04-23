using CPAD.Core.Enums;
using CPAD.Core.Models;

namespace CPAD.Core.Abstractions.Updates;

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        UpdateChannel channel = UpdateChannel.Stable,
        CancellationToken cancellationToken = default);
}
