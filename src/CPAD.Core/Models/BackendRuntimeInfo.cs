namespace CPAD.Core.Models;

public sealed class BackendRuntimeInfo
{
    public required int RequestedPort { get; init; }

    public required int Port { get; init; }

    public required bool PortWasAdjusted { get; init; }

    public string? PortMessage { get; init; }

    public required string ManagementKey { get; init; }

    public required string ConfigPath { get; init; }

    public required string BaseUrl { get; init; }

    public required string HealthUrl { get; init; }

    public required string ManagementApiBaseUrl { get; init; }

    public required string ManagementPageUrl { get; init; }
}
