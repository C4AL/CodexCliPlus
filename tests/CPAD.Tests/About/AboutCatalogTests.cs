using CPAD.Core.About;

namespace CPAD.Tests.About;

public sealed class AboutCatalogTests
{
    [Fact]
    public void ComponentSourcesExposeRequiredAboutPageContract()
    {
        var sources = AboutCatalog.ComponentSources;

        Assert.Contains(sources, item => item.Name == "Cli Proxy API Desktop" && item.License == "MIT");
        Assert.Contains(sources, item => item.Name.Contains("CLIProxyAPI", StringComparison.Ordinal) && item.License == "MIT");
        Assert.Contains(sources, item => item.Name == "CLI Proxy API Management Center" && item.Notes.Contains("not embedded", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sources, item => item.Name == "BetterGI" && item.License == "GPL-3.0");
        Assert.Contains(sources, item => item.Notes.Contains("without WebView2", StringComparison.OrdinalIgnoreCase));
    }
}
