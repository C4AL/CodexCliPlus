using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using CPAD.Core.Abstractions.Configuration;
using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Abstractions.Security;
using CPAD.Core.Constants;
using CPAD.Core.Models;
using CPAD.Infrastructure.Security;

namespace CPAD.Infrastructure.Configuration;

public sealed class JsonAppConfigurationService : IAppConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IPathService _pathService;
    private readonly ISecureCredentialStore _credentialStore;

    public JsonAppConfigurationService(IPathService pathService)
        : this(pathService, new DpapiCredentialStore(pathService))
    {
    }

    public JsonAppConfigurationService(
        IPathService pathService,
        ISecureCredentialStore credentialStore)
    {
        _pathService = pathService;
        _credentialStore = credentialStore;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _pathService.EnsureCreatedAsync(cancellationToken);

        if (!File.Exists(_pathService.Directories.SettingsFilePath))
        {
            return new AppSettings();
        }

        var json = await File.ReadAllTextAsync(_pathService.Directories.SettingsFilePath, cancellationToken);
        var persisted = JsonSerializer.Deserialize<PersistedAppSettings>(json, JsonOptions)
            ?? new PersistedAppSettings();

        var settings = persisted.ToModel();
        if (!string.IsNullOrWhiteSpace(persisted.ManagementKey))
        {
            settings.ManagementKey = persisted.ManagementKey.Trim();
            if (string.IsNullOrWhiteSpace(settings.ManagementKeyReference))
            {
                settings.ManagementKeyReference = AppConstants.DefaultManagementKeyReference;
            }

            await _credentialStore.SaveSecretAsync(settings.ManagementKeyReference, settings.ManagementKey, cancellationToken);
            await SaveAsync(settings, cancellationToken);
            return settings;
        }

        settings.ManagementKey = string.IsNullOrWhiteSpace(settings.ManagementKeyReference)
            ? string.Empty
            : await _credentialStore.LoadSecretAsync(settings.ManagementKeyReference, cancellationToken) ?? string.Empty;

        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _pathService.EnsureCreatedAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.ManagementKey))
        {
            settings.ManagementKeyReference = string.IsNullOrWhiteSpace(settings.ManagementKeyReference)
                ? AppConstants.DefaultManagementKeyReference
                : settings.ManagementKeyReference.Trim();

            await _credentialStore.SaveSecretAsync(settings.ManagementKeyReference, settings.ManagementKey, cancellationToken);
        }

        var persisted = PersistedAppSettings.FromModel(settings);
        var json = JsonSerializer.Serialize(persisted, JsonOptions);
        await File.WriteAllTextAsync(
            _pathService.Directories.SettingsFilePath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    private sealed class PersistedAppSettings
    {
        public int BackendPort { get; init; }

        public string? ManagementKey { get; init; }

        public string? ManagementKeyReference { get; init; }

        public Core.Enums.CodexSourceKind PreferredCodexSource { get; init; } = Core.Enums.CodexSourceKind.Official;

        public bool StartWithWindows { get; init; }

        public bool MinimizeToTrayOnClose { get; init; } = true;

        public bool EnableTrayIcon { get; init; } = true;

        public bool CheckForUpdatesOnStartup { get; init; } = true;

        public bool UseBetaChannel { get; init; }

        public Core.Enums.AppThemeMode ThemeMode { get; init; } = Core.Enums.AppThemeMode.System;

        public Core.Enums.AppLogLevel MinimumLogLevel { get; init; } = Core.Enums.AppLogLevel.Information;

        public bool EnableDebugTools { get; init; }

        public string? LastRepositoryPath { get; init; }

        public AppSettings ToModel()
        {
            return new AppSettings
            {
                BackendPort = BackendPort <= 0 ? AppConstants.DefaultBackendPort : BackendPort,
                ManagementKeyReference = string.IsNullOrWhiteSpace(ManagementKeyReference)
                    ? AppConstants.DefaultManagementKeyReference
                    : ManagementKeyReference.Trim(),
                PreferredCodexSource = PreferredCodexSource,
                StartWithWindows = StartWithWindows,
                MinimizeToTrayOnClose = MinimizeToTrayOnClose,
                EnableTrayIcon = EnableTrayIcon,
                CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
                UseBetaChannel = UseBetaChannel,
                ThemeMode = ThemeMode,
                MinimumLogLevel = MinimumLogLevel,
                EnableDebugTools = EnableDebugTools,
                LastRepositoryPath = LastRepositoryPath,
                ManagementKey = string.Empty
            };
        }

        public static PersistedAppSettings FromModel(AppSettings settings)
        {
            return new PersistedAppSettings
            {
                BackendPort = settings.BackendPort,
                ManagementKeyReference = settings.ManagementKeyReference,
                PreferredCodexSource = settings.PreferredCodexSource,
                StartWithWindows = settings.StartWithWindows,
                MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose,
                EnableTrayIcon = settings.EnableTrayIcon,
                CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup,
                UseBetaChannel = settings.UseBetaChannel,
                ThemeMode = settings.ThemeMode,
                MinimumLogLevel = settings.MinimumLogLevel,
                EnableDebugTools = settings.EnableDebugTools,
                LastRepositoryPath = settings.LastRepositoryPath
            };
        }
    }
}
