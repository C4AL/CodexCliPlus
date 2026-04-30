using CodexCliPlus.Core.Abstractions.Configuration;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Persistence;
using CodexCliPlus.Core.Abstractions.Processes;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Abstractions.Updates;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Infrastructure.Codex;
using CodexCliPlus.Infrastructure.Configuration;
using CodexCliPlus.Infrastructure.Dependencies;
using CodexCliPlus.Infrastructure.Diagnostics;
using CodexCliPlus.Infrastructure.Logging;
using CodexCliPlus.Infrastructure.Management;
using CodexCliPlus.Infrastructure.Paths;
using CodexCliPlus.Infrastructure.Persistence;
using CodexCliPlus.Infrastructure.Platform;
using CodexCliPlus.Infrastructure.Processes;
using CodexCliPlus.Infrastructure.Security;
using CodexCliPlus.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace CodexCliPlus.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCpadInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddHttpClient<BackendAssetService>();
        services.AddSingleton<IPathService, AppPathService>();
        services.AddSingleton<ISecureCredentialStore, DpapiCredentialStore>();
        services.AddSingleton<ISecretVault, DpapiSecretVault>();
        services.AddSingleton<SensitiveConfigMigrationService>();
        services.AddSingleton<SecretBrokerService>();
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
        services.AddSingleton<IManagementSessionService, ManagementSessionService>();
        services.AddSingleton<IManagementApiClient, ManagementApiClient>();
        services.AddSingleton<ManagementAuthService>();
        services.AddSingleton<IManagementOverviewService, ManagementOverviewService>();
        services.AddSingleton<IManagementConfigurationService, ManagementConfigurationService>();
        services.AddSingleton<IManagementProvidersService>(provider =>
            provider.GetRequiredService<ManagementAuthService>()
        );
        services.AddSingleton<IManagementAuthFilesService>(provider =>
            provider.GetRequiredService<ManagementAuthService>()
        );
        services.AddSingleton<IManagementOAuthService>(provider =>
            provider.GetRequiredService<ManagementAuthService>()
        );
        services.AddSingleton<IManagementAuthService>(provider =>
            provider.GetRequiredService<ManagementAuthService>()
        );
        services.AddSingleton<IManagementQuotaService, ManagementQuotaService>();
        services.AddSingleton<IManagementUsageService, ManagementUsageService>();
        services.AddSingleton<IManagementLogsService, ManagementLogsService>();
        services.AddSingleton<IManagementSystemService, ManagementSystemService>();
        services.AddSingleton<IManagementPersistenceService, ManagementPersistenceService>();
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
