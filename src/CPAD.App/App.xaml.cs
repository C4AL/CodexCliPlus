using CPAD.Core.Abstractions.Build;
using CPAD.Infrastructure.Backend;
using CPAD.Infrastructure.DependencyInjection;
using CPAD.Services;
using CPAD.ViewModels;

using Microsoft.Extensions.DependencyInjection;

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
