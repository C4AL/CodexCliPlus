using DesktopHost.Core.Models;

using Microsoft.Web.WebView2.Core;

namespace DesktopHost.Services;

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
                Summary = string.IsNullOrWhiteSpace(version) ? "未检测到 WebView2 Runtime" : "已检测到 WebView2 Runtime",
                Detail = version
            };
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            return new DependencyCheckResult
            {
                IsAvailable = false,
                Summary = "未安装 WebView2 Runtime",
                Detail = exception.Message
            };
        }
        catch (Exception exception)
        {
            return new DependencyCheckResult
            {
                IsAvailable = false,
                Summary = "WebView2 Runtime 检测失败",
                Detail = exception.Message
            };
        }
    }
}
