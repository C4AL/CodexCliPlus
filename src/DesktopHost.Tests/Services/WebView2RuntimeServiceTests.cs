using DesktopHost.Services;

using Microsoft.Web.WebView2.Core;

namespace DesktopHost.Tests.Services;

public sealed class WebView2RuntimeServiceTests
{
    [Fact]
    public void CheckReturnsAvailableWhenVersionExists()
    {
        var service = new WebView2RuntimeService(() => "136.0.0.0");

        var result = service.Check();

        Assert.True(result.IsAvailable);
        Assert.Equal("已检测到 WebView2 Runtime", result.Summary);
        Assert.Equal("136.0.0.0", result.Detail);
    }

    [Fact]
    public void CheckReturnsMissingWhenRuntimeIsNotInstalled()
    {
        var service = new WebView2RuntimeService(() => throw new WebView2RuntimeNotFoundException("missing"));

        var result = service.Check();

        Assert.False(result.IsAvailable);
        Assert.Equal("未安装 WebView2 Runtime", result.Summary);
        Assert.Equal("missing", result.Detail);
    }

    [Fact]
    public void CheckReturnsFailureWhenUnexpectedExceptionOccurs()
    {
        var service = new WebView2RuntimeService(() => throw new InvalidOperationException("boom"));

        var result = service.Check();

        Assert.False(result.IsAvailable);
        Assert.Equal("WebView2 Runtime 检测失败", result.Summary);
        Assert.Equal("boom", result.Detail);
    }
}
