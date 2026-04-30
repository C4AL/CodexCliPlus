using CodexCliPlus.Core.Models;

namespace CodexCliPlus.Core.About;

public static class AboutCatalog
{
    public static IReadOnlyList<AboutComponentSource> ComponentSources { get; } =
    [
        new(
            "CodexCliPlus",
            "轻量 Windows 桌面外壳",
            "C4AL/CodexCliPlus",
            "MIT",
            "提供窗口、托盘行为、后端生命周期、安全存储、更新和本地 WebView2 宿主所需的 WPF 桌面外壳。"
        ),
        new(
            "CLIProxyAPI 后端",
            "托管后端与管理 API 契约",
            "router-for-me/CLIProxyAPI",
            "MIT",
            "通过本地进程托管和 HTTP 管理调用使用后端二进制文件与 API 语义。"
        ),
        new(
            "CodexCliPlus 管理界面",
            "内置上游前端",
            "router-for-me/Cli-Proxy-API-Management-Center",
            "MIT",
            "上游前端随仓库内置，在本地构建后作为打包静态资源嵌入桌面 WebView2 宿主。"
        ),
        new(
            "BetterGI",
            "桌面外壳 UI 与资源派生来源",
            "babalae/better-genshin-impact",
            "GPL-3.0",
            "CodexCliPlus 按 GPL-3.0 合规打包方式再分发派生自 BetterGI 的外壳资源、字体和配套 WPF 资产。"
        ),
        new(
            ".NET WPF, WebView2, and CommunityToolkit.Mvvm",
            "桌面框架与宿主集成",
            "Microsoft .NET ecosystem",
            "MIT",
            "提供轻量桌面外壳使用的原生桌面框架、WebView2 宿主集成和 MVVM 辅助能力。"
        ),
        new(
            "MahApps.Metro.IconPacks.Lucide",
            "桌面外壳 Lucide 图标控件",
            "MahApps/MahApps.Metro.IconPacks and lucide-icons/lucide",
            "MIT / ISC",
            "为桌面外壳的主题按钮、风险指示器和状态栏提供 WPF Lucide 图标控件。"
        ),
        new(
            "cpa-usage-keeper",
            "本地 SQLite 用量事件持久化设计",
            "Willxup/cpa-usage-keeper@06117c79ca254a5fe5113d05768f17c335d62596",
            "MIT",
            "CodexCliPlus 将 keeper 表结构、事件扁平化、SHA-256 去重、水位重叠过滤和原始导出备份保留逻辑移植到 .NET 桌面宿主。"
        ),
    ];

    public static IReadOnlyList<AboutLicenseDocument> LicenseDocuments { get; } =
    [
        new(
            "桌面程序许可",
            "MIT",
            "CodexCliPlus.LICENSE.txt",
            "LICENSE.txt",
            "CodexCliPlus 原创桌面应用代码遵循 MIT 许可。"
        ),
        new(
            "后端许可",
            "MIT",
            "CLIProxyAPI.LICENSE.txt",
            Path.Combine("resources", "backend", "windows-x64", "LICENSE"),
            "CLIProxyAPI 后端二进制与契约遵循 MIT 许可。"
        ),
        new(
            "前端界面许可",
            "MIT",
            "CliProxyApiManagementCenter.LICENSE.txt",
            Path.Combine("resources", "webui", "upstream", "source", "LICENSE"),
            "Vendored 前端源码与静态资源遵循 MIT 许可。"
        ),
        new(
            "BetterGI 派生 UI 许可",
            "GPL-3.0",
            "BetterGI.GPL-3.0.txt",
            Path.Combine("resources", "licenses", "BetterGI.GPL-3.0.txt"),
            "BetterGI 派生的桌面外壳、字体和资源按 GPL-3.0 再分发。"
        ),
        new(
            "cpa-usage-keeper 派生持久化许可",
            "MIT",
            "cpa-usage-keeper.MIT.txt",
            Path.Combine("resources", "licenses", "cpa-usage-keeper.MIT.txt"),
            "本地 SQLite 统计持久化逻辑移植自 cpa-usage-keeper。"
        ),
        new(
            "合并 NOTICE",
            "NOTICE",
            "NOTICE.txt",
            Path.Combine("resources", "licenses", "NOTICE.txt"),
            "发行包中的合并来源与许可说明。"
        ),
    ];
}
