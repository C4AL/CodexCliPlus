using Microsoft.Playwright;

using FlaUI.Core.AutomationElements;

namespace CodexCliPlus.UiTests;

[Collection("Desktop UI")]
[Trait("Category", "UIProbe")]
public sealed class FirstRunUiAutomationTests
{
    [Fact]
    public void FirstRunProbeCapturesNativeUiArtifactsForManualReview()
    {
        using var run = UiTestRun.Start(nameof(FirstRunProbeCapturesNativeUiArtifactsForManualReview));

        run.CaptureWindow("startup-or-first-run-full.png");
        run.CaptureOptionalElement("PreparationProgressBar", "loading-progress.png", padding: 12);

        run.WaitForAutomationId("FirstRunSecurityKeyTextBox", TimeSpan.FromSeconds(15));
        run.WaitForAutomationId("FirstRunCopyKeyButton", TimeSpan.FromSeconds(15));
        run.WaitForAutomationId("FirstRunSaveToDesktopButton", TimeSpan.FromSeconds(3));
        run.WaitForAutomationId("FirstRunRememberSecurityKeyCheckBox", TimeSpan.FromSeconds(3));

        var keyBoxBounds = run.GetElementBounds("FirstRunSecurityKeyTextBox");
        var saveButtonBounds = run.GetElementBounds("FirstRunSaveToDesktopButton");
        var copyButtonBounds = run.GetElementBounds("FirstRunCopyKeyButton");
        var rememberCheckBoxBounds = run.GetElementBounds("FirstRunRememberSecurityKeyCheckBox");
        AssertFirstRunKeyRowGeometry(keyBoxBounds, saveButtonBounds, copyButtonBounds, rememberCheckBoxBounds);

        run.CaptureWindow("first-run-full.png");
        run.CaptureElementsUnion(
            "security-key-row-crop.png",
            6,
            "FirstRunSecurityKeyTextBox",
            "FirstRunSaveToDesktopButton",
            "FirstRunCopyKeyButton",
            "FirstRunRememberSecurityKeyCheckBox");
        run.CaptureElement("FirstRunCopyKeyButton", "copy-button-crop.png", padding: 6);
        run.CaptureElement("FirstRunSaveToDesktopButton", "save-button-crop.png", padding: 6);

        run.Hover("FirstRunCopyKeyButton");
        run.CaptureTooltip("复制密钥", "copy-tooltip.png");
        run.CaptureWindow("copy-tooltip-full.png");

        run.Hover("FirstRunSaveToDesktopButton");
        run.CaptureTooltip("保存到桌面", "save-tooltip.png");
        run.CaptureWindow("save-tooltip-full.png");

        run.TrackDesktopSecurityKeyFiles();
        run.Click("FirstRunSaveToDesktopButton");
        var savedFiles = run.WaitFor(
            () => run.GetNewDesktopSecurityKeyFiles().Length > 0 ? run.GetNewDesktopSecurityKeyFiles() : null,
            TimeSpan.FromSeconds(5),
            "保存到桌面没有生成安全密钥文件。");
        run.WriteJson("desktop-save.json", new
        {
            createdFiles = savedFiles.Select(Path.GetFileName).ToArray()
        });

        run.Click("FirstRunCopyKeyButton");
        run.WaitForAutomationId("AutoNotificationMessage", TimeSpan.FromSeconds(3));
        run.CaptureElement("AutoNotificationMessage", "copy-notification-message-crop.png", padding: 12);
        run.CaptureWindow("copy-notification-full.png");

        run.Click("FirstRunEnterManagementButton");
        run.WaitForAutomationId("FirstRunConfirmCloseButton", TimeSpan.FromSeconds(3));
        run.WaitForAutomationId("FirstRunConfirmContinueButton", TimeSpan.FromSeconds(3));
        run.CaptureElement("FirstRunConfirmCloseButton", "confirm-close-button-crop.png", padding: 10);

        run.Hover("FirstRunConfirmCloseButton");
        run.CaptureElement("FirstRunConfirmCloseButton", "confirm-close-hover-crop.png", padding: 12);
        run.CaptureWindow("confirm-close-hover-full.png");

        run.WriteUiaTree("uia-tree.json");
        run.WriteReview(
            "review.md",
            "首次初始化原生 UI 探针",
            "查看 security-key-row-crop.png：保存按钮必须在复制按钮左侧，两个按钮都必须完整显示且不重叠。",
            "查看 copy-button-crop.png：复制图标必须完整、居中、无裁切、无拉伸。",
            "查看 save-button-crop.png：分享/保存图标必须完整、居中、无裁切、无拉伸。",
            "查看 copy-tooltip.png 和 save-tooltip.png：中文必须正常显示，不能出现空框符号。",
            "查看 confirm-close-hover-crop.png：右上角 hover 背景必须匹配弹窗圆角，不应是方形雾玻璃块。",
            "查看 loading-progress.png（若存在）：高光动画不应溢出进度轨道。",
            "查看 copy-notification-full.png：自动通知和蓝色条位置应自然且无遮挡。");

        Assert.True(File.Exists(Path.Combine(run.ArtifactDirectory, "copy-button-crop.png")));
        Assert.True(File.Exists(Path.Combine(run.ArtifactDirectory, "review.md")));
    }

