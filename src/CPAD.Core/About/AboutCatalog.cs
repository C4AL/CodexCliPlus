using CPAD.Core.Models;

namespace CPAD.Core.About;

public static class AboutCatalog
{
    public static IReadOnlyList<AboutComponentSource> ComponentSources { get; } =
    [
        new(
            "Cli Proxy API Desktop",
            "Native Windows desktop shell",
            "Blackblock-inc/Cli-Proxy-API-Desktop",
            "MIT",
            "WPF implementation for backend hosting, source switching, settings, updates, and diagnostics."),
        new(
            "CLIProxyAPI / CPA-UV",
            "Managed backend and Management API contract",
            "router-for-me/CLIProxyAPI plus the audited CPA-UV community release line",
            "MIT",
            "Backend binaries and API semantics are used through local process hosting and HTTP management calls."),
        new(
            "CLI Proxy API Management Center",
            "Management workflow reference",
            "router-for-me/Cli-Proxy-API-Management-Center and Blackblock audited mirror",
            "MIT",
            "Pages are reimplemented natively; the web UI and management.html are not embedded."),
        new(
            "BetterGI",
            "Desktop shell UI and resource derivative",
            "babalae/better-genshin-impact",
            "GPL-3.0",
            "CPAD redistributes BetterGI-derived navigation shell structure, fonts, and WPF resource files under GPL-3.0-compliant packaging."),
        new(
            ".NET WPF and CommunityToolkit.Mvvm",
            "Desktop framework and MVVM helpers",
            "Microsoft .NET ecosystem",
            "MIT",
            "Provides the native application framework without WebView2 runtime dependency.")
    ];

    public static IReadOnlyList<AboutLicenseDocument> LicenseDocuments { get; } =
    [
        new(
            "桌面程序许可",
            "MIT",
            "CPAD.LICENSE.txt",
            "LICENSE.txt",
            "CPAD 原创桌面应用代码的 MIT 许可。"),
        new(
            "后端许可",
            "MIT",
            "CLIProxyAPI.LICENSE.txt",
            Path.Combine("resources", "backend", "windows-x64", "LICENSE"),
            "CLIProxyAPI / CPA-UV 后端二进制与契约的 MIT 许可。"),
        new(
            "BetterGI 派生 UI 许可",
            "GPL-3.0",
            "BetterGI.GPL-3.0.txt",
            Path.Combine("resources", "licenses", "BetterGI.GPL-3.0.txt"),
            "BetterGI 派生的 UI 外壳、字体和资源按 GPL-3.0 再分发。"),
        new(
            "合并 NOTICE",
            "NOTICE",
            "NOTICE.txt",
            Path.Combine("resources", "licenses", "NOTICE.txt"),
            "发行包中的合并来源与许可说明。")
    ];
}
