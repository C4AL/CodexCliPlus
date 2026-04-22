namespace CPAD.Contracts;

public sealed record HostSnapshotDto
{
    public string InstallRoot { get; init; } = string.Empty;
    public ServiceStateDto? ServiceState { get; init; }
    public CpaRuntimeStatusDto CpaRuntime { get; init; } = new();
    public CodexShimResolutionDto Codex { get; init; } = new();
    public PluginMarketStatusDto PluginMarket { get; init; } = new();
    public UpdateCenterStatusDto UpdateCenter { get; init; } = new();
    public ManagerStatusDto ManagerStatus { get; init; } = new();
}

public sealed record ServiceStateDto
{
    public string ProductName { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string InstallRoot { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ManagerStatusDto
{
    public string ServiceName { get; init; } = string.Empty;
    public bool Installed { get; init; }
    public string State { get; init; } = string.Empty;
    public string StartType { get; init; } = string.Empty;
    public string BinaryPath { get; init; } = string.Empty;
}

public sealed record CodexShimResolutionDto
{
    public string Mode { get; init; } = "official";
    public string ModeFile { get; init; } = string.Empty;
    public string ShimPath { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public bool TargetExists { get; init; }
    public string GlobalPath { get; init; } = string.Empty;
    public bool GlobalExists { get; init; }
    public IReadOnlyList<string> LaunchArgs { get; init; } = Array.Empty<string>();
    public bool LaunchReady { get; init; }
    public string LaunchMessage { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record CpaRuntimeStatusDto
{
    public string SourceRoot { get; init; } = string.Empty;
    public bool SourceExists { get; init; }
    public string BuildPackage { get; init; } = string.Empty;
    public string ManagedBinary { get; init; } = string.Empty;
    public bool BinaryExists { get; init; }
    public string ConfigPath { get; init; } = string.Empty;
    public bool ConfigExists { get; init; }
    public string StateFile { get; init; } = string.Empty;
    public string LogPath { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public int Pid { get; init; }
    public bool Running { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
    public CpaRuntimeConfigInsightDto ConfigInsight { get; init; } = new();
    public CpaRuntimeHealthCheckDto HealthCheck { get; init; } = new();
}

public sealed record CpaRuntimeConfigInsightDto
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public bool TlsEnabled { get; init; }
    public string BaseUrl { get; init; } = string.Empty;
    public string HealthUrl { get; init; } = string.Empty;
    public string ManagementUrl { get; init; } = string.Empty;
    public string UsageUrl { get; init; } = string.Empty;
    public string CodexRemoteUrl { get; init; } = string.Empty;
    public bool ManagementAllowRemote { get; init; }
    public bool ManagementEnabled { get; init; }
    public bool ControlPanelEnabled { get; init; }
    public string PanelRepository { get; init; } = string.Empty;
    public bool CodexAppServerProxyEnabled { get; init; }
    public bool CodexAppServerRestrictToLocalhost { get; init; }
    public string CodexAppServerCodexBin { get; init; } = string.Empty;
}

public sealed record CpaRuntimeHealthCheckDto
{
    public bool Checked { get; init; }
    public bool Healthy { get; init; }
    public int StatusCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CheckedAt { get; init; }
}

public sealed record PluginMarketStatusDto
{
    public string SourceRoot { get; init; } = string.Empty;
    public bool SourceExists { get; init; }
    public string MarketplacePath { get; init; } = string.Empty;
    public bool MarketplaceExists { get; init; }
    public string CatalogSource { get; init; } = string.Empty;
    public string CatalogPath { get; init; } = string.Empty;
    public string StatePath { get; init; } = string.Empty;
    public string PluginsDir { get; init; } = string.Empty;
    public IReadOnlyList<PluginStatusDto> Plugins { get; init; } = Array.Empty<PluginStatusDto>();
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record PluginStatusDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SourceType { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public bool SourceExists { get; init; }
    public string ReadmePath { get; init; } = string.Empty;
    public bool ReadmeExists { get; init; }
    public string Category { get; init; } = string.Empty;
    public string RepositoryUrl { get; init; } = string.Empty;
    public string RepositoryRef { get; init; } = string.Empty;
    public string RepositorySubdir { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string InstallPath { get; init; } = string.Empty;
    public bool InstallExists { get; init; }
    public bool Installed { get; init; }
    public bool Enabled { get; init; }
    public string InstalledVersion { get; init; } = string.Empty;
    public bool NeedsUpdate { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record UpdateCenterStatusDto
{
    public string ProductName { get; init; } = string.Empty;
    public string StateFile { get; init; } = string.Empty;
    public IReadOnlyList<UpdateSourceStatusDto> Sources { get; init; } = Array.Empty<UpdateSourceStatusDto>();
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record UpdateSourceStatusDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string CurrentRef { get; init; } = string.Empty;
    public string LatestRef { get; init; } = string.Empty;
    public bool Dirty { get; init; }
    public bool Available { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
}
