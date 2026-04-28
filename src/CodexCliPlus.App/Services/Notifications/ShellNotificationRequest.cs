namespace CodexCliPlus.Services.Notifications;

public enum ShellNotificationPlacement
{
    BottomCenterAuto,
    BottomRightManual
}

public sealed record ShellNotificationRequest(
    ShellNotificationPlacement Placement,
    string Title,
    string Message);
