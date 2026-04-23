using System.Windows;

using DesktopHost.Core.Abstractions.Build;
using DesktopHost.Core.Abstractions.Configuration;
using DesktopHost.Core.Abstractions.Logging;
using DesktopHost.Core.Models;
using DesktopHost.Infrastructure.Backend;
using DesktopHost.Infrastructure.DependencyInjection;
using DesktopHost.Infrastructure.Diagnostics;
using DesktopHost.Services;
using DesktopHost.ViewModels;

using Microsoft.Extensions.DependencyInjection;

using MessageBox = System.Windows.MessageBox;

namespace DesktopHost;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private IAppLogger? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _serviceProvider = ConfigureServices();
            var isSmoke = e.Args.Contains("--smoke", StringComparer.OrdinalIgnoreCase);
            var verifyHosting = e.Args.Contains("--verify-hosting", StringComparer.OrdinalIgnoreCase);
            var verifyOnboarding = e.Args.Contains("--verify-onboarding", StringComparer.OrdinalIgnoreCase);

            var configurationService = _serviceProvider.GetRequiredService<IDesktopConfigurationService>();
            _logger = _serviceProvider.GetRequiredService<IAppLogger>();

            var settings = await configurationService.LoadAsync();
            _logger.Info(
                $"Application startup. BackendPort={settings.BackendPort}, PreferredSource={settings.PreferredCodexSource}.");

            if (!isSmoke && (verifyOnboarding || !settings.OnboardingCompleted))
            {
                var onboardingWindow = _serviceProvider.GetRequiredService<OnboardingWindow>();
                onboardingWindow.ConfigureAutomation(verifyOnboarding);
                var onboardingResult = onboardingWindow.ShowDialog();
                if (onboardingResult != true)
                {
                    Shutdown(Environment.ExitCode == 0 ? -1 : Environment.ExitCode);
                    return;
                }

                if (verifyOnboarding && !verifyHosting)
                {
                    var backendProcessManager = _serviceProvider.GetRequiredService<BackendProcessManager>();
                    await backendProcessManager.StopAsync();
                    Shutdown(0);
                    return;
                }
            }

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.ConfigureAutomation(verifyHosting || verifyOnboarding);
            MainWindow = mainWindow;
            mainWindow.Show();

            if (isSmoke)
            {
                _logger.Info("Smoke mode enabled.");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(900));
                    await (await mainWindow.Dispatcher.InvokeAsync(() => mainWindow.CloseForAutomationAsync()));
                });
            }
        }
        catch (Exception exception)
        {
            TryCreateStartupSnapshot(exception);

            MessageBox.Show(
                $"桌面宿主启动失败：{exception.Message}",
                "CPAD 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("Application exiting.");
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void TryCreateStartupSnapshot(Exception exception)
    {
        try
        {
            var diagnosticsService = _serviceProvider?.GetService<DiagnosticsService>();
            diagnosticsService?.CreateErrorSnapshot(
                "桌面宿主启动失败",
                exception.Message,
                exception,
                new BackendStatusSnapshot(),
                new CodexStatusSnapshot(),
                new DependencyCheckResult());
        }
        catch
        {
            // Ignore secondary snapshot errors during startup.
        }
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddDesktopHostInfrastructure();
        services.AddSingleton<IBuildInfo, BuildInfo>();
        services.AddSingleton<WebView2RuntimeService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<OnboardingWindow>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
