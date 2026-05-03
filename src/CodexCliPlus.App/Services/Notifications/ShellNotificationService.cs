namespace CodexCliPlus.Services.Notifications;

public sealed class ShellNotificationService
{
    public event EventHandler<ShellNotificationRequest>? NotificationRequested;

    public void ShowAuto(
        string message,
        ShellNotificationLevel level = ShellNotificationLevel.Success
    )
    {
        NotificationRequested?.Invoke(
            this,
            new ShellNotificationRequest(
                ShellNotificationPlacement.BottomCenterAuto,
                string.Empty,
                message,
                level
            )
        );
    }

    public void ShowManual(
        string title,
        string message,
        ShellNotificationLevel? level = null
    )
    {
        NotificationRequested?.Invoke(
            this,
            new ShellNotificationRequest(
                ShellNotificationPlacement.BottomRightManual,
                title,
                message,
                level ?? ResolveManualLevel(title)
            )
        );
    }

    public void ShowShellNotification(string message, ShellNotificationLevel level)
    {
        if (level == ShellNotificationLevel.Error)
        {
            ShowManual("操作失败", message, ShellNotificationLevel.Error);
            return;
        }

        ShowAuto(message, level);
    }

    private static ShellNotificationLevel ResolveManualLevel(string title)
    {
        if (
            title.Contains("失败", StringComparison.Ordinal)
            || title.Contains("错误", StringComparison.Ordinal)
            || title.Contains("不可用", StringComparison.Ordinal)
        )
        {
            return ShellNotificationLevel.Error;
        }

        return ShellNotificationLevel.Warning;
    }
}
