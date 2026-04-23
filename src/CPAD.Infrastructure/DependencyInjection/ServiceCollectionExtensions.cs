using CPAD.Core.Abstractions.Configuration;
using CPAD.Core.Abstractions.Logging;
using CPAD.Core.Abstractions.Management;
using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Abstractions.Processes;
using CPAD.Core.Abstractions.Security;
using CPAD.Core.Abstractions.Updates;
using CPAD.Infrastructure.Backend;
using CPAD.Infrastructure.Codex;
using CPAD.Infrastructure.Configuration;
using CPAD.Infrastructure.Dependencies;
using CPAD.Infrastructure.Diagnostics;
using CPAD.Infrastructure.Logging;
using CPAD.Infrastructure.Management;
using CPAD.Infrastructure.Paths;
using CPAD.Infrastructure.Platform;
using CPAD.Infrastructure.Processes;
using CPAD.Infrastructure.Security;
using CPAD.Infrastructure.Updates;

using Microsoft.Extensions.DependencyInjection;

namespace CPAD.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCpadInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddHttpClient<BackendAssetService>();
        services.AddSingleton<IPathService, AppPathService>();
        services.AddSingleton<ISecureCredentialStore, DpapiCredentialStore>();
        services.AddSingleton<IAppLogger, FileAppLogger>();
        services.AddSingleton<IAppConfigurationService, JsonAppConfigurationService>();
        services.AddSingleton<IProcessService, SystemProcessService>();
        services.AddSingleton<DirectoryAccessService>();
        services.AddSingleton<DependencyHealthService>();
        services.AddSingleton<StartupRegistrationService>();
        services.AddSingleton<DiagnosticsService>();
        services.AddSingleton<BackendConfigWriter>();
        services.AddSingleton<BackendHealthChecker>();
        services.AddSingleton<BackendProcessManager>();
        services.AddSingleton<IManagementConnectionProvider, BackendManagementConnectionProvider>();
        services.AddSingleton<IManagementApiClient, ManagementApiClient>();
        services.AddSingleton<IManagementOverviewService, ManagementOverviewService>();
        services.AddSingleton<IManagementConfigurationService, ManagementConfigurationService>();
        services.AddSingleton<IManagementAuthService, ManagementAuthService>();
        services.AddSingleton<IManagementUsageService, ManagementUsageService>();
        services.AddSingleton<IManagementLogsService, ManagementLogsService>();
        services.AddSingleton<IManagementSystemService, ManagementSystemService>();
        services.AddSingleton<IUpdateCheckService, GitHubReleaseUpdateService>();
        services.AddSingleton<IUpdateInstallerService, UpdateInstallerService>();
        services.AddSingleton<CodexLocator>();
        services.AddSingleton<CodexVersionReader>();
        services.AddSingleton<CodexAuthStateReader>();
        services.AddSingleton<CodexConfigService>();
        services.AddSingleton<CodexLaunchService>();

        return services;
    }
}
