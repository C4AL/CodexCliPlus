using DesktopHost.Core.Abstractions.Configuration;
using DesktopHost.Core.Abstractions.Logging;
using DesktopHost.Core.Abstractions.Paths;
using DesktopHost.Core.Abstractions.Processes;
using DesktopHost.Infrastructure.Backend;
using DesktopHost.Infrastructure.Codex;
using DesktopHost.Infrastructure.Configuration;
using DesktopHost.Infrastructure.Diagnostics;
using DesktopHost.Infrastructure.Logging;
using DesktopHost.Infrastructure.Paths;
using DesktopHost.Infrastructure.Platform;
using DesktopHost.Infrastructure.Processes;

using Microsoft.Extensions.DependencyInjection;

namespace DesktopHost.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopHostInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddHttpClient<BackendAssetService>();
        services.AddSingleton<IPathService, DesktopPathService>();
        services.AddSingleton<IAppLogger, FileAppLogger>();
        services.AddSingleton<IDesktopConfigurationService, JsonDesktopConfigurationService>();
        services.AddSingleton<IProcessService, SystemProcessService>();
        services.AddSingleton<DirectoryAccessService>();
        services.AddSingleton<StartupRegistrationService>();
        services.AddSingleton<DiagnosticsService>();
        services.AddSingleton<BackendConfigWriter>();
        services.AddSingleton<BackendHealthChecker>();
        services.AddSingleton<BackendProcessManager>();
        services.AddSingleton<CodexLocator>();
        services.AddSingleton<CodexVersionReader>();
        services.AddSingleton<CodexAuthStateReader>();
        services.AddSingleton<CodexConfigService>();
        services.AddSingleton<CodexLaunchService>();

        return services;
    }
}
