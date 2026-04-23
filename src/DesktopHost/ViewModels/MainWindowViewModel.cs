using CommunityToolkit.Mvvm.ComponentModel;

namespace DesktopHost.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string windowTitle = "CPAD";

    [ObservableProperty]
    private string headerTitle = "Cli Proxy API Desktop";

    [ObservableProperty]
    private string headerSubtitle = "Windows 原生桌面宿主，承载原版 CLIProxyAPI 与原版 Management WebUI。";

    [ObservableProperty]
    private string backendBadgeText = "后端未启动";

    [ObservableProperty]
    private string codexBadgeText = "Codex 未检测";

    [ObservableProperty]
    private string currentSourceText = "official";

    [ObservableProperty]
    private string backendStatusText = "后端尚未启动。";

    [ObservableProperty]
    private string backendDetailText = "等待启动 CLIProxyAPI。";

    [ObservableProperty]
    private string webViewStatusText = "等待 WebView2 Runtime 检测。";

    [ObservableProperty]
    private string codexSummaryText = "尚未检测 Codex CLI。";

    [ObservableProperty]
    private string codexDetailText = "点击“检测 Codex”后读取版本、认证状态和 profile。";

    [ObservableProperty]
    private string commandPreviewText = "codex --profile official";

    [ObservableProperty]
    private string backendLogText = "暂无日志。";

    [ObservableProperty]
    private string appDataPath = string.Empty;

    [ObservableProperty]
    private string logsPath = string.Empty;

    [ObservableProperty]
    private string versionText = string.Empty;

    [ObservableProperty]
    private string informationalVersionText = string.Empty;

    [ObservableProperty]
    private string repositoryPath = string.Empty;

    [ObservableProperty]
    private string footerStatus = "等待初始化。";

    [ObservableProperty]
    private string diagnosticsSummaryText = "诊断信息尚未生成。";

    [ObservableProperty]
    private bool startWithWindows;
}
