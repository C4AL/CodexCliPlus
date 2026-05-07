using System.Text;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Services;

namespace CodexCliPlus.Tests.UI;

[Trait("Category", "Fast")]
public sealed class NavigationShellTests
{
    [Fact]
    public void MainWindowHostsSingleWebViewAndTrayMenuContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml"),
            Encoding.UTF8
        );
        var startupFlowXaml = ReadStartupFlowXaml(repositoryRoot);
        var startupFlowResources = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Views",
                "Resources",
                "StartupFlowResources.xaml"
            ),
            Encoding.UTF8
        );
        var shellResources = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Views",
                "Resources",
                "ShellResources.xaml"
            ),
            Encoding.UTF8
        );
        var hostSource = ReadMainWindowSources(repositoryRoot);
        var windowLoadedSource = SliceBetween(
            hostSource,
            "private async void Window_Loaded",
            "private async void Window_Closed"
        );
        var shellBrandDockPopupXaml = SliceBetween(
            xaml,
            "x:Name=\"ShellBrandDockPopup\"",
            "      </Popup>"
        );
        var managementEntryTransitionPopupXaml = SliceBetween(
            xaml,
            "x:Name=\"ManagementEntryTransitionPopup\"",
            "    </Popup>"
        );
        Assert.Contains("<wv2:WebView2", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionPopup\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "PlacementTarget=\"{Binding ElementName=ShellRootGrid}\"",
            managementEntryTransitionPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("欢迎回来", managementEntryTransitionPopupXaml, StringComparison.Ordinal);
        Assert.Contains(
            "正在打开管理界面",
            managementEntryTransitionPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionWelcomePanel\"",
            managementEntryTransitionPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionWelcomeTranslateTransform\"",
            managementEntryTransitionPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionBrandRing\"",
            managementEntryTransitionPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionBrandOuterHalo\"",
            managementEntryTransitionPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionBrandInnerHalo\"",
            managementEntryTransitionPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionBrandOrbit\"",
            managementEntryTransitionPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionBrandLightSweep\"",
            managementEntryTransitionPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionBrandIcon\"",
            managementEntryTransitionPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "BeginManagementEntryTransitionAsync",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "RestoreMainWindowForManagementEntryTransitionAsync",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("return true;", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "var shouldExpandFromAuthentication = _isAuthenticationCompactWindowMode;",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "if (shouldExpandFromAuthentication)",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "Math.Abs(Width - AuthenticationCompactWindowWidth)",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ManagementEntryTransitionPhase.ExpandingWindow",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ManagementEntryTransitionPhase.WelcomeFadeIn",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ManagementEntryTransitionPhase.LoadingWebView",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ManagementEntryTransitionPhase.WelcomeFadeOut",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ResetManagementEntryTransitionWelcomeVisuals();",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "FadeManagementEntryTransitionOverlayAsync(1, 180)",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "FadeManagementEntryTransitionOverlayAsync(0, 220)",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "ManagementEntryTransitionOverlay.Opacity = 1;",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "var expansionCenterX = Left + Width / 2;",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "var centeredTargetLeft = expansionCenterX - target.Width / 2;",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "PrepareManagementWebViewForStableNavigationAsync",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("waitForNavigation: true", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "ManagementWebView.Visibility = Visibility.Hidden;",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "await DispatchManagementWebViewResizeAsync();",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "window.dispatchEvent(new Event('resize'));",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "await CompleteManagementEntryTransitionAsync();",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "_settings = await _appConfigurationService.LoadAsync();",
            windowLoadedSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "await ContinueAfterStartupGateAsync();",
            windowLoadedSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "ShowPreparationStep(5",
            windowLoadedSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "SystemParameters.ClientAreaAnimation",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Source=\"Views/Resources/ShellResources.xaml\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "MouseLeftButtonDown=\"DragRegion_MouseLeftButtonDown\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains("<controls:StartupFlowView", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartupFlow\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"LoginPanel\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"FirstRunKeyPanel\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"LoadingPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LoginPanel\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UpgradeNoticePanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FirstRunKeyPanel\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"LoadingPanel\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Name=\"StartupStepHost\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"PreparationProgressBar\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementKeyPasswordBox\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"RememberPasswordCheckBox\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("x:Name=\"AutoLoginCheckBox\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"FirstRunSecurityKeyDisplay\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"FirstRunSecurityKeyText\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"FirstRunRememberPasswordCheckBox\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"FirstRunAutoLoginCheckBox\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("MinimizeWindowButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("CloseWindowButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("Activated=\"Window_Activated\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Deactivated=\"Window_Deactivated\"", xaml, StringComparison.Ordinal);
        Assert.Contains("复制密钥", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains("保存到桌面", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains("进入管理界面", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains("忘记安全密钥/重置", startupFlowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("当前步骤：", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "Source=\"/Views/Resources/ShellResources.xaml\"",
            startupFlowResources,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "BasedOn=\"{StaticResource ShellRaisedPanelStyle}\"",
            startupFlowResources,
            StringComparison.Ordinal
        );
        Assert.Contains("x:Key=\"TitleBarButtonStyle\"", shellResources, StringComparison.Ordinal);
        Assert.Contains("BorderThickness\" Value=\"0\"", shellResources, StringComparison.Ordinal);
        Assert.True(CountOccurrences(xaml, "Style=\"{StaticResource TitleBarButtonStyle}\"") >= 2);
        Assert.Contains("x:Name=\"ShellSidebarButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellNavigationDockPopup\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "PlacementTarget=\"{Binding ElementName=ManagementContentHost}\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains("x:Name=\"ShellNavigationDockHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"56\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Panel.ZIndex=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ClipToBounds=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellNavigationRailTrack\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "MouseMove=\"ShellNavigationDockHost_MouseMove\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "MouseLeave=\"ShellNavigationPanel_MouseLeave\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains("NavigationDockVisualState", hostSource, StringComparison.Ordinal);
        Assert.Contains("CanShowNavigationDockPopup", hostSource, StringComparison.Ordinal);
        Assert.Contains("_isMainWindowActive", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "WindowState != WindowState.Minimized",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("Window_Deactivated", hostSource, StringComparison.Ordinal);
        Assert.Contains("CloseShellDockPopups", hostSource, StringComparison.Ordinal);
        Assert.Contains("NavigationDockRestingWidth", hostSource, StringComparison.Ordinal);
        Assert.Contains("NavigationDockEdgeIntentWidth = 18", hostSource, StringComparison.Ordinal);
        Assert.Contains("NavigationDockPanelOpenOffset", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "NavigationDockPanelOpenHeight",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ResolveNavigationDockPanelMeasuredOpenHeight",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ResolveVisibleNavigationItemsHeight",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ShellNavigationItemsHost.Children.OfType<WpfButton>()",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("button.Measure(", hostSource, StringComparison.Ordinal);
        Assert.Contains("ManagementContentHost.ActualHeight", hostSource, StringComparison.Ordinal);
        Assert.Contains("ShellNavigationPanel.Padding.Top", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Height=\"392\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"ShellNavigationItemsHost\" VerticalAlignment=\"Top\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains("ApplyNavigationDockLabelState", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "NavigationDockExpandThreshold",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "Effect=\"{StaticResource ShellNavigationDockShadow}\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "Effect=\"{StaticResource ShellNavigationRailShadow}\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "ShellNavigationDockShadow",
            shellResources,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "ShellNavigationRailShadow",
            shellResources,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ShellNavDashboardOverviewButton\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"ShellNavRuntimeOverviewButton\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("x:Name=\"ShellNavConsoleButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"/dashboard/overview\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandParameter=\"/console\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"操作台\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"运行概览\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"控制台\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellNavCodexConfigButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"/codex-config\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\".codex配置\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellNavConfigButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"/config\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"API配置\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShellNavCodexConfigButton", hostSource, StringComparison.Ordinal);
        AssertSourceOrder(
            xaml,
            "CommandParameter=\"/codex-config\"",
            "CommandParameter=\"/config\""
        );
        Assert.Contains("x:Name=\"ShellNavAccountButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"/accounts\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"账号中心\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"ShellNavAuthFilesButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"ShellNavQuotaButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandParameter=\"/ai-providers\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandParameter=\"/auth-files\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandParameter=\"/quota\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellBrandDockButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PreviewMouseLeftButtonDown=\"ShellBrandDockButton_PreviewMouseLeftButtonDown\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains("x:Name=\"ShellBrandDockPopup\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StaysOpen=\"True\"", shellBrandDockPopupXaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "StaysOpen=\"False\"",
            shellBrandDockPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("x:Name=\"ShellBrandDockCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"0\"", shellBrandDockPopupXaml, StringComparison.Ordinal);
        Assert.Contains(
            "RenderTransformOrigin=\"0.5,0\"",
            shellBrandDockPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("Opened=\"ShellBrandDockPopup_Opened\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Closed=\"ShellBrandDockPopup_Closed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellBrandDockScaleTransform\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScaleY=\"0.88\"", shellBrandDockPopupXaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"ShellBrandDockTranslateTransform\" Y=\"-8\"",
            shellBrandDockPopupXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "codexcliplus-display.png",
            shellBrandDockPopupXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "Text=\"CodexCliPlus\"",
            shellBrandDockPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("x:Name=\"ShellDockAppVersionText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellDockCoreVersionText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellDockConnectionStatusText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellDockBackendAddressText\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"ShellDockCopyBackendAddressButton\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "AutomationProperties.AutomationId=\"ShellConnectionStatusPill\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Text=\"桌面宿主\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"ShellRefreshButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellThemeButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("iconPacks:PackIconLucide", xaml, StringComparison.Ordinal);
        Assert.Contains("Kind=\"SunMoon\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"17\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("M12,3 C7.029,3 3,7.029 3,12", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellSettingsButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsOverlayPopup\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "PlacementTarget=\"{Binding ElementName=ShellRootGrid}\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains("x:Name=\"SettingsOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "Width=\"{Binding ActualWidth, ElementName=ShellRootGrid}\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Height=\"{Binding ActualHeight, ElementName=ShellRootGrid}\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("SettingsWindow_Deactivated", hostSource, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsFollowSystemCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"18,18,0,0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Name=\"SettingsOverlayCloseButton\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"SettingsRequestLogButton\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"SettingsRefreshInfoButton\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("x:Name=\"SettingsAppVersionText\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Name=\"SettingsBackendVersionText\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("x:Name=\"SettingsConnectionText\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ui:NavigationView", xaml, StringComparison.Ordinal);
        Assert.Contains("TrayOpenMenuItem_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("TrayRestartBackendMenuItem_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("TrayCheckUpdatesMenuItem_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("TrayExitMenuItem_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("RequestApplicationExitAsync", hostSource, StringComparison.Ordinal);
        Assert.Contains("RunApplicationExitAsync", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "BackendProcessStopOptions.FastExit",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "System.Windows.Application.Current.Shutdown();",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "_wasShellBrandDockOpenBeforeButtonClick",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "ShellBrandDockButton_PreviewMouseLeftButtonDown",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("var wasOpenBeforeClick", hostSource, StringComparison.Ordinal);
        Assert.Contains("if (ShellBrandDockPopup.IsOpen)", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "await HideShellBrandDockPopupAsync();",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("ShellBrandDockPopup.IsOpen = true", hostSource, StringComparison.Ordinal);
        Assert.Contains("HideShellBrandDockPopupAsync", hostSource, StringComparison.Ordinal);
        Assert.Contains("ShellBrandDockTranslateTransform", xaml, StringComparison.Ordinal);
        Assert.Contains("ShellBrandDockScaleTransform", xaml, StringComparison.Ordinal);
        Assert.Contains("UIElement.OpacityProperty", hostSource, StringComparison.Ordinal);
        Assert.Contains("ScaleTransform.ScaleYProperty", hostSource, StringComparison.Ordinal);
        Assert.Contains("TranslateTransform.YProperty", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "ShellBrandDockScaleTransform.ScaleY = 0.88",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ShellBrandDockTranslateTransform.Y = -8",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("CreateEaseAnimation(0.88, 130)", hostSource, StringComparison.Ordinal);
        Assert.Contains("CreateEaseAnimation(-8, 130)", hostSource, StringComparison.Ordinal);
        Assert.Contains("_isShellBrandDockClosing = true", hostSource, StringComparison.Ordinal);
        Assert.Contains("NavigationDockRailTrack", xaml, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"0.78\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"0\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeAndStartupCardsAvoidTransparentShadowArtifacts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appProject = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "CodexCliPlus.App.csproj"),
            Encoding.UTF8
        );
        var updaterProject = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.Updater",
                "CodexCliPlus.Updater.csproj"
            ),
            Encoding.UTF8
        );
        var mainXaml = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml"),
            Encoding.UTF8
        );
        var updaterXaml = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.Updater", "MainWindow.xaml"),
            Encoding.UTF8
        );
        var startupFlowXaml = ReadStartupFlowXaml(repositoryRoot);
        var startupFlowResources = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Views",
                "Resources",
                "StartupFlowResources.xaml"
            ),
            Encoding.UTF8
        );
        var shellSettingsSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "MainWindow.ShellSettingsPresentation.cs"
            ),
            Encoding.UTF8
        );
        var shellNotificationsSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "MainWindow.ShellNotifications.cs"
            ),
            Encoding.UTF8
        );

        Assert.Contains(
            "PackageReference Include=\"ControlzEx\"",
            appProject,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "PackageReference Include=\"ControlzEx\"",
            updaterProject,
            StringComparison.Ordinal
        );
        Assert.Contains("controlzEx:WindowChromeBehavior", mainXaml, StringComparison.Ordinal);
        Assert.Contains("controlzEx:GlowWindowBehavior", mainXaml, StringComparison.Ordinal);
        Assert.Contains("UseNativeCaptionButtons=\"False\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("<controlzEx:WindowChromeWindow", updaterXaml, StringComparison.Ordinal);
        Assert.Contains("UseNativeCaptionButtons=\"True\"", updaterXaml, StringComparison.Ordinal);
        Assert.Contains("GlowColor=\"Transparent\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("NonActiveGlowColor=\"Transparent\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("GlowDepth=\"0\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("PreferDWMBorderColor=\"False\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("GlowColor=\"Transparent\"", updaterXaml, StringComparison.Ordinal);
        Assert.Contains("NonActiveGlowColor=\"Transparent\"", updaterXaml, StringComparison.Ordinal);
        Assert.Contains("GlowDepth=\"0\"", updaterXaml, StringComparison.Ordinal);
        Assert.Contains("PreferDWMBorderColor=\"False\"", updaterXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GlowColor=\"#330F766E\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GlowColor=\"#330F766E\"", updaterXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GlowDepth=\"8\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GlowDepth=\"8\"", updaterXaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PreferDWMBorderColor=\"True\"",
            mainXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "PreferDWMBorderColor=\"True\"",
            updaterXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Style=\"{StaticResource ShellRaisedPanelShadowHostStyle}\"",
            mainXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "Style=\"{StaticResource ShellRaisedPanelShadowHostStyle}\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Style=\"{StaticResource StartupFlowPasswordBoxStyle}\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Style=\"{StaticResource StartupFlowKeyDisplayStyle}\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Key=\"StartupFlowKeyDisplayStyle\"",
            startupFlowResources,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Key=\"StartupFlowPasswordBoxStyle\"",
            startupFlowResources,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "FocusVisualStyle\" Value=\"{x:Null}\"",
            startupFlowResources,
            StringComparison.Ordinal
        );
        Assert.Contains("x:Name=\"SettingsOverlayPopup\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains(
            "SettingsOverlayPopup.IsOpen",
            shellSettingsSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "RefreshDockPopupPlacement(SettingsOverlayPopup)",
            shellSettingsSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "ManagementWebView.Visibility = Visibility.Collapsed;",
            shellSettingsSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "RestoreManagementWebViewAfterSettingsOverlay",
            shellSettingsSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "AllowsTransparency = true",
            shellSettingsSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("new Window", shellSettingsSource, StringComparison.Ordinal);
        Assert.Contains(
            "var contentCard = new Border",
            shellNotificationsSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "clipTarget.Clip = new RectangleGeometry",
            shellNotificationsSource,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void TrayExitMarksSingleInstanceExitAndUsesFastExitCleanup()
    {
        var repositoryRoot = FindRepositoryRoot();
        var hostSource = ReadMainWindowSources(repositoryRoot);
        var eventHandlersSource = ReadAppSource(repositoryRoot, "MainWindow.EventHandlers.cs");
        var trayHelpersSource = ReadAppSource(repositoryRoot, "MainWindow.ShellTrayAndHelpers.cs");
        var requestExitSource = SliceBetween(
            eventHandlersSource,
            "private Task RequestApplicationExitAsync()",
            "private async Task RunApplicationExitAsync()"
        );
        var runExitSource = eventHandlersSource[
            eventHandlersSource.IndexOf(
                "private async Task RunApplicationExitAsync()",
                StringComparison.Ordinal
            )..
        ];
        var restoreActivationSource = SliceBetween(
            trayHelpersSource,
            "internal void RestoreFromExternalActivation()",
            "private void RestoreMainWindowToForeground()"
        );
        var windowClosedSource = SliceBetween(
            eventHandlersSource,
            "private async void Window_Closed",
            "public void Dispose()"
        );

        Assert.Contains(
            "App.MarkSingleInstanceExitInProgress();",
            requestExitSource,
            StringComparison.Ordinal
        );
        AssertSourceOrder(
            requestExitSource,
            "_isExitRequested = true;",
            "App.MarkSingleInstanceExitInProgress();"
        );
        AssertSourceOrder(
            requestExitSource,
            "App.MarkSingleInstanceExitInProgress();",
            "_applicationExitTask = RunApplicationExitAsync();"
        );
        Assert.Contains(
            "if (_applicationExitTask is not null)",
            requestExitSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "return _applicationExitTask;",
            requestExitSource,
            StringComparison.Ordinal
        );
        AssertSourceOrder(
            requestExitSource,
            "_isExitRequested = true;",
            "_applicationExitTask = RunApplicationExitAsync();"
        );
        Assert.Contains(
            "CancelUsageStatsSyncDebounce();",
            requestExitSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "CancelPostStartupPersistenceImport();",
            requestExitSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "CloseSettingsOverlayImmediately();",
            requestExitSource,
            StringComparison.Ordinal
        );
        Assert.Contains("TrayIcon.Unregister();", requestExitSource, StringComparison.Ordinal);
        Assert.Contains(
            "ExitPersistenceSyncTimeout = TimeSpan.FromMilliseconds(500)",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "ExitPersistenceSyncTimeout = TimeSpan.FromSeconds(5)",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("if (_isExitRequested)", restoreActivationSource, StringComparison.Ordinal);
        AssertSourceOrder(
            restoreActivationSource,
            "if (_isExitRequested)",
            "RestoreMainWindowToForeground();"
        );
        Assert.Contains("ExitPersistenceSyncTimeout", runExitSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromSeconds(5)", runExitSource, StringComparison.Ordinal);
        Assert.Contains(
            "FastExitBackendStopTimeout",
            runExitSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "BackendProcessStopOptions.FastExit",
            runExitSource,
            StringComparison.Ordinal
        );
        Assert.Contains("TrayIcon.Unregister();", windowClosedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppSingleInstanceStartupWaitsOnlyWhenExistingInstanceIsExiting()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "App.xaml.cs"),
            Encoding.UTF8
        );
        var acquisitionSource = NormalizeLineEndings(
            SliceBetween(
                source,
                "private bool TryAcquireSingleInstance()",
                "internal static void MarkSingleInstanceExitInProgress()"
            )
        );
        var stopListenerSource = NormalizeLineEndings(
            SliceBetween(
                source,
                "private void StopSingleInstanceWakeListener()",
                "private static bool TryParseRepairMode("
            )
        );

        Assert.Contains("SingleInstanceExitingEventName", source, StringComparison.Ordinal);
        Assert.Contains(
            "SingleInstanceExitTakeoverTimeout = TimeSpan.FromSeconds(3)",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "BackendProcessStopOptions.FastExit",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains("EventResetMode.ManualReset", source, StringComparison.Ordinal);
        Assert.Contains("MarkSingleInstanceExitInProgress", source, StringComparison.Ordinal);
        Assert.Contains(
            "IsSingleInstanceExitInProgress()",
            acquisitionSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "WaitForSingleInstanceExitTakeover()",
            acquisitionSource,
            StringComparison.Ordinal
        );
        AssertSourceOrder(
            acquisitionSource,
            "else if (IsSingleInstanceExitInProgress())",
            "if (!WaitForSingleInstanceExitTakeover())"
        );
        AssertSourceOrder(
            acquisitionSource,
            "if (!WaitForSingleInstanceExitTakeover())",
            "_singleInstanceExitingEvent.Reset();"
        );
        Assert.Contains(
            "else\n            {\n                _singleInstanceWakeEvent.Set();",
            acquisitionSource,
            StringComparison.Ordinal
        );
        AssertSourceOrder(
            acquisitionSource,
            "else if (IsSingleInstanceExitInProgress())",
            "else\n            {\n                _singleInstanceWakeEvent.Set();"
        );
        Assert.Contains(
            "WaitOne(SingleInstanceExitTakeoverTimeout)",
            source,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "WaitForSingleInstanceExitTakeover();\n                _singleInstanceWakeEvent.Set();",
            acquisitionSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "var wakeTask = _singleInstanceWakeTask;",
            stopListenerSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "if (wakeTask is not null)\n            {\n                _singleInstanceWakeEvent?.Set();",
            stopListenerSource,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void AppRegistersMinimalShellInsteadOfRuntimeManagementPages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "App.xaml.cs"),
            Encoding.UTF8
        );

        Assert.Contains("AddSingleton<WebUiAssetLocator>()", source, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<MainWindow>()", source, StringComparison.Ordinal);
        Assert.Contains("SingleInstanceMutexName", source, StringComparison.Ordinal);
        Assert.Contains("SingleInstanceWakeEventName", source, StringComparison.Ordinal);
        Assert.Contains("SingleInstanceExitingEventName", source, StringComparison.Ordinal);
        Assert.Contains("TryParseRepairMode(e.Args", source, StringComparison.Ordinal);
        Assert.Contains("TryAcquireSingleInstance()", source, StringComparison.Ordinal);
        AssertSourceOrder(
            source,
            "if (TryParseRepairMode(e.Args",
            "if (!TryAcquireSingleInstance())"
        );
        Assert.Contains("EventWaitHandle", source, StringComparison.Ordinal);
        Assert.Contains("new Mutex(", source, StringComparison.Ordinal);
        Assert.Contains("ListenForSingleInstanceWakeRequests", source, StringComparison.Ordinal);
        Assert.Contains("RestoreFromExternalActivation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IManagementNavigationService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DashboardPageViewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DashboardPage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ManagementNavigationService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ManagementRouteCatalog", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowPlacementChangesRefreshOpenedDockPopupPlacementWithoutReopeningNavigationDock()
    {
        var repositoryRoot = FindRepositoryRoot();
        var eventHandlersSource = ReadAppSource(repositoryRoot, "MainWindow.EventHandlers.cs");
        var startupPresentationSource = ReadAppSource(
            repositoryRoot,
            "MainWindow.ShellStartupPresentation.cs"
        );

        var placementChangedSource = SliceBetween(
            eventHandlersSource,
            "private void Window_PlacementChanged",
            "private async void RetryButton_Click"
        );
        var brandPopupOpenedSource = SliceBetween(
            eventHandlersSource,
            "private void ShellBrandDockPopup_Opened",
            "private void ShellBrandDockPopup_Closed"
        );
        var refreshAllSource = SliceBetween(
            startupPresentationSource,
            "private void RefreshShellDockPopupPlacements()",
            "private void RefreshNavigationDockPopupPlacement()"
        );
        var navigationRefreshSource = SliceBetween(
            startupPresentationSource,
            "private void RefreshNavigationDockPopupPlacement()",
            "private void RefreshShellBrandDockPopupPlacement()"
        );
        var brandRefreshSource = SliceBetween(
            startupPresentationSource,
            "private void RefreshShellBrandDockPopupPlacement()",
            "private static void RefreshDockPopupPlacement"
        );

        Assert.Contains("RefreshShellDockPopupPlacements();", placementChangedSource);
        Assert.Contains("UpdateSettingsOverlayPopupVisibility();", placementChangedSource);
        Assert.Contains("UpdateShellNotificationPopupVisibility();", placementChangedSource);
        Assert.Contains("RefreshShellBrandDockPopupPlacement();", brandPopupOpenedSource);
        Assert.Contains("RefreshShellBrandDockPopupPlacement();", refreshAllSource);
        Assert.Contains("RefreshNavigationDockPopupPlacement();", refreshAllSource);
        Assert.Contains("RefreshShellNotificationPopupPlacements();", refreshAllSource);
        Assert.DoesNotContain(
            "ShellNavigationDockPopup.IsOpen = false",
            navigationRefreshSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "ShellNavigationDockPopup.IsOpen = true",
            navigationRefreshSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "RefreshDockPopupPlacement(ShellNavigationDockPopup);",
            navigationRefreshSource
        );
        Assert.Contains(
            "ShellBrandDockPopup is null",
            brandRefreshSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "popup.HorizontalOffset = horizontalOffset + 0.01",
            startupPresentationSource
        );
        Assert.Contains("popup.HorizontalOffset = horizontalOffset;", startupPresentationSource);
    }

    [Fact]
    public void DesktopHostInjectsBootstrapAndRedirectsExternalLinks()
    {
        var repositoryRoot = FindRepositoryRoot();
        var hostSource = ReadMainWindowSources(repositoryRoot);
        var bridgeSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Services",
                "DesktopBridgeScriptFactory.cs"
            ),
            Encoding.UTF8
        );
        var webBridgeSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "desktop",
                "bridge.ts"
            ),
            Encoding.UTF8
        );

        Assert.Contains("http://{AppHostName}/index.html", hostSource, StringComparison.Ordinal);
        Assert.Contains("CreateManagementEntryUri", hostSource, StringComparison.Ordinal);
        Assert.Contains("ccp_nav=", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "Interlocked.Increment(ref _managementNavigationNonce)",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ManagementWebView.CoreWebView2.Navigate(entryUri.ToString())",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "ManagementWebView.CoreWebView2.Navigate(AppEntryUri.ToString())",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "https://{AppHostName}/index.html",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("SetVirtualHostNameToFolderMapping", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "AddScriptToExecuteOnDocumentCreatedAsync",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("WebMessageReceived", hostSource, StringComparison.Ordinal);
        Assert.Contains("openExternal", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("getBootstrap", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("consumeBootstrap: readBootstrap", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("bootstrap = null", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("getBootstrap", webBridgeSource, StringComparison.Ordinal);
        Assert.Contains("requestNativeLogin", hostSource, StringComparison.Ordinal);
        Assert.Contains("requestNativeLogin", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("shellStateChanged", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("shellNotification", hostSource, StringComparison.Ordinal);
        Assert.Contains("shellNotification", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("showShellNotification", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("showShellNotification", webBridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("refreshAll", hostSource, StringComparison.Ordinal);
        Assert.Contains("setTheme", hostSource, StringComparison.Ordinal);
        Assert.Contains("transitionMs", hostSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationThemeManager.Apply", hostSource, StringComparison.Ordinal);
        Assert.Contains("CreateShellThemeSnapshot", hostSource, StringComparison.Ordinal);
        Assert.Contains("ApplyCurrentShellTheme", hostSource, StringComparison.Ordinal);
        Assert.Contains("navigate", hostSource, StringComparison.Ordinal);
        Assert.Contains("pathname", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("navigationHoverZone", hostSource, StringComparison.Ordinal);
        Assert.Contains("navigationHoverZone", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("usageStatsRefreshed", hostSource, StringComparison.Ordinal);
        Assert.Contains("usageStatsRefreshed", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("requestLocalDependencySnapshot", hostSource, StringComparison.Ordinal);
        Assert.Contains("requestLocalDependencySnapshot", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("runLocalDependencyRepair", hostSource, StringComparison.Ordinal);
        Assert.Contains("runLocalDependencyRepair", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("localDependencyRepairProgress", hostSource, StringComparison.Ordinal);
        Assert.Contains("localDependencyRepairProgress", webBridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("确认本地环境修复", hostSource, StringComparison.Ordinal);
        Assert.Contains("requestCodexRouteState", hostSource, StringComparison.Ordinal);
        Assert.Contains("requestCodexRouteState", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("switchCodexRoute", hostSource, StringComparison.Ordinal);
        Assert.Contains("switchCodexRoute", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("targetId", hostSource, StringComparison.Ordinal);
        Assert.Contains("targetId", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("codexRouteResponse", hostSource, StringComparison.Ordinal);
        Assert.Contains("codexRouteResponse", webBridgeSource, StringComparison.Ordinal);
        Assert.Contains("hasHostBridge", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("return false", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("桌面桥接通道未就绪", webBridgeSource, StringComparison.Ordinal);
        Assert.Contains("managementRequest", hostSource, StringComparison.Ordinal);
        Assert.Contains("managementRequest", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("managementResponse", hostSource, StringComparison.Ordinal);
        Assert.Contains("dataChanged", hostSource, StringComparison.Ordinal);
        Assert.Contains("managementResponse", webBridgeSource, StringComparison.Ordinal);
        Assert.Contains("dataChanged", webBridgeSource, StringComparison.Ordinal);
        Assert.Contains("ManagementChangeBroadcastService", hostSource, StringComparison.Ordinal);
        Assert.Contains("ScheduleUsageStatsRefreshedSync", hostSource, StringComparison.Ordinal);
        Assert.Contains("event.clientX > 18", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("setTimeout", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("90", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PostWebUiCommand(new { type = \"toggleSidebarCollapsed\"",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("__CODEXCLIPLUS_DESKTOP_BRIDGE__", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("StartupState", hostSource, StringComparison.Ordinal);
        Assert.Contains("UpgradeNotice", hostSource, StringComparison.Ordinal);
        Assert.Contains("FirstRunKeyReveal", hostSource, StringComparison.Ordinal);
        Assert.Contains("NativeLogin", hostSource, StringComparison.Ordinal);
        Assert.Contains("VerifyManagementKey", hostSource, StringComparison.Ordinal);
        Assert.Contains("LastSeenApplicationVersion", hostSource, StringComparison.Ordinal);
        Assert.Contains("SecurityKeyOnboardingCompleted", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopHostPostsThemeCommandBeforePersistingThemeSelection()
    {
        var repositoryRoot = FindRepositoryRoot();
        var hostSource = ReadMainWindowSources(repositoryRoot);
        var setThemeSource = SliceBetween(
            hostSource,
            "private async Task SetShellThemeAsync",
            "private async Task PersistLatestShellThemeSelectionAsync"
        );
        var persistSource = SliceBetween(
            hostSource,
            "private async Task PersistLatestShellThemeSelectionAsync",
            "private void PostShellThemeCommand"
        );
        var applySnapshotSource = SliceBetween(
            hostSource,
            "private void ApplyShellThemeSnapshot",
            "private void ApplyShellTheme("
        );
        var applyWebUiShellStateSource = SliceBetween(
            hostSource,
            "private void ApplyWebUiShellState",
            "private void CoreWebView2_ProcessFailed"
        );

        Assert.Contains("CreateShellThemeSnapshot(", setThemeSource, StringComparison.Ordinal);
        Assert.Contains(
            "ShellThemeTransitionMilliseconds",
            setThemeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ApplyShellThemeSnapshot(snapshot, postWebCommand: true);",
            setThemeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "PostShellThemeCommand(snapshot)",
            applySnapshotSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "UpdateShellThemePresentation(snapshot)",
            applySnapshotSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "PersistLatestShellThemeSelectionAsync(themePersistenceVersion)",
            setThemeSource,
            StringComparison.Ordinal
        );
        AssertSourceOrder(
            setThemeSource,
            "ApplyShellThemeSnapshot(snapshot, postWebCommand: true);",
            "PersistLatestShellThemeSelectionAsync(themePersistenceVersion)"
        );
        Assert.Contains(
            "transitionMs = snapshot.TransitionMilliseconds",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "_appConfigurationService.SaveAsync(_settings)",
            persistSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "themePersistenceVersion != Volatile.Read(ref _shellThemePersistenceVersion)",
            persistSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ApplyCurrentShellTheme(transitionMilliseconds: 0, postWebCommand: false)",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ApplyCurrentShellTheme(",
            SliceBetween(
                hostSource,
                "private void SystemEvents_UserPreferenceChanged",
                "private void Window_Closing"
            ),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "TryGetProperty(\"theme\"",
            applyWebUiShellStateSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "TryGetProperty(\"resolvedTheme\"",
            applyWebUiShellStateSource,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void DesktopBridgePayloadUsesWebUiCamelCaseContract()
    {
        var script = DesktopBridgeScriptFactory.CreateInitializationScript(
            new DesktopBootstrapPayload
            {
                DesktopMode = true,
                ApiBase = "http://127.0.0.1:15345",
                DesktopSessionId = "desktop-session-1",
                Theme = "white",
                ResolvedTheme = "light",
                SidebarCollapsed = true,
            }
        );

        Assert.Contains("\"desktopMode\":true", script, StringComparison.Ordinal);
        Assert.Contains("\"apiBase\":\"http://127.0.0.1:15345\"", script, StringComparison.Ordinal);
        Assert.Contains(
            "\"desktopSessionId\":\"desktop-session-1\"",
            script,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("\"managementKey\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key", script, StringComparison.Ordinal);
        Assert.Contains("\"theme\":\"white\"", script, StringComparison.Ordinal);
        Assert.Contains("\"resolvedTheme\":\"light\"", script, StringComparison.Ordinal);
        Assert.Contains("\"sidebarCollapsed\":true", script, StringComparison.Ordinal);
        Assert.Contains("const root = document.documentElement;", script, StringComparison.Ordinal);
        Assert.Contains("if (!root)", script, StringComparison.Ordinal);
        Assert.Contains("DOMContentLoaded", script, StringComparison.Ordinal);
        Assert.Contains("data-theme", script, StringComparison.Ordinal);
        Assert.Contains("root.style.colorScheme", script, StringComparison.Ordinal);
        Assert.Contains("payload.resolvedTheme", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "theme: typeof normalized.theme",
            script,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "resolvedTheme: typeof normalized.resolvedTheme",
            script,
            StringComparison.Ordinal
        );
        AssertSourceOrder(
            script,
            "Object.defineProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__'",
            "if (!applyInitialTheme())"
        );
        Assert.DoesNotContain("\"DesktopMode\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ApiBase\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ManagementKey\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\"DesktopSessionId\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WebUiNotificationsUseShellBridgeOnly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "App.tsx"
            ),
            Encoding.UTF8
        );
        var storeSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "stores",
                "useNotificationStore.ts"
            ),
            Encoding.UTF8
        );
        var componentsSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "styles",
                "components.scss"
            ),
            Encoding.UTF8
        );
        var mainLayoutSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "components",
                "layout",
                "MainLayout.tsx"
            ),
            Encoding.UTF8
        );
        var notificationContainerPath = Path.Combine(
            repositoryRoot,
            "resources",
            "webui",
            "upstream",
            "source",
            "src",
            "components",
            "common",
            "NotificationContainer.tsx"
        );

        Assert.False(File.Exists(notificationContainerPath));
        Assert.DoesNotContain("NotificationContainer", appSource, StringComparison.Ordinal);
        Assert.Contains(
            "showShellNotification(message, type);",
            storeSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "notifications: [...state.notifications",
            storeSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            ".notification-container",
            componentsSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "@keyframes notification-enter",
            componentsSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "@keyframes notification-exit",
            componentsSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(".notification {", componentsSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "className=\"notification",
            mainLayoutSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "className=\"theme-menu-popover\"",
            mainLayoutSource,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void FirstRunOnboardingUsesIconActionsSingleLineKeyDisplayAndCountdownConfirm()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml"),
            Encoding.UTF8
        );
        var startupFlowXaml = ReadStartupFlowXaml(repositoryRoot);
        var source =
            ReadMainWindowSources(repositoryRoot)
            + Environment.NewLine
            + ReadStartupFlowSource(repositoryRoot);
        var shellResources = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Views",
                "Resources",
                "ShellResources.xaml"
            ),
            Encoding.UTF8
        );
        var startupFlowResources = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Views",
                "Resources",
                "StartupFlowResources.xaml"
            ),
            Encoding.UTF8
        );
        var appProject = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "CodexCliPlus.App.csproj"),
            Encoding.UTF8
        );
        var startupFlowTextBoxStyle = SliceBetween(
            startupFlowResources,
            "x:Key=\"StartupFlowTextBoxStyle\"",
            "  <Style x:Key=\"StartupFlowPasswordBoxStyle\""
        );
        var startupFlowPasswordBoxStyle = SliceBetween(
            startupFlowResources,
            "x:Key=\"StartupFlowPasswordBoxStyle\"",
            "  <Style x:Key=\"StartupPrimaryButtonStyle\""
        );
        var shellInlineIconButtonStyle = SliceBetween(
            shellResources,
            "x:Key=\"ShellInlineIconButtonStyle\"",
            "  <Style x:Key=\"ShellBrandDockButtonStyle\""
        );
        var startupProtocolLinkTextStyle = SliceBetween(
            startupFlowResources,
            "x:Key=\"StartupProtocolLinkTextStyle\"",
            "  <Style x:Key=\"StartupFooterLinkTextStyle\""
        );
        var startupFlowErrorTextStyle = SliceBetween(
            startupFlowResources,
            "x:Key=\"StartupFlowErrorTextStyle\"",
            "  <Style x:Key=\"StartupFlowKeyDisplayStyle\""
        );
        var startupFlowKeyDisplayStyle = SliceBetween(
            startupFlowResources,
            "x:Key=\"StartupFlowKeyDisplayStyle\"",
            "  <Style x:Key=\"StartupFlowTextBoxStyle\""
        );
        var firstRunActionPanelXaml = SliceBetween(
            startupFlowXaml,
            "<StackPanel Grid.Row=\"3\" Margin=\"0,14,0,0\" VerticalAlignment=\"Bottom\">",
            "          </StackPanel>"
        );
        var firstRunSecurityKeyRegionXaml = SliceBetween(
            startupFlowXaml,
            "x:Name=\"FirstRunSecurityKeyDisplay\"",
            "          </StackPanel>"
        );
        var firstRunSecurityKeyTextXaml = SliceBetween(
            startupFlowXaml,
            "x:Name=\"FirstRunSecurityKeyText\"",
            "                />"
        );
        var loginPanelXaml = SliceBetween(startupFlowXaml, "x:Name=\"LoginPanel\"", "    <Popup");
        var loginPasswordBoxXaml = SliceBetween(
            startupFlowXaml,
            "x:Name=\"ManagementKeyPasswordBox\"",
            "            />"
        );
        var loginErrorTextXaml = SliceBetween(
            startupFlowXaml,
            "x:Name=\"LoginErrorText\"",
            "            />"
        );
        var firstRunConfirmPopupXaml = SliceBetween(
            startupFlowXaml,
            "x:Name=\"FirstRunConfirmPopup\"",
            "    </Popup>"
        );
        var shellPasswordInputStyle = SliceBetween(
            shellResources,
            "x:Key=\"ShellPasswordInputStyle\"",
            "  <Style x:Key=\"ShellIconViewboxStyle\""
        );

        Assert.Contains(
            "Source=\"Views/Resources/ShellResources.xaml\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Source=\"/Views/Resources/StartupFlowResources.xaml\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Key=\"ShellRaisedPanelStyle\"",
            shellResources,
            StringComparison.Ordinal
        );
        Assert.Contains("MahApps.Metro.IconPacks.Lucide", appProject, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Style=\"{StaticResource StartupFlowCardStyle}\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("x:Name=\"ShellTitleBarRow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellContentRow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellTitleBar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MainWindowChromeBehavior\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MainWindowGlowBehavior\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AuthenticationCompactWindowWidth = 320", source, StringComparison.Ordinal);
        Assert.Contains(
            "AuthenticationCompactWindowHeight = 460",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains("EnterAuthenticationCompactWindowMode", source, StringComparison.Ordinal);
        Assert.Contains("ExitAuthenticationCompactWindowMode", source, StringComparison.Ordinal);
        Assert.Contains("ShowFirstRunKeyReveal", source, StringComparison.Ordinal);
        Assert.Contains(
            "EnterAuthenticationCompactWindowMode();",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains("ExitAuthenticationCompactWindowMode();", source, StringComparison.Ordinal);
        Assert.Contains("rememberPassword: false", source, StringComparison.Ordinal);
        Assert.Contains(
            "StartupFlow.ShowLogin(errorMessage, _settings.RememberPassword, _settings.AutoLogin);",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains("WindowBackdropType.None", source, StringComparison.Ordinal);
        Assert.Contains("WindowBackdropType.Auto", source, StringComparison.Ordinal);
        Assert.Contains("ResizeMode.NoResize", source, StringComparison.Ordinal);
        Assert.Contains("ResizeMode.CanResize", source, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"FirstRunSaveToDesktopButton\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("Content=\"保存到桌面\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains(
            "Click=\"FirstRunSaveToDesktopButton_Click\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"FirstRunCopyKeyButton\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("Content=\"复制密钥\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains(
            "Click=\"FirstRunCopyKeyButton_Click\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Click=\"FirstRunEnterManagementButton_Click\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Click=\"FirstRunConfirmContinueButton_Click\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Click=\"FirstRunConfirmCloseButton_Click\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"FirstRunConfirmPanel\"",
            firstRunActionPanelXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"FirstRunConfirmPopup\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "PlacementTarget=\"{Binding ElementName=FirstRunEnterManagementButton}\"",
            firstRunConfirmPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("Placement=\"Bottom\"", firstRunConfirmPopupXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Placement=\"Top\"", firstRunConfirmPopupXaml, StringComparison.Ordinal);
        Assert.Contains(
            "VerticalOffset=\"8\"",
            firstRunConfirmPopupXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "VerticalOffset=\"-8\"",
            firstRunConfirmPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "AllowsTransparency=\"True\"",
            firstRunConfirmPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("StaysOpen=\"True\"", firstRunConfirmPopupXaml, StringComparison.Ordinal);
        Assert.Contains(
            "Width=\"{Binding ActualWidth, ElementName=FirstRunEnterManagementButton}\"",
            firstRunConfirmPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"FirstRunConfirmPanel\"",
            firstRunConfirmPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("FirstRunConfirmPopup.IsOpen = visible;", source, StringComparison.Ordinal);
        Assert.Contains("FirstRunConfirmPopup.IsOpen = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstRunConfirmPanel.Visibility", source, StringComparison.Ordinal);
        Assert.Contains(
            "KeyDown=\"ManagementKeyPasswordBox_KeyDown\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("Click=\"LoginButton_Click\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains(
            "AuthenticationMenuResetItem_Click",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("ForgotSecurityKeyButton", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.Name=\"记住密码\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "AutomationProperties.Name=\"自动登录\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "MouseLeftButtonDown=\"PersistenceOptionLabel_MouseLeftButtonDown\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("静默登录", startupFlowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SilentLogin", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Content=\"本机记住安全密钥\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"FirstRunPersistenceRiskIndicator\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"LoginPersistenceRiskIndicator\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Kind=\"CircleAlert\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "已阅读并同意本机安全密钥使用规则",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "StartupFooterLinkTextStyle",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Text=\"本机凭据保护\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"已阅读并同意本机\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains(
            "BasedOn=\"{StaticResource StartupProtocolTextStyle}\"",
            startupProtocolLinkTextStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"Foreground\" Value=\"#3388CC\" />",
            startupProtocolLinkTextStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Trigger Property=\"IsMouseOver\" Value=\"True\">",
            startupProtocolLinkTextStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"Foreground\" Value=\"#12B7F5\" />",
            startupProtocolLinkTextStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"TextDecorations\" Value=\"Underline\" />",
            startupProtocolLinkTextStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Style=\"{StaticResource StartupProtocolLinkTextStyle}\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("Text=\"安全密钥规则\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains("Cursor=\"Help\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains(
            "Style=\"{StaticResource ShellRiskToolTipStyle}\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("仅在受信任的个人设备上使用", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"AuthenticationMenuButton\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"AuthenticationCloseButton\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("Header=\"忘记安全密钥/重置\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains("安全密钥登录", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Key=\"ShellInlineIconButtonStyle\"",
            shellResources,
            StringComparison.Ordinal
        );
        Assert.Contains("IsMouseOver", shellInlineIconButtonStyle, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IsKeyboardFocusWithin",
            shellInlineIconButtonStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"BorderBrush\" Value=\"Transparent\" />",
            shellInlineIconButtonStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Style=\"{StaticResource StartupFlowKeyDisplayStyle}\"",
            firstRunSecurityKeyRegionXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"Height\" Value=\"40\" />",
            startupFlowKeyDisplayStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"Background\" Value=\"{DynamicResource SurfaceBrush}\" />",
            startupFlowKeyDisplayStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Padding=\"12,0,5,0\"",
            firstRunSecurityKeyRegionXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Padding=\"4,0,72,0\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Key=\"ShellIconViewboxStyle\"",
            shellResources,
            StringComparison.Ordinal
        );
        Assert.Contains("Stretch\" Value=\"Uniform\"", shellResources, StringComparison.Ordinal);
        Assert.Contains("Kind=\"Save\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains("Kind=\"Copy\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("M12,5 L18,11 L12,17", startupFlowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "M8,8 L18,8 L18,18 L8,18 Z",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Key=\"ShellTextToolTipStyle\"",
            shellResources,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Segoe MDL2 Assets", startupFlowXaml, StringComparison.Ordinal);
        Assert.True(
            startupFlowXaml.IndexOf("x:Name=\"FirstRunCopyKeyButton\"", StringComparison.Ordinal)
                < startupFlowXaml.IndexOf(
                    "x:Name=\"FirstRunSaveToDesktopButton\"",
                    StringComparison.Ordinal
                )
        );
        Assert.True(
            startupFlowXaml.IndexOf(
                "x:Name=\"FirstRunSaveToDesktopButton\"",
                StringComparison.Ordinal
            )
                < startupFlowXaml.IndexOf(
                    "x:Name=\"FirstRunEnterManagementButton\"",
                    StringComparison.Ordinal
                )
        );
        Assert.DoesNotContain("MinWidth=\"360\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"64\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains(
            "<ColumnDefinition Width=\"Auto\" />",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "<ColumnDefinition Width=\"76\" />",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "AutomationProperties.AutomationId=\"FirstRunSecurityKeyDisplay\"",
            firstRunSecurityKeyRegionXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "AutomationProperties.AutomationId=\"FirstRunSecurityKeyText\"",
            firstRunSecurityKeyTextXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "TextTrimming=\"CharacterEllipsis\"",
            firstRunSecurityKeyTextXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "TextWrapping=\"NoWrap\"",
            firstRunSecurityKeyTextXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "FirstRunSecurityKeyTextBox",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "Padding=\"4,0,72,0\"",
            firstRunSecurityKeyRegionXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "HorizontalScrollBarVisibility",
            firstRunSecurityKeyRegionXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\"",
            startupFlowTextBoxStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\"",
            startupFlowPasswordBoxStyle,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "VerticalAlignment=\"Stretch\"",
            startupFlowPasswordBoxStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\"",
            startupFlowPasswordBoxStyle,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "HorizontalAlignment=\"Stretch\"",
            startupFlowPasswordBoxStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Grid Grid.Row=\"0\" Height=\"78\">",
            loginPanelXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("Width=\"64\"", loginPanelXaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"64\"", loginPanelXaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0,12,0,0\"", loginPanelXaml, StringComparison.Ordinal);
        Assert.Contains(
            "<StackPanel Grid.Row=\"2\" Margin=\"0,20,0,0\">",
            loginPanelXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Style=\"{StaticResource StartupFlowPasswordBoxStyle}\"",
            loginPasswordBoxXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "HorizontalAlignment=\"Stretch\"",
            loginPasswordBoxXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Height=\"40\"", loginPasswordBoxXaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "VerticalContentAlignment=\"Center\"",
            loginPasswordBoxXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "HorizontalContentAlignment=\"Center\"",
            startupFlowPasswordBoxStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"Height\" Value=\"40\" />",
            startupFlowPasswordBoxStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"Padding\" Value=\"4,0,5,0\" />",
            startupFlowPasswordBoxStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"HorizontalContentAlignment\" Value=\"Left\" />",
            startupFlowPasswordBoxStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"VerticalContentAlignment\" Value=\"Center\" />",
            startupFlowPasswordBoxStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "CornerRadius=\"6\"",
            startupFlowPasswordBoxStyle,
            StringComparison.Ordinal
        );
        Assert.Contains("MinHeight=\"34\"", loginErrorTextXaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "              Height=\"34\"",
            loginErrorTextXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"TextWrapping\" Value=\"Wrap\" />",
            startupFlowErrorTextStyle,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("TextTrimming", startupFlowErrorTextStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("ClipToBounds", startupFlowErrorTextStyle, StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"Background\" Value=\"{DynamicResource SurfaceBrush}\" />",
            startupFlowTextBoxStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"BorderBrush\" Value=\"{DynamicResource BorderBrush}\" />",
            startupFlowTextBoxStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"Background\" Value=\"{DynamicResource SurfaceBrush}\" />",
            startupFlowPasswordBoxStyle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<Setter Property=\"BorderBrush\" Value=\"{DynamicResource BorderBrush}\" />",
            startupFlowPasswordBoxStyle,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("#F9FFFFFF", startupFlowTextBoxStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("#F9FFFFFF", startupFlowPasswordBoxStyle, StringComparison.Ordinal);
        Assert.Contains(
            "VerticalAlignment=\"Stretch\"",
            shellPasswordInputStyle,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\"",
            shellPasswordInputStyle,
            StringComparison.Ordinal
        );
        Assert.Contains("FirstRunSecurityKeyText.Text = key;", source, StringComparison.Ordinal);
        Assert.Contains(
            "FirstRunSecurityKeyText.Text = string.Empty;",
            source,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("FirstRunCopyKeyButton.Focus()", source, StringComparison.Ordinal);
        Assert.Contains("Keyboard.ClearFocus();", source, StringComparison.Ordinal);
        Assert.Contains(
            "FocusManager.SetFocusedElement(this, FirstRunSecurityKeyDisplay);",
            source,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("FirstRunSecurityKeyTextBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollToHome", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CaretIndex", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"CompactAuthenticationHost\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("Text=\"初始化\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"安全密钥登录\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"登录\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("首次初始化安全密钥", startupFlowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "请立即保存下面的安全密钥",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "复制或保存到桌面后再继续",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("确认前请确保密钥已复制", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"FirstRunConfirmCloseButton\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Style=\"{StaticResource StartupLinkButtonStyle}\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Width=\"420\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Background=\"#99000000\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"FirstRunConfirmCancelButton\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Content=\"返回\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"确认进入\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains("buttonText: $\"确认 ({seconds})\"", source, StringComparison.Ordinal);
        Assert.Contains(
            "_settings.ManagementKey = string.Empty;",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ShowLogin(\"初始化已完成，请输入安全密钥登录。\")",
            source,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "StartupFlow.FirstRunRememberPassword",
            source,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("StartupFlow.FirstRunAutoLogin", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FirstRunActionStatusText",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("ShowFirstRunStatus", source, StringComparison.Ordinal);
        Assert.DoesNotContain("可以继续。", source, StringComparison.Ordinal);
        Assert.Contains("MinimumPreparationDisplayDuration", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds", source, StringComparison.Ordinal);
        Assert.Contains("300", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupFlow.ShowLoading", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupFlowScreen.Loading", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsLoadingVisible", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadingBrandBadge", startupFlowXaml, StringComparison.Ordinal);
        Assert.Contains("codexcliplus-display.png", startupFlowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Source=\"pack://application:,,,/Resources/Icons/codexcliplus.ico\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"PreparationProgressTrack\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"PreparationProgressBar\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"PreparationProgressFillScale\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionBrandRing\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionBrandOuterHalo\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionBrandInnerHalo\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionBrandOrbit\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManagementEntryTransitionBrandLightSweep\"",
            xaml,
            StringComparison.Ordinal
        );
        Assert.Contains("Storyboard RepeatBehavior=\"Forever\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScaleTransform ScaleX=\"0.92\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RotateTransform Angle=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ClipToBounds=\"True\"", startupFlowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Name=\"PreparationStepText\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"LoadingWebView2StepText\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"LoadingBackendStepText\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"LoadingSecurityStepText\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "x:Name=\"LoadingWebUiStepText\"",
            startupFlowXaml,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void InitialWindowPresentationWaitsForStableStartupSurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appSource = ReadAppSource(repositoryRoot, "App.xaml.cs");
        var mainWindowSource = ReadAppSource(repositoryRoot, "MainWindow.xaml.cs");
        var startupPresentationSource = ReadAppSource(
            repositoryRoot,
            "MainWindow.ShellStartupPresentation.cs"
        );
        var prepareHiddenStartupSource = SliceBetween(
            startupPresentationSource,
            "private void PrepareHiddenAuthenticationStartupWindow()",
            "private void ShowPreparationStep("
        );
        var showPreparationStepSource = SliceBetween(
            startupPresentationSource,
            "private void ShowPreparationStep(",
            "private void ShowUpgradeNotice()"
        );
        var showUpgradeNoticeSource = SliceBetween(
            startupPresentationSource,
            "private void ShowUpgradeNotice()",
            "private void ShowFirstRunKeyReveal()"
        );
        var showFirstRunKeyRevealSource = SliceBetween(
            startupPresentationSource,
            "private void ShowFirstRunKeyReveal()",
            "private void ShowLogin("
        );
        var showLoginSource = SliceBetween(
            startupPresentationSource,
            "private void ShowLogin(",
            "private void ShowLoginError("
        );
        var showBlockerSource = SliceBetween(
            startupPresentationSource,
            "private void ShowBlocker(",
            "private void ShowWebView()"
        );
        var showWebViewSource = SliceBetween(
            startupPresentationSource,
            "private void ShowWebView()",
            "private void RevealInitialPresentation()"
        );
        var revealSource = SliceBetween(
            startupPresentationSource,
            "private void RevealInitialPresentation()",
            "private static bool ShouldUseManagementEntryTransition()"
        );
        var beginManagementEntryTransitionSource = SliceBetween(
            startupPresentationSource,
            "private async Task BeginManagementEntryTransitionAsync()",
            "private async Task<WindowState> RestoreMainWindowForManagementEntryTransitionAsync()"
        );
        var authenticationChromeSource = SliceBetween(
            startupPresentationSource,
            "private void ApplyAuthenticationCompactWindowChrome()",
            "private void ExitAuthenticationCompactWindowMode()"
        );
        var exitAuthenticationSource = SliceBetween(
            startupPresentationSource,
            "private void ExitAuthenticationCompactWindowMode()",
            "private void ApplyMainWindowModeChrome()"
        );
        var mainWindowModeChromeSource = SliceBetween(
            startupPresentationSource,
            "private void ApplyMainWindowModeChrome()",
            "private WindowLayoutSnapshot CaptureCurrentWindowLayout()"
        );

        Assert.Contains(
            "private bool _initialPresentationRevealed;",
            mainWindowSource,
            StringComparison.Ordinal
        );
        AssertSourceOrder(
            appSource,
            "MainWindow = _serviceProvider.GetRequiredService<MainWindow>();",
            "MainWindow.Show();"
        );
        AssertSourceOrder(
            mainWindowSource,
            "InitializeComponent();",
            "PrepareHiddenAuthenticationStartupWindow();"
        );
        AssertSourceOrder(
            mainWindowSource,
            "PrepareHiddenAuthenticationStartupWindow();",
            "BindStartupFlowEvents();"
        );
        Assert.Contains(
            "_isAuthenticationCompactWindowMode = true;",
            prepareHiddenStartupSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "_preAuthenticationWindowLayout = null;",
            prepareHiddenStartupSource,
            StringComparison.Ordinal
        );
        Assert.Contains("Opacity = 0;", prepareHiddenStartupSource, StringComparison.Ordinal);
        Assert.Contains(
            "ShowInTaskbar = false;",
            prepareHiddenStartupSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "StartupFlow.Visibility = Visibility.Collapsed;",
            prepareHiddenStartupSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ApplyAuthenticationCompactWindowChrome();",
            prepareHiddenStartupSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "CenterAuthenticationCompactWindow();",
            prepareHiddenStartupSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "RevealInitialPresentation",
            prepareHiddenStartupSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ResizeMode.NoResize",
            authenticationChromeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Width = AuthenticationCompactWindowWidth;",
            authenticationChromeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Height = AuthenticationCompactWindowHeight;",
            authenticationChromeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ShellTitleBar.Visibility = Visibility.Collapsed;",
            authenticationChromeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ManagementContentHost.Visibility = Visibility.Collapsed;",
            authenticationChromeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "MainWindowGlowBehavior.GlowDepth = 0;",
            authenticationChromeSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "MainWindowGlowBehavior.GlowDepth = 0;",
            mainWindowModeChromeSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "MainWindowGlowBehavior.GlowDepth = 8;",
            startupPresentationSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "var windowLayout = ResolveAuthenticationExitLayout();",
            exitAuthenticationSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Left = windowLayout.Left;",
            exitAuthenticationSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Top = windowLayout.Top;",
            exitAuthenticationSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "if (_initialPresentationRevealed)",
            revealSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "_initialPresentationRevealed = true;",
            revealSource,
            StringComparison.Ordinal
        );
        AssertSourceOrder(revealSource, "_initialPresentationRevealed = true;", "Opacity = 1;");
        AssertSourceOrder(revealSource, "Opacity = 1;", "ShowInTaskbar = true;");
        Assert.DoesNotContain(
            "RevealInitialPresentation",
            showPreparationStepSource,
            StringComparison.Ordinal
        );

        AssertSourceOrder(
            showLoginSource,
            "EnterAuthenticationCompactWindowMode();",
            "StartupFlow.ShowLogin(errorMessage, _settings.RememberPassword, _settings.AutoLogin);"
        );
        AssertSourceOrder(
            showLoginSource,
            "StartupFlow.Visibility = Visibility.Visible;",
            "RevealInitialPresentation();"
        );
        AssertSourceOrder(
            showFirstRunKeyRevealSource,
            "EnterAuthenticationCompactWindowMode();",
            "StartupFlow.ShowFirstRunKey("
        );
        AssertSourceOrder(
            showFirstRunKeyRevealSource,
            "StartupFlow.Visibility = Visibility.Visible;",
            "RevealInitialPresentation();"
        );
        AssertSourceOrder(
            showUpgradeNoticeSource,
            "ExitAuthenticationCompactWindowMode();",
            "UpgradeNoticePanel.Visibility = Visibility.Visible;"
        );
        AssertSourceOrder(
            showUpgradeNoticeSource,
            "UpgradeNoticePanel.Visibility = Visibility.Visible;",
            "RevealInitialPresentation();"
        );
        AssertSourceOrder(
            showBlockerSource,
            "ExitAuthenticationCompactWindowMode();",
            "BlockerPanel.Visibility = Visibility.Visible;"
        );
        AssertSourceOrder(
            showBlockerSource,
            "BlockerPanel.Visibility = Visibility.Visible;",
            "RevealInitialPresentation();"
        );
        AssertSourceOrder(
            showWebViewSource,
            "ExitAuthenticationCompactWindowMode();",
            "ManagementWebView.Visibility = Visibility.Visible;"
        );
        AssertSourceOrder(
            showWebViewSource,
            "ManagementWebView.Visibility = Visibility.Visible;",
            "RevealInitialPresentation();"
        );
        AssertSourceOrder(
            beginManagementEntryTransitionSource,
            "await ShowManagementEntryTransitionPopupAsync();",
            "RevealInitialPresentation();"
        );
        AssertSourceOrder(
            beginManagementEntryTransitionSource,
            "RevealInitialPresentation();",
            "SetManagementEntryTransitionPhase(ManagementEntryTransitionPhase.LoadingWebView);"
        );
    }

    [Fact]
    public void ShellNotificationsProvideAutoAndManualPlacementContracts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml"),
            Encoding.UTF8
        );
        var hostSource = ReadMainWindowSources(repositoryRoot);
        var serviceSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Services",
                "Notifications",
                "ShellNotificationService.cs"
            ),
            Encoding.UTF8
        );
        var requestSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Services",
                "Notifications",
                "ShellNotificationRequest.cs"
            ),
            Encoding.UTF8
        );

        var autoNotificationPopupXaml = SliceBetween(
            xaml,
            "x:Name=\"AutoNotificationPopup\"",
            "    </Popup>"
        );
        var manualNotificationPopupXaml = SliceBetween(
            xaml,
            "x:Name=\"ManualNotificationPopup\"",
            "    </Popup>"
        );

        Assert.Contains("x:Name=\"AutoNotificationPopup\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "PlacementTarget=\"{Binding ElementName=ManagementContentHost}\"",
            autoNotificationPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Placement=\"Relative\"",
            autoNotificationPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "AllowsTransparency=\"True\"",
            autoNotificationPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("StaysOpen=\"True\"", autoNotificationPopupXaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"AutoNotificationStack\"",
            autoNotificationPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("Width=\"380\"", autoNotificationPopupXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ManualNotificationPopup\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "PlacementTarget=\"{Binding ElementName=ManagementContentHost}\"",
            manualNotificationPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Placement=\"Relative\"",
            manualNotificationPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "AllowsTransparency=\"True\"",
            manualNotificationPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "StaysOpen=\"True\"",
            manualNotificationPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "x:Name=\"ManualNotificationStack\"",
            manualNotificationPopupXaml,
            StringComparison.Ordinal
        );
        Assert.Contains("Width=\"360\"", manualNotificationPopupXaml, StringComparison.Ordinal);
        Assert.Contains("BottomCenterAuto", requestSource, StringComparison.Ordinal);
        Assert.Contains("BottomRightManual", requestSource, StringComparison.Ordinal);
        Assert.Contains("ShellNotificationLevel", requestSource, StringComparison.Ordinal);
        Assert.Contains("Info", requestSource, StringComparison.Ordinal);
        Assert.Contains("Success", requestSource, StringComparison.Ordinal);
        Assert.Contains("Warning", requestSource, StringComparison.Ordinal);
        Assert.Contains("Error", requestSource, StringComparison.Ordinal);
        Assert.Contains("ShowAuto", serviceSource, StringComparison.Ordinal);
        Assert.Contains("ShowManual", serviceSource, StringComparison.Ordinal);
        Assert.Contains("ShowShellNotification", serviceSource, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(2)", hostSource, StringComparison.Ordinal);
        Assert.Contains("AutoNotificationDefaultWidth = 380", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "ManualNotificationDefaultWidth = 360",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("AutoNotificationBottomOffset = 28", hostSource, StringComparison.Ordinal);
        Assert.Contains("ManualNotificationRightOffset = 24", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "ManualNotificationBottomOffset = 24",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ResolveShellNotificationPlacementHost",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("return ManagementContentHost;", hostSource, StringComparison.Ordinal);
        Assert.Contains("return StartupFlow;", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "popup.PlacementTarget = placementHost;",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ApplyShellNotificationStackWidth(owner, hostWidth, bottomCenter);",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("Math.Min(defaultWidth, hostWidth)", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "UpdateShellNotificationPopupPlacement",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("(hostWidth - ownerWidth) / 2", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "hostWidth - ownerWidth - ManualNotificationRightOffset",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "RefreshShellNotificationPopupPlacements",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("RefreshDockPopupPlacement(popup)", hostSource, StringComparison.Ordinal);
        Assert.Contains("FadeOutAndRemoveAsync", hostSource, StringComparison.Ordinal);
        Assert.Contains("MaxVisibleShellNotifications = 3", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "EnforceShellNotificationCapacity(AutoNotificationStack);",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "EnforceShellNotificationCapacity(ManualNotificationStack);",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("_notificationService.ShowAuto", hostSource, StringComparison.Ordinal);
        Assert.Contains("_notificationService.ShowManual", hostSource, StringComparison.Ordinal);
        Assert.Contains("ScaleTransform.ScaleXProperty", hostSource, StringComparison.Ordinal);
        Assert.Contains("UpdateNotificationCardClip", hostSource, StringComparison.Ordinal);
        Assert.Contains("new RectangleGeometry", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "progress.Width = card.ActualWidth",
            hostSource,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void NativeLoginShortValidationErrorsUseAutoShellNotifications()
    {
        var repositoryRoot = FindRepositoryRoot();
        var startupSource = ReadAppSource(repositoryRoot, "MainWindow.StartupAndSecurity.cs");

        var emptyPasswordBranch = SliceBetween(
            startupSource,
            "if (string.IsNullOrWhiteSpace(managementKey))",
            "StartupFlow.SetLoginBusy(true);"
        );
        Assert.Contains(
            "StartupFlow.SetLoginError(null);",
            emptyPasswordBranch,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "_notificationService.ShowAuto(\"请输入安全密钥。\", ShellNotificationLevel.Warning);",
            emptyPasswordBranch,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("ShowLoginError(", emptyPasswordBranch, StringComparison.Ordinal);

        var wrongPasswordBranch = SliceBetween(
            startupSource,
            "if (!_backendConfigWriter.VerifyManagementKey(managementKey))",
            "_settings.ManagementKey = managementKey;"
        );
        Assert.Contains(
            "StartupFlow.SetLoginError(null);",
            wrongPasswordBranch,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "_notificationService.ShowAuto(\"安全密钥不正确。\", ShellNotificationLevel.Error);",
            wrongPasswordBranch,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("ShowLoginError(", wrongPasswordBranch, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "StartupFlow.ClearLoginPassword();",
            wrongPasswordBranch,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ShellNotificationLevelsMapToAutoAndManualDestinations()
    {
        var repositoryRoot = FindRepositoryRoot();
        var hostSource = ReadMainWindowSources(repositoryRoot);
        var serviceSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Services",
                "Notifications",
                "ShellNotificationService.cs"
            ),
            Encoding.UTF8
        );
        var requestSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Services",
                "Notifications",
                "ShellNotificationRequest.cs"
            ),
            Encoding.UTF8
        );

        Assert.Contains("case \"shellNotification\":", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "ReadString(root, \"notificationType\")",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("ReadShellNotificationLevel", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "\"success\" => ShellNotificationLevel.Success",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "\"warning\" => ShellNotificationLevel.Warning",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "\"error\" => ShellNotificationLevel.Error",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains("_ => ShellNotificationLevel.Info", hostSource, StringComparison.Ordinal);
        Assert.Contains("ShowShellNotification", serviceSource, StringComparison.Ordinal);
        Assert.Contains(
            "if (level == ShellNotificationLevel.Error)",
            serviceSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ShowManual(\"操作失败\", message, ShellNotificationLevel.Error);",
            serviceSource,
            StringComparison.Ordinal
        );
        Assert.Contains("ShowAuto(message, level);", serviceSource, StringComparison.Ordinal);
        Assert.Contains("Warning", requestSource, StringComparison.Ordinal);
        Assert.Contains("Error", requestSource, StringComparison.Ordinal);
        Assert.Contains("ShellNotificationLevel.Info", requestSource, StringComparison.Ordinal);
        Assert.Contains("Success", requestSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellNotificationsLimitStacksAndReplaceOldestOverflow()
    {
        var repositoryRoot = FindRepositoryRoot();
        var hostSource = ReadMainWindowSources(repositoryRoot);

        Assert.Contains(
            "private const int MaxVisibleShellNotifications = 3;",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "while (CountActiveShellNotifications(owner) >= MaxVisibleShellNotifications)",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "FirstOrDefault(card => !_removingShellNotifications.Contains(card))",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "_ = FadeOutAndRemoveAsync(owner, oldest);",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "if (!_removingShellNotifications.Add(card))",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "await FadeOutAndRemoveAsync(owner, oldest);",
            hostSource,
            StringComparison.Ordinal
        );
        AssertSourceOrder(
            hostSource,
            "EnforceShellNotificationCapacity(AutoNotificationStack);",
            "AutoNotificationStack.Children.Add(card);"
        );
        AssertSourceOrder(
            hostSource,
            "EnforceShellNotificationCapacity(ManualNotificationStack);",
            "ManualNotificationStack.Children.Add(card);"
        );
        AssertSourceOrder(
            hostSource,
            "var oldest = owner",
            "_ = FadeOutAndRemoveAsync(owner, oldest);"
        );
    }

    [Fact]
    public void SettingsUpdateStatusUsesShortInlineMessages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", "MainWindow.xaml"),
            Encoding.UTF8
        );
        var hostSource = ReadMainWindowSources(repositoryRoot);

        Assert.Contains("Text=\"更新状态：正在读取。\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SetSettingsUpdateStatus", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "SetSettingsUpdateStatus(\"检查中\")",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "SetSettingsUpdateStatus(\"检查失败\")",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "SetSettingsUpdateStatus(\"应用失败\")",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "SettingsUpdateStatusText.Text = $\"检查更新失败：{exception.Message}\"",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "SettingsUpdateStatusText.Text = $\"应用更新失败：{exception.Message}\"",
            hostSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("UsageDatabasePath", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstRunDesktopSavePathUsesOnlySystemDesktopDirectory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadMainWindowSources(repositoryRoot);

        Assert.Contains(
            "Environment.SpecialFolder.DesktopDirectory",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Directory.Exists(normalizedDesktopDirectory)",
            source,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("SpecialFolder.UserProfile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("USERPROFILE", source, StringComparison.Ordinal);

        var method = typeof(CodexCliPlus.MainWindow).GetMethod(
            "BuildDesktopSecurityKeyFilePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        Assert.NotNull(method);

        var desktopDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory
        );
        if (string.IsNullOrWhiteSpace(desktopDirectory) || !Directory.Exists(desktopDirectory))
        {
            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                method.Invoke(null, null)
            );
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            return;
        }

        var filePath = Assert.IsType<string>(method.Invoke(null, null));
        var expectedDirectory = Path.GetFullPath(desktopDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var actualDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath))!
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Assert.True(
            string.Equals(expectedDirectory, actualDirectory, StringComparison.OrdinalIgnoreCase),
            $"Expected '{actualDirectory}' to match the system desktop '{expectedDirectory}'."
        );
    }

    [Fact]
    public void DesktopWebUiRoutesLoginThroughNativeShell()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "App.tsx"
            ),
            Encoding.UTF8
        );
        var protectedRouteSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "router",
                "ProtectedRoute.tsx"
            ),
            Encoding.UTF8
        );
        var loginRouteSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "router",
                "LoginRoute.tsx"
            ),
            Encoding.UTF8
        );
        var browserManagementBlockedSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "router",
                "BrowserManagementBlocked.tsx"
            ),
            Encoding.UTF8
        );
        var bridgeSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "desktop",
                "bridge.ts"
            ),
            Encoding.UTF8
        );
        var authStoreSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "stores",
                "useAuthStore.ts"
            ),
            Encoding.UTF8
        );
        var constantsSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "utils",
                "constants.ts"
            ),
            Encoding.UTF8
        );
        var mainLayoutSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "components",
                "layout",
                "MainLayout.tsx"
            ),
            Encoding.UTF8
        );
        var mainRoutesSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "router",
                "MainRoutes.tsx"
            ),
            Encoding.UTF8
        );
        var dashboardOverviewPageSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "pages",
                "DashboardOverviewPage.tsx"
            ),
            Encoding.UTF8
        );
        var overlayDashboardOverviewPageSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "modules",
                "cpa-uv-overlay",
                "source",
                "src",
                "pages",
                "DashboardOverviewPage.tsx"
            ),
            Encoding.UTF8
        );
        var overlayMainRoutesSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "modules",
                "cpa-uv-overlay",
                "source",
                "src",
                "router",
                "MainRoutes.tsx"
            ),
            Encoding.UTF8
        );
        var logsPageSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "pages",
                "LogsPage.tsx"
            ),
            Encoding.UTF8
        );
        var systemPageSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "pages",
                "SystemPage.tsx"
            ),
            Encoding.UTF8
        );
        var themeStoreSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "stores",
                "useThemeStore.ts"
            ),
            Encoding.UTF8
        );
        var commonTypesSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "types",
                "common.ts"
            ),
            Encoding.UTF8
        );

        Assert.Contains(
            "{ path: '/login', element: <LoginRoute /> }",
            appSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "const desktopMode = isDesktopMode();",
            appSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("isDesktopMode()", appSource, StringComparison.Ordinal);
        Assert.Contains("isDesktopMode()", loginRouteSource, StringComparison.Ordinal);
        Assert.Contains(
            "return <Navigate to=\"/\" replace />;",
            loginRouteSource,
            StringComparison.Ordinal
        );
        Assert.Contains("<BrowserManagementBlocked />", loginRouteSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LoginPage", loginRouteSource, StringComparison.Ordinal);
        Assert.Contains(
            "请在桌面应用内打开管理界面",
            browserManagementBlockedSource,
            StringComparison.Ordinal
        );
        Assert.Contains("restoreSession", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("isDesktopMode()", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains(
            "return <BrowserManagementBlocked />;",
            protectedRouteSource,
            StringComparison.Ordinal
        );
        Assert.Contains("桌面会话需要恢复", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("返回登录", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("setRetryAttempt", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("requestNativeLogin", protectedRouteSource, StringComparison.Ordinal);
        Assert.Contains("requestNativeLogin?:", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("postMessage?:", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("shellStateChanged?:", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("subscribeDesktopShellCommand", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("sendShellStateChanged", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("!desktopMode &&", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("desktop-shell", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("toggleSidebarCollapsed", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains(
            "applyDesktopTheme(command.theme, command.resolvedTheme, command.transitionMs)",
            mainLayoutSource,
            StringComparison.Ordinal
        );
        Assert.Contains("command.type === 'navigate'", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("pathname: location.pathname", mainLayoutSource, StringComparison.Ordinal);
        Assert.DoesNotContain("themeMenuOpen", mainLayoutSource, StringComparison.Ordinal);
        Assert.DoesNotContain("THEME_CARDS", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("path: '/dashboard/overview'", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("t('nav.dashboard_overview')", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("path: '/codex-config'", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("t('nav.codex_config')", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("t('nav.config_management')", mainLayoutSource, StringComparison.Ordinal);
        AssertSourceOrder(mainLayoutSource, "path: '/codex-config'", "path: '/config'");
        Assert.DoesNotContain(
            "t('nav.runtime_overview')",
            mainLayoutSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("t('nav.console')", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("path: '/accounts'", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains("t('nav.account_center')", mainLayoutSource, StringComparison.Ordinal);
        Assert.DoesNotContain("path: '/ai-providers'", mainLayoutSource, StringComparison.Ordinal);
        Assert.DoesNotContain("path: '/auth-files'", mainLayoutSource, StringComparison.Ordinal);
        Assert.DoesNotContain("path: '/quota'", mainLayoutSource, StringComparison.Ordinal);
        Assert.DoesNotContain("path: '/oauth'", mainLayoutSource, StringComparison.Ordinal);
        Assert.Contains(
            "const AccountCenterPage = lazyPage(",
            mainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "const CodexConfigPage = lazyPage(",
            mainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "{ path: '/accounts', element: route('accounts', <AccountCenterPage />) }",
            mainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "{ path: '/ai-providers', element: <Navigate to=\"/accounts#codex-config\" replace /> }",
            mainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "{ path: '/auth-files', element: <Navigate to=\"/accounts#auth-files\" replace /> }",
            mainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "{ path: '/quota', element: <Navigate to=\"/accounts#quota-management\" replace /> }",
            mainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("AiProvidersPage", mainRoutesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthFilesPage", mainRoutesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("QuotaPage", mainRoutesSource, StringComparison.Ordinal);
        Assert.Contains(
            "const DashboardOverviewPage = lazyPage(",
            mainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "{ path: '/dashboard/overview', element: route('dashboard-overview', <DashboardOverviewPage />) }",
            mainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "{ path: '/console', element: <Navigate to=\"/dashboard/overview\" replace /> }",
            mainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "{ path: '/codex-config', element: route('codex-config', <CodexConfigPage />) }",
            mainRoutesSource,
            StringComparison.Ordinal
        );
        AssertSourceOrder(mainRoutesSource, "path: '/codex-config'", "path: '/config'");
        Assert.Contains("path: \"/console\"", overlayMainRoutesSource, StringComparison.Ordinal);
        Assert.Contains("path: \"/accounts\"", overlayMainRoutesSource, StringComparison.Ordinal);
        Assert.Contains(
            "path: \"/codex-config\"",
            overlayMainRoutesSource,
            StringComparison.Ordinal
        );
        AssertSourceOrder(overlayMainRoutesSource, "path: \"/codex-config\"", "path: \"/config\"");
        Assert.Contains(
            "element: <Navigate to=\"/accounts#codex-config\" replace />",
            overlayMainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "element: <Navigate to=\"/accounts#auth-files\" replace />",
            overlayMainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "element: <Navigate to=\"/accounts#quota-management\" replace />",
            overlayMainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "element: <Navigate to=\"/dashboard/overview\" replace />",
            overlayMainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "RuntimeOverviewPage",
            overlayMainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "runtime-overview",
            overlayMainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("ConsolePage", mainRoutesSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "{ path: '/dashboard/overview', element: <Navigate to=\"/console\" replace /> }",
            mainRoutesSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "export function DashboardOverviewPage()",
            dashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "requestCodexRouteState",
            dashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains("switchCodexRoute", dashboardOverviewPageSource, StringComparison.Ordinal);
        Assert.Contains(
            "当前：{codexRouteModeLabel}",
            dashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "selectedCodexRouteTargetId",
            dashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains("Codex 路由目标", dashboardOverviewPageSource, StringComparison.Ordinal);
        Assert.Contains("选择路由", dashboardOverviewPageSource, StringComparison.Ordinal);
        Assert.Contains("检测失败", dashboardOverviewPageSource, StringComparison.Ordinal);
        Assert.Contains("应用", dashboardOverviewPageSource, StringComparison.Ordinal);
        Assert.Contains(
            "usage_stats.total_5h_remaining_quota",
            dashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "FIVE_HOUR_WINDOW_ID = 'five-hour'",
            dashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains("label: '加载中'", dashboardOverviewPageSource, StringComparison.Ordinal);
        Assert.Contains(
            "已汇总 ${formatLocaleNumber(validCount, i18n.language)}",
            dashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "requestCodexRouteState",
            overlayDashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "switchCodexRoute",
            overlayDashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "当前：{codexRouteModeLabel}",
            overlayDashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "selectedCodexRouteTargetId",
            overlayDashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Codex 路由目标",
            overlayDashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains("选择路由", overlayDashboardOverviewPageSource, StringComparison.Ordinal);
        Assert.Contains("检测失败", overlayDashboardOverviewPageSource, StringComparison.Ordinal);
        Assert.Contains("应用", overlayDashboardOverviewPageSource, StringComparison.Ordinal);
        Assert.Contains(
            "usage_stats.total_5h_remaining_quota",
            overlayDashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "FIVE_HOUR_WINDOW_ID = 'five-hour'",
            overlayDashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "label: '加载中'",
            overlayDashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "已汇总 ${formatLocaleNumber(validCount, i18n.language)}",
            overlayDashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "export function ConsolePage()",
            dashboardOverviewPageSource,
            StringComparison.Ordinal
        );
        Assert.Contains("'requestLog'", logsPageSource, StringComparison.Ordinal);
        Assert.Contains("logs.request_log_tab", logsPageSource, StringComparison.Ordinal);
        Assert.Contains("configApi.updateRequestLog", logsPageSource, StringComparison.Ordinal);
        Assert.DoesNotContain("request-log-modal", systemPageSource, StringComparison.Ordinal);
        Assert.DoesNotContain("key: 'light'", mainLayoutSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "export type Theme = 'light'",
            commonTypesSource,
            StringComparison.Ordinal
        );
        Assert.Contains("applyDesktopTheme", themeStoreSource, StringComparison.Ordinal);
        Assert.Contains(
            "desktopBootstrap.resolvedTheme",
            themeStoreSource,
            StringComparison.Ordinal
        );
        Assert.Contains("theme-transitioning", themeStoreSource, StringComparison.Ordinal);
        Assert.Contains("if (theme === 'light')", themeStoreSource, StringComparison.Ordinal);
        Assert.Contains(
            "prefers-reduced-motion: reduce",
            themeStoreSource,
            StringComparison.Ordinal
        );
        Assert.Contains("root.style.colorScheme", themeStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("setTheme: (theme)", themeStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("cycleTheme", themeStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("STORAGE_KEY_THEME", constantsSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "theme: normalizeDesktopTheme(state.theme)",
            bridgeSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "resolvedTheme: normalizeResolvedTheme(state.resolvedTheme)",
            bridgeSource,
            StringComparison.Ordinal
        );
        Assert.Contains("__CODEXCLIPLUS_DESKTOP_BRIDGE__", bridgeSource, StringComparison.Ordinal);
        Assert.Contains(
            "window.chrome?.webview?.postMessage",
            bridgeSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "desktopBootstrapCache = null",
            bridgeSource,
            StringComparison.Ordinal
        );
        Assert.Contains("restoreSessionPromise = null", authStoreSource, StringComparison.Ordinal);
        Assert.Contains(
            "resetRestoreSessionPromise = true",
            authStoreSource,
            StringComparison.Ordinal
        );
        Assert.Contains("if (!desktopBootstrap)", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("clearBrowserManagementSession", authStoreSource, StringComparison.Ordinal);
        Assert.Contains(
            "throw new Error('管理界面只能在桌面应用内打开。')",
            authStoreSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "normalizeApiBase(desktopBootstrap.apiBase)",
            authStoreSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "detectApiBaseFromLocation",
            authStoreSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "obfuscatedStorage.migratePlaintextKeys",
            authStoreSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "localStorage.setItem('isLoggedIn'",
            authStoreSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "managementKey: state.managementKey",
            authStoreSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("currentState.apiBase", authStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "desktopBootstrap?.apiBase ||",
            authStoreSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "STORAGE_KEY_AUTH = 'codexcliplus-auth'",
            constantsSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "STORAGE_KEY_AUTH = 'cli-proxy-auth'",
            constantsSource,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void VendoredWebUiSourceContainsDesktopRecoveryBridge()
    {
        var repositoryRoot = FindRepositoryRoot();
        var bridgeSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "desktop",
                "bridge.ts"
            ),
            Encoding.UTF8
        );
        var protectedRouteSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "router",
                "ProtectedRoute.tsx"
            ),
            Encoding.UTF8
        );

        Assert.Contains("requestNativeLogin", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("shellStateChanged", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("toggleSidebarCollapsed", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("navigate", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("requestCodexRouteState", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("switchCodexRoute", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("pathname", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("桌面会话需要恢复", protectedRouteSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardContainsLocalEnvironmentSnapshotAndRepairSurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var dashboardSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "pages",
                "DashboardPage.tsx"
            ),
            Encoding.UTF8
        );
        var bridgeSource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "desktop",
                "bridge.ts"
            ),
            Encoding.UTF8
        );
        var zhCn = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "resources",
                "webui",
                "upstream",
                "source",
                "src",
                "i18n",
                "locales",
                "zh-CN.json"
            ),
            Encoding.UTF8
        );

        Assert.Contains(
            "requestLocalDependencySnapshot",
            dashboardSource,
            StringComparison.Ordinal
        );
        Assert.Contains("runLocalDependencyRepair", dashboardSource, StringComparison.Ordinal);
        Assert.Contains(
            "repair-required-env-install-latest-codex",
            dashboardSource,
            StringComparison.Ordinal
        );
        Assert.Contains("local_environment", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("repairCapabilities", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("LocalDependencyRepairProgress", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("localDependencyRepairResult", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("localDependencyRepairProgress", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("LocalDependencyRepairProgress", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("本地环境", zhCn, StringComparison.Ordinal);
        Assert.Contains("一键修复并安装最新 Codex", zhCn, StringComparison.Ordinal);
        Assert.Contains("日志路径", zhCn, StringComparison.Ordinal);
        Assert.Contains("修复入口仅在桌面模式可用", zhCn, StringComparison.Ordinal);
    }

    [Fact]
    public void VendoredWebUiMetadataPinsUpstreamCommit()
    {
        var repositoryRoot = FindRepositoryRoot();
        var metadataPath = Path.Combine(
            repositoryRoot,
            "resources",
            "webui",
            "upstream",
            "sync.json"
        );
        var metadata = File.ReadAllText(metadataPath, Encoding.UTF8);

        Assert.Contains(
            "router-for-me/Cli-Proxy-API-Management-Center.git",
            metadata,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "b45639aa0169de8441bc964fb765f2405c10ccf4",
            metadata,
            StringComparison.Ordinal
        );
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodexCliPlus.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string NormalizeLineEndings(string source)
    {
        return source.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void AssertSourceOrder(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Expected to find '{first}'.");
        var secondIndex = source.IndexOf(second, firstIndex, StringComparison.Ordinal);
        Assert.True(secondIndex > firstIndex, $"Expected to find '{second}' after '{first}'.");
    }

    private static string ReadMainWindowSources(string repositoryRoot)
    {
        var appDirectory = Path.Combine(repositoryRoot, "src", "CodexCliPlus.App");
        var sourceFiles = Directory
            .GetFiles(appDirectory, "MainWindow*.cs")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal);

        return string.Join(
            Environment.NewLine,
            sourceFiles.Select(path => File.ReadAllText(path, Encoding.UTF8))
        );
    }

    private static string ReadAppSource(string repositoryRoot, string fileName)
    {
        return File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "CodexCliPlus.App", fileName),
            Encoding.UTF8
        );
    }

    private static string ReadStartupFlowXaml(string repositoryRoot)
    {
        return File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Views",
                "Controls",
                "StartupFlowView.xaml"
            ),
            Encoding.UTF8
        );
    }

    private static string ReadStartupFlowSource(string repositoryRoot)
    {
        return File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.App",
                "Views",
                "Controls",
                "StartupFlowView.xaml.cs"
            ),
            Encoding.UTF8
        );
    }

    private static string SliceBetween(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Expected to find '{start}'.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"Expected to find '{end}'.");
        return source[startIndex..endIndex];
    }
}
