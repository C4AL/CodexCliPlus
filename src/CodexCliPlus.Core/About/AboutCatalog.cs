using CodexCliPlus.Core.Models;

namespace CodexCliPlus.Core.About;

public static class AboutCatalog
{
    public static IReadOnlyList<AboutComponentSource> ComponentSources { get; } =
    [
        new(
            "CodexCliPlus",
            "Minimal Windows desktop shell",
            "C4AL/CodexCliPlus",
            "MIT",
            "WPF shell for windowing, tray behavior, backend lifecycle, secure storage, updates, and the local WebView2 host."
        ),
        new(
            "CLIProxyAPI 后端",
            "Managed backend and Management API contract",
            "router-for-me/CLIProxyAPI",
            "MIT",
            "Backend binaries and API semantics are used through local process hosting and HTTP management calls."
        ),
        new(
            "CodexCliPlus 管理界面",
            "Vendored upstream frontend",
            "router-for-me/Cli-Proxy-API-Management-Center",
            "MIT",
            "The upstream frontend is vendored into the repository, built locally, and embedded as packaged static assets for the desktop WebView2 host."
        ),
        new(
            "BetterGI",
            "Desktop shell UI and resource derivative",
            "babalae/better-genshin-impact",
            "GPL-3.0",
            "CodexCliPlus redistributes BetterGI-derived shell resources, fonts, and supporting WPF assets under GPL-3.0-compliant packaging."
        ),
        new(
            ".NET WPF, WebView2, and CommunityToolkit.Mvvm",
            "Desktop framework and host integration",
            "Microsoft .NET ecosystem",
            "MIT",
            "Provides the native desktop framework, WebView2 host integration, and MVVM helpers used by the minimal desktop shell."
        ),
        new(
            "MahApps.Metro.IconPacks.Lucide",
            "Desktop shell Lucide icon controls",
            "MahApps/MahApps.Metro.IconPacks and lucide-icons/lucide",
            "MIT / ISC",
            "Provides WPF Lucide icon controls for the desktop shell theme button, risk indicator, and status dock."
        ),
        new(
            "cpa-usage-keeper",
            "Local SQLite usage event persistence design",
            "Willxup/cpa-usage-keeper@06117c79ca254a5fe5113d05768f17c335d62596",
            "MIT",
            "CodexCliPlus ports the keeper schema, event flattening, SHA-256 de-duplication, watermark overlap filtering, and raw export backup retention into the .NET desktop host."
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