    [Fact]
    public void FirstRunProbeCapturesUnrememberedLoginFlowForManualReview()
    {
        using var run = UiTestRun.Start(nameof(FirstRunProbeCapturesUnrememberedLoginFlowForManualReview));

        run.WaitForAutomationId("FirstRunCopyKeyButton", TimeSpan.FromSeconds(15));
        run.WaitForAutomationId("FirstRunEnterManagementButton", TimeSpan.FromSeconds(15));
        run.Click("FirstRunEnterManagementButton");
        run.WaitForAutomationId("FirstRunConfirmCloseButton", TimeSpan.FromSeconds(8));
        run.CaptureWindow("confirm-countdown-full.png");

        var continueButton = run.WaitFor(
            () =>
            {
                var element = run.TryFindAutomationId("FirstRunConfirmContinueButton");
                return element is not null && element.Properties.IsEnabled.ValueOrDefault ? element : null;
            },
            TimeSpan.FromSeconds(12),
            "确认按钮倒计时结束后没有启用。");
        run.CaptureElement("FirstRunConfirmContinueButton", "confirm-ready-button-crop.png", padding: 10);
        continueButton.AsButton().Click();

        run.WaitForAutomationId("LoginButton", TimeSpan.FromSeconds(8));
        run.CaptureWindow("unremembered-login-full.png");
        run.CaptureElement("LoginButton", "login-button-crop.png", padding: 10);
        run.WriteUiaTree("uia-tree.json");
        run.WriteReview(
            "review.md",
            "未记住安全密钥登录流探针",
            "查看 unremembered-login-full.png：未勾选记住时确认后必须进入原生登录页，而不是直接进入管理页。",
            "查看 login-button-crop.png：登录按钮布局和文字应完整自然。");

        Assert.True(File.Exists(Path.Combine(run.ArtifactDirectory, "unremembered-login-full.png")));
    }

    [Fact]
    public async Task FirstRunProbeCapturesRememberedWebUiBridgeForManualReview()
    {
        using var run = UiTestRun.Start(nameof(FirstRunProbeCapturesRememberedWebUiBridgeForManualReview));

        run.WaitForAutomationId("FirstRunCopyKeyButton", TimeSpan.FromSeconds(15));
        run.WaitForAutomationId("FirstRunRememberSecurityKeyCheckBox", TimeSpan.FromSeconds(15));
        run.WaitForAutomationId("FirstRunEnterManagementButton", TimeSpan.FromSeconds(15));
        run.Click("FirstRunRememberSecurityKeyCheckBox");
        run.Click("FirstRunEnterManagementButton");
        run.WaitForAutomationId("FirstRunConfirmCloseButton", TimeSpan.FromSeconds(8));
        run.CaptureWindow("confirm-countdown-full.png");

        var continueButton = run.WaitFor(
            () =>
            {
                var element = run.TryFindAutomationId("FirstRunConfirmContinueButton");
                return element is not null && element.Properties.IsEnabled.ValueOrDefault ? element : null;
            },
            TimeSpan.FromSeconds(12),
            "确认按钮倒计时结束后没有启用。");
        run.CaptureElement("FirstRunConfirmContinueButton", "confirm-ready-button-crop.png", padding: 10);
        run.WaitForBackendPortAvailable(TimeSpan.FromSeconds(75));
        continueButton.AsButton().Click();
        run.CaptureWindow("after-confirm-full.png");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await WaitForCdpBrowserAsync(
            playwright,
            run.RemoteDebuggingPort,
            TimeSpan.FromSeconds(45));

        var page = await WaitForWebViewPageAsync(browser, TimeSpan.FromSeconds(30));
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await CaptureWebUiStateAsync(page, run);
        await page.WaitForSelectorAsync(".app-shell", new PageWaitForSelectorOptions
        {
            Timeout = 30_000
        });

        var state = await CaptureWebUiStateAsync(page, run);
        run.WaitFor(
            () =>
            {
                var element = run.TryFindAutomationId("ManagementWebView");
                return element is not null && element.IsVisibleEnough() ? element : null;
            },
            TimeSpan.FromSeconds(30),
            "管理 WebView 没有在原生窗口中变为可见。");
        run.CaptureWindow("webui-bridge-full.png");
        run.WriteReview(
            "review.md",
            "记住安全密钥 WebUI 桥接探针",
            "查看 webui-page.png 和 webui-bridge-full.png：勾选记住后应进入 WebUI 管理界面，不应停留在原生加载页。",
            "查看 webui-state.json：DesktopMode 与 AppShellVisible 必须为 true，AuthFailureVisible 必须为 false，URL 应为 codexcliplus-webui.local。");

        Assert.True(state.DesktopMode);
        Assert.True(state.AppShellVisible);
        Assert.False(state.AuthFailureVisible);
        Assert.Contains("codexcliplus-webui.local", state.Url, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<WebUiProbeState> CaptureWebUiStateAsync(IPage page, UiTestRun run)
    {
        var state = new WebUiProbeState(
            page.Url,
            await page.EvaluateAsync<bool>(
                "Boolean(window.__CODEXCLIPLUS_DESKTOP_BRIDGE__?.isDesktopMode?.())"),
            await page.EvaluateAsync<bool>(
                "Boolean(document.querySelector('.app-shell'))"),
            await page.EvaluateAsync<bool>(
                "Boolean(document.body?.innerText?.includes('桌面登录已失效'))"),
            await page.EvaluateAsync<string>(
                "(document.body?.innerText || '').slice(0, 500)"));
        run.WriteJson("webui-state.json", state);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(run.ArtifactDirectory, "webui-page.png"),
            FullPage = true
        });
        return state;
    }

