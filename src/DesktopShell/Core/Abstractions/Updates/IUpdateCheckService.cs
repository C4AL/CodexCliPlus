using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;

namespace CodexCliPlus.Core.Abstractions.Updates;

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        UpdateChannel channel = UpdateChannel.Stable,
        CancellationToken cancellationToken = default
    );
}
