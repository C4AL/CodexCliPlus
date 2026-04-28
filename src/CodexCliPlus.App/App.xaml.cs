using CodexCliPlus.Core.Abstractions.Build;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Infrastructure.DependencyInjection;
using CodexCliPlus.Services;
using CodexCliPlus.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace CodexCliPlus;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddCpadInfrastructure();
        services.AddSingleton<IBuildInfo, BuildInfo>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<WebUiAssetLocator>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        try
        {
            _serviceProvider?.GetService<BackendProcessManager>()?.StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
