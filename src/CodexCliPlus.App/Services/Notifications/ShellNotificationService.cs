namespace CodexCliPlus.Services.Notifications;

public sealed class ShellNotificationService
{
    public event EventHandler<ShellNotificationRequest>? NotificationRequested;

    public void ShowAuto(string message)
    {
        NotificationRequested?.Invoke(
            this,
            new ShellNotificationRequest(
                ShellNotificationPlacement.BottomCenterAuto,
                string.Empty,
                message
            )
        );
    }

    public void ShowManual(string title, string message)
    {
        NotificationRequested?.Invoke(
            this,
            new ShellNotificationRequest(
                ShellNotificationPlacement.BottomRightManual,
                title,
                message
            )
        );
    }
}
