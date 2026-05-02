using CodexCliPlus.Core.Abstractions.Build;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Infrastructure.DependencyInjection;
using CodexCliPlus.Infrastructure.LocalEnvironment;
using CodexCliPlus.Services;
using CodexCliPlus.Services.Notifications;
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

        if (TryParseRepairMode(e.Args, out var repairActionId, out var repairStatusPath))
        {
            _serviceProvider = services.BuildServiceProvider();
            var repairService = _serviceProvider.GetRequiredService<LocalDependencyRepairService>();
            var result = repairService
                .ExecuteRepairModeAsync(repairActionId, repairStatusPath)
                .GetAwaiter()
                .GetResult();
            _serviceProvider.Dispose();
            _serviceProvider = null;
            Shutdown(result.Succeeded ? 0 : 1);
            return;
        }

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<WebUiAssetLocator>();
        services.AddSingleton<ShellNotificationService>();
        services.AddSingleton<ManagementChangeBroadcastService>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        try
        {
            var backendProcessManager = _serviceProvider?.GetService<BackendProcessManager>();
            if (backendProcessManager is not null)
            {
                Task.Run(() => backendProcessManager.StopAsync()).GetAwaiter().GetResult();
            }
        }
        catch { }

        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static bool TryParseRepairMode(
        string[] args,
        out string actionId,
        out string statusPath
    )
    {
        actionId = string.Empty;
        statusPath = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            if (
                string.Equals(args[index], "--repair", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length
            )
            {
                actionId = args[index + 1];
                index++;
                continue;
            }

            if (
                string.Equals(args[index], "--status", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length
            )
            {
                statusPath = args[index + 1];
                index++;
            }
        }

        return !string.IsNullOrWhiteSpace(actionId) && !string.IsNullOrWhiteSpace(statusPath);
    }
}
