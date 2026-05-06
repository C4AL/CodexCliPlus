namespace CodexCliPlus.Core.Constants;

public static class LocalDependencyRepairActionIds
{
    public const string InstallNodeNpm = "install-node-npm";
    public const string InstallPowerShell = "install-powershell";
    public const string RepairWinget = "repair-winget";
    public const string InstallWsl = "install-wsl";
    public const string UpdateWsl = "update-wsl";
    public const string InstallCodexCli = "install-codex-cli";
    public const string RepairUserPath = "repair-user-path";

    public static bool IsKnown(string actionId)
    {
        return actionId
            is InstallNodeNpm
                or InstallPowerShell
                or RepairWinget
                or InstallWsl
                or UpdateWsl
                or InstallCodexCli
                or RepairUserPath;
    }
}
