using System.Diagnostics.CodeAnalysis;

namespace CodexCliPlus.ViewModels;

[SuppressMessage(
    "Performance",
    "CA1822:Mark members as static",
    Justification = "WPF binding requires instance properties."
)]
public sealed class NotifyIconViewModel
{
    public string OpenLabel => "打开主界面";

    public string RestartBackendLabel => "重启后端";

    public string CheckUpdatesLabel => "检查更新";

    public string ExitLabel => "退出并停止后端";
}
