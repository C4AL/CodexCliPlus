using CPAD.Services;

using Microsoft.Web.WebView2.Core;

namespace CPAD.Tests.Services;

public sealed class WebView2RuntimeServiceTests
{
    [Fact]
    public void CheckReturnsAvailableWhenVersionExists()
    {
        var service = new WebView2RuntimeService(() => "136.0.0.0");

        var result = service.Check();

        Assert.True(result.IsAvailable);
        Assert.Equal("WebView2 Runtime is available.", result.Summary);
        Assert.Equal("136.0.0.0", result.Detail);
    }

    [Fact]
    public void CheckReturnsMissingWhenRuntimeIsNotInstalled()
    {
        var service = new WebView2RuntimeService(() => throw new WebView2RuntimeNotFoundException("missing"));

        var result = service.Check();

        Assert.False(result.IsAvailable);
        Assert.Equal("WebView2 Runtime is not installed.", result.Summary);
        Assert.Equal("missing", result.Detail);
    }

    [Fact]
    public void CheckReturnsFailureWhenUnexpectedExceptionOccurs()
    {
        var service = new WebView2RuntimeService(() => throw new InvalidOperationException("boom"));

        var result = service.Check();

        Assert.False(result.IsAvailable);
        Assert.Equal("WebView2 Runtime detection failed.", result.Summary);
        Assert.Equal("boom", result.Detail);
    }
}
