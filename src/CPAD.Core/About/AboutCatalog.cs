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
            "Desktop shell interaction reference",
            "babalae/better-genshin-impact",
            "GPL-3.0",
            "Used as an audited WPF navigation, theme, tray, and diagnostics reference; not linked or redistributed as a component."),
        new(
            ".NET WPF and CommunityToolkit.Mvvm",
            "Desktop framework and MVVM helpers",
            "Microsoft .NET ecosystem",
            "MIT",
            "Provides the native application framework without WebView2 runtime dependency.")
    ];
}
