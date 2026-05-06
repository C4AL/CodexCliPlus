using CodexCliPlus.Core.Abstractions.Build;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Infrastructure.DependencyInjection;
using CodexCliPlus.Infrastructure.LocalEnvironment;
using CodexCliPlus.Services;
using CodexCliPlus.Services.Notifications;
using CodexCliPlus.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CodexCliPlus;

public partial class App : System.Windows.Application, IDisposable
{
    private const string SingleInstanceMutexName = "BlackblockInc.CodexCliPlus.App";
    private const string SingleInstanceWakeEventName = "BlackblockInc.CodexCliPlus.App.Wake";

    private ServiceProvider? _serviceProvider;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _singleInstanceWakeEvent;
    private CancellationTokenSource? _singleInstanceWakeCancellation;
    private Task? _singleInstanceWakeTask;

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

        if (!TryAcquireSingleInstance())
        {
            Shutdown(0);
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
        _serviceProvider = null;
        StopSingleInstanceWakeListener();
        base.OnExit(e);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        _serviceProvider = null;
        StopSingleInstanceWakeListener();
        GC.SuppressFinalize(this);
    }

    private bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceWakeEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                SingleInstanceWakeEventName
            );
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                SingleInstanceMutexName,
                out var createdNew
            );

            if (!createdNew)
            {
                _singleInstanceWakeEvent.Set();
                _singleInstanceWakeEvent.Dispose();
                _singleInstanceWakeEvent = null;
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                return false;
            }

            _singleInstanceWakeCancellation = new CancellationTokenSource();
            _singleInstanceWakeTask = Task.Run(() =>
                ListenForSingleInstanceWakeRequests(
                    _singleInstanceWakeEvent!,
                    _singleInstanceWakeCancellation.Token
                )
            );
            return true;
        }
        catch
        {
            StopSingleInstanceWakeListener();
            return true;
        }
    }

    private void ListenForSingleInstanceWakeRequests(
        EventWaitHandle wakeEvent,
        CancellationToken cancellationToken
    )
    {
        WaitHandle[] handles = [wakeEvent, cancellationToken.WaitHandle];
        while (!cancellationToken.IsCancellationRequested)
        {
            var signaled = WaitHandle.WaitAny(handles);
            if (signaled != 0 || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RestoreFromExternalActivation();
                }
            });
        }
    }

    private void StopSingleInstanceWakeListener()
    {
        try
        {
            _singleInstanceWakeCancellation?.Cancel();
            _singleInstanceWakeEvent?.Set();
            _singleInstanceWakeTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch { }

        _singleInstanceWakeCancellation?.Dispose();
        _singleInstanceWakeCancellation = null;
        _singleInstanceWakeTask = null;

        _singleInstanceWakeEvent?.Dispose();
        _singleInstanceWakeEvent = null;

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch { }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
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
