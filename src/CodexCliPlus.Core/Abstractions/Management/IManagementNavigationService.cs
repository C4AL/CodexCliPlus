using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.Core.Abstractions.Management;

public interface IManagementNavigationService
{
    IReadOnlyList<ManagementRouteDefinition> Routes { get; }

    IReadOnlyList<ManagementRouteDefinition> PrimaryRoutes { get; }

    ManagementRouteDefinition? CurrentRoute { get; }

    string? SelectedPrimaryRouteKey { get; }

    string? CurrentSecondaryRouteKey { get; }

    bool CanGoBack { get; }

    event EventHandler? RouteChanged;

    bool TryNavigate(string routeKey);

    bool GoBack();
}
