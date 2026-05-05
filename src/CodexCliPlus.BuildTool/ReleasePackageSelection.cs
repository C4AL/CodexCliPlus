namespace CodexCliPlus.BuildTool;

[Flags]
public enum ReleasePackageSelection
{
    None = 0,
    OnlineInstaller = 1,
    OfflineInstaller = 2,
    UpdatePackage = 4,
    All = OnlineInstaller | OfflineInstaller | UpdatePackage,
}

public static class ReleasePackageSelectionExtensions
{
    public static bool IncludesOnlineInstaller(this ReleasePackageSelection selection)
    {
        return selection.HasFlag(ReleasePackageSelection.OnlineInstaller);
    }

    public static bool IncludesOfflineInstaller(this ReleasePackageSelection selection)
    {
        return selection.HasFlag(ReleasePackageSelection.OfflineInstaller);
    }

    public static bool IncludesUpdatePackage(this ReleasePackageSelection selection)
    {
        return selection.HasFlag(ReleasePackageSelection.UpdatePackage);
    }

    public static string ToDisplayString(this ReleasePackageSelection selection)
    {
        if (selection == ReleasePackageSelection.All)
        {
            return "all";
        }

        var values = ToManifestValues(selection);
        return values.Length == 0 ? "none" : string.Join(",", values);
    }

    public static string[] ToManifestValues(this ReleasePackageSelection selection)
    {
        var values = new List<string>();
        if (selection.IncludesOnlineInstaller())
        {
            values.Add("online");
        }

        if (selection.IncludesOfflineInstaller())
        {
            values.Add("offline");
        }

        if (selection.IncludesUpdatePackage())
        {
            values.Add("update");
        }

        return values.ToArray();
    }

    public static bool TryParse(
        string value,
        out ReleasePackageSelection selection,
        out string? error
    )
    {
        selection = ReleasePackageSelection.None;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Release package selection cannot be empty.";
            return false;
        }

        var tokens = value.Split(
            [',', ';', '|', '+'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (tokens.Length == 0)
        {
            error = "Release package selection cannot be empty.";
            return false;
        }

        foreach (var token in tokens)
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "all":
                case "*":
                    selection = ReleasePackageSelection.All;
                    break;
                case "online":
                case "online-installer":
                case "package-online-installer":
                    selection |= ReleasePackageSelection.OnlineInstaller;
                    break;
                case "offline":
                case "offline-installer":
                case "package-offline-installer":
                    selection |= ReleasePackageSelection.OfflineInstaller;
                    break;
                case "update":
                case "update-package":
                case "package-update":
                    selection |= ReleasePackageSelection.UpdatePackage;
                    break;
                default:
                    error =
                        $"Unknown release package selection '{token}'. Expected all, online, offline, or update.";
                    return false;
            }
        }

        if (selection == ReleasePackageSelection.None)
        {
            error = "Release package selection cannot be empty.";
            return false;
        }

        return true;
    }
}
