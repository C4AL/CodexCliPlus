using CPAD.Core.Abstractions.Build;
using CPAD.Core.Abstractions.Management;
using CPAD.Infrastructure.DependencyInjection;
using CPAD.Services;
using CPAD.Services.SecondaryRoutes;
using CPAD.ViewModels;
using CPAD.ViewModels.Pages;
using CPAD.Views.Pages;

using Microsoft.Extensions.DependencyInjection;

using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace CPAD;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddCpadInfrastructure();
        services.AddSingleton<IBuildInfo, BuildInfo>();
        services.AddNavigationViewPageProvider();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<NotifyIconViewModel>();
        services.AddSingleton<IManagementNavigationService, ManagementNavigationService>();
        services.AddSingleton<IUnsavedChangesGuard, UnsavedChangesGuard>();
        services.AddSingleton<AiProvidersRouteState>();
        services.AddSingleton<AuthFilesRouteState>();
        services.AddSingleton<ConfigPageState>();
        services.AddSingleton<LogsPageState>();

        services.AddSingleton<DashboardPageViewModel>();
        services.AddSingleton<ConfigPageViewModel>();
        services.AddSingleton<AiProvidersPageViewModel>();
        services.AddSingleton<AuthFilesPageViewModel>();
        services.AddSingleton<OAuthPageViewModel>();
        services.AddSingleton<QuotaPageViewModel>();
        services.AddSingleton<UsagePageViewModel>();
        services.AddSingleton<LogsPageViewModel>();
        services.AddSingleton<SystemPageViewModel>();

        services.AddSingleton<DashboardPage>();
        services.AddSingleton<ConfigPage>();
        services.AddSingleton<AiProvidersPage>();
        services.AddSingleton<AuthFilesPage>();
        services.AddSingleton<OAuthPage>();
        services.AddSingleton<QuotaPage>();
        services.AddSingleton<UsagePage>();
        services.AddSingleton<LogsPage>();
        services.AddSingleton<SystemPage>();

        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
