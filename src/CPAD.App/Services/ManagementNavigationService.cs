using CPAD.Core.Abstractions.Management;
using CPAD.Core.Models.Management;

namespace CPAD.Services;

public sealed class ManagementNavigationService : IManagementNavigationService
{
    private readonly Dictionary<string, ManagementRouteDefinition> _routes;
    private ManagementRouteDefinition? _selectedPrimaryRoute;
    private ManagementRouteDefinition? _selectedSecondaryRoute;

    public ManagementNavigationService()
    {
        Routes = ManagementRouteCatalog.All;
        PrimaryRoutes = ManagementRouteCatalog.Primary;
        _routes = Routes.ToDictionary(route => route.Key, StringComparer.OrdinalIgnoreCase);
        _selectedPrimaryRoute = PrimaryRoutes.Count > 0 ? PrimaryRoutes[0] : null;
    }

    public IReadOnlyList<ManagementRouteDefinition> Routes { get; }

    public IReadOnlyList<ManagementRouteDefinition> PrimaryRoutes { get; }

    public ManagementRouteDefinition? CurrentRoute => _selectedSecondaryRoute ?? _selectedPrimaryRoute;

    public string? SelectedPrimaryRouteKey => _selectedPrimaryRoute?.Key;

    public string? CurrentSecondaryRouteKey => _selectedSecondaryRoute?.Key;

    public bool CanGoBack => _selectedSecondaryRoute is not null;

    public event EventHandler? RouteChanged;

    public bool TryNavigate(string routeKey)
    {
        if (!_routes.TryGetValue(routeKey, out var route))
        {
            return false;
        }

        if (route.IsPrimary)
        {
            _selectedPrimaryRoute = route;
            _selectedSecondaryRoute = null;
            RouteChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (!string.Equals(route.ParentKey, _selectedPrimaryRoute?.Key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (string.Equals(_selectedSecondaryRoute?.Key, route.Key, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        _selectedSecondaryRoute = route;
        RouteChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool GoBack()
    {
        if (_selectedSecondaryRoute is null)
        {
            return false;
        }

        _selectedSecondaryRoute = null;
        RouteChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
