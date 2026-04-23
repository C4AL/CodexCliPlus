using CPAD.Core.Models;

using Microsoft.Web.WebView2.Core;

namespace CPAD.Services;

public sealed class WebView2RuntimeService
{
    private readonly Func<string?> _versionProvider;

    public WebView2RuntimeService()
        : this(() => CoreWebView2Environment.GetAvailableBrowserVersionString())
    {
    }

    public WebView2RuntimeService(Func<string?> versionProvider)
    {
        _versionProvider = versionProvider;
    }

    public DependencyCheckResult Check()
    {
        try
        {
            var version = _versionProvider();
            return new DependencyCheckResult
            {
                IsAvailable = !string.IsNullOrWhiteSpace(version),
                Summary = string.IsNullOrWhiteSpace(version)
                    ? "WebView2 Runtime is not installed."
                    : "WebView2 Runtime is available.",
                Detail = version
            };
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            return new DependencyCheckResult
            {
                IsAvailable = false,
                Summary = "WebView2 Runtime is not installed.",
                Detail = exception.Message
            };
        }
        catch (Exception exception)
        {
            return new DependencyCheckResult
            {
                IsAvailable = false,
                Summary = "WebView2 Runtime detection failed.",
                Detail = exception.Message
            };
        }
    }
}
