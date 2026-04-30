using CodexCliPlus.Core.About;

namespace CodexCliPlus.Tests.About;

public sealed class AboutCatalogTests
{
    [Fact]
    public void ComponentSourcesExposeRequiredAboutPageContract()
    {
        var sources = AboutCatalog.ComponentSources;

        Assert.Contains(sources, item => item.Name == "CodexCliPlus" && item.License == "MIT");
        Assert.Contains(
            sources,
            item =>
                item.Name.Contains("CLIProxyAPI", StringComparison.Ordinal) && item.License == "MIT"
        );
        Assert.Contains(
            sources,
            item =>
                item.Name == "CodexCliPlus 管理界面"
                && item.Notes.Contains("内置", StringComparison.Ordinal)
        );
        Assert.Contains(
            sources,
            item =>
                item.Name == "BetterGI"
                && item.License == "GPL-3.0"
                && item.Notes.Contains("外壳资源", StringComparison.Ordinal)
        );
        Assert.Contains(
            sources,
            item => item.Name == "MahApps.Metro.IconPacks.Lucide" && item.License == "MIT / ISC"
        );
        Assert.Contains(
            sources,
            item =>
                item.Name == "cpa-usage-keeper"
                && item.License == "MIT"
                && item.Origin.Contains("06117c79", StringComparison.Ordinal)
        );
        Assert.Contains(
            sources,
            item => item.Notes.Contains("WebView2", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void LicenseDocumentsExposeMergedRedistributionNotice()
    {
        var documents = AboutCatalog.LicenseDocuments;

        Assert.Contains(
            documents,
            item => item.OutputFileName == "CodexCliPlus.LICENSE.txt" && item.License == "MIT"
        );
        Assert.Contains(
            documents,
            item => item.OutputFileName == "CLIProxyAPI.LICENSE.txt" && item.License == "MIT"
        );
        Assert.Contains(
            documents,
            item =>
                item.OutputFileName == "CliProxyApiManagementCenter.LICENSE.txt"
                && item.License == "MIT"
        );
        Assert.Contains(
            documents,
            item => item.OutputFileName == "BetterGI.GPL-3.0.txt" && item.License == "GPL-3.0"
        );
        Assert.Contains(
            documents,
            item => item.OutputFileName == "cpa-usage-keeper.MIT.txt" && item.License == "MIT"
        );
        Assert.Contains(
            documents,
            item => item.OutputFileName == "NOTICE.txt" && item.License == "NOTICE"
        );
    }
}