    private static void AssertFirstRunKeyRowGeometry(
        UiElementBounds keyBox,
        UiElementBounds saveButton,
        UiElementBounds copyButton,
        UiElementBounds rememberCheckBox)
    {
        const double tolerance = 1.0;
        const double minimumButtonSide = 30.0;

        Assert.True(saveButton.Width >= minimumButtonSide, $"保存按钮宽度异常：{saveButton.Width:0.##}");
        Assert.True(saveButton.Height >= minimumButtonSide, $"保存按钮高度异常：{saveButton.Height:0.##}");
        Assert.True(copyButton.Width >= minimumButtonSide, $"复制按钮宽度异常：{copyButton.Width:0.##}");
        Assert.True(copyButton.Height >= minimumButtonSide, $"复制按钮高度异常：{copyButton.Height:0.##}");
        Assert.InRange(saveButton.Width / saveButton.Height, 0.75, 1.35);
        Assert.InRange(copyButton.Width / copyButton.Height, 0.75, 1.35);

        var rowLeft = keyBox.Left;
        var rowTop = Math.Min(keyBox.Top, rememberCheckBox.Top);
        var rowRight = rememberCheckBox.Right;
        var rowBottom = Math.Max(keyBox.Bottom, rememberCheckBox.Bottom);

        AssertInsideRow("保存按钮", saveButton, rowLeft, rowTop, rowRight, rowBottom, tolerance);
        AssertInsideRow("复制按钮", copyButton, rowLeft, rowTop, rowRight, rowBottom, tolerance);
        Assert.True(keyBox.Right <= saveButton.Left + tolerance, "保存按钮必须位于安全密钥框右侧。");
        Assert.True(saveButton.Left < copyButton.Left, "保存按钮必须在复制按钮左侧。");
        Assert.True(saveButton.Right <= copyButton.Left + tolerance, "保存按钮和复制按钮不能重叠。");
        Assert.True(copyButton.Right <= rememberCheckBox.Left + tolerance, "复制按钮必须位于记住安全密钥选项左侧。");
    }

    private static void AssertInsideRow(
        string name,
        UiElementBounds bounds,
        double rowLeft,
        double rowTop,
        double rowRight,
        double rowBottom,
        double tolerance)
    {
        Assert.True(bounds.Left + tolerance >= rowLeft, $"{name}超出了输入行左侧。");
        Assert.True(bounds.Top + tolerance >= rowTop, $"{name}超出了输入行上侧。");
        Assert.True(bounds.Right <= rowRight + tolerance, $"{name}超出了输入行右侧。");
        Assert.True(bounds.Bottom <= rowBottom + tolerance, $"{name}超出了输入行下侧。");
    }

    private sealed record WebUiProbeState(
        string Url,
        bool DesktopMode,
        bool AppShellVisible,
        bool AuthFailureVisible,
        string BodyTextSnippet);

    private static async Task<IPage> WaitForWebViewPageAsync(IBrowser browser, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var page = browser.Contexts
                .SelectMany(context => context.Pages)
                .FirstOrDefault(candidate => candidate.Url.Contains("codexcliplus-webui.local", StringComparison.OrdinalIgnoreCase));
            if (page is not null)
            {
                return page;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("WebView2 CDP 中没有出现 CodexCliPlus WebUI 页面。");
    }

    private static async Task<IBrowser> WaitForCdpBrowserAsync(IPlaywright playwright, int port, TimeSpan timeout)
    {
        var endpoint = $"http://127.0.0.1:{port}";
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                return await playwright.Chromium.ConnectOverCDPAsync(endpoint);
            }
            catch (Exception exception) when (exception is PlaywrightException or HttpRequestException)
            {
                lastError = exception;
                await Task.Delay(300);
            }
        }

        throw new TimeoutException(
            lastError is null
                ? $"WebView2 CDP 端口未打开：{endpoint}"
                : $"WebView2 CDP 端口未打开：{endpoint}。最后错误：{lastError.Message}");
    }
}
