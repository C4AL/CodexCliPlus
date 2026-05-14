namespace CodexCliPlus.Services.Notifications;

public enum ShellNotificationPlacement
{
    BottomCenterAuto,
    BottomRightManual,
}

public enum ShellNotificationLevel
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record ShellNotificationAction(string Id, string Text, Action Invoke);

public sealed record ShellNotificationRequest(
    ShellNotificationPlacement Placement,
    string Title,
    string Message,
    ShellNotificationLevel Level = ShellNotificationLevel.Info,
    IReadOnlyList<ShellNotificationAction>? Actions = null
);
