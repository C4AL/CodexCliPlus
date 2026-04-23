using CPAD.Core.Abstractions.Management;
using CPAD.Management.DesignSystem.Controls;

namespace CPAD.Services.SecondaryRoutes;

public sealed class ManagementSecondaryRouteHost : IDisposable
{
    private readonly SecondaryPageShell _shell;
    private readonly IManagementNavigationService _navigationService;
    private readonly IUnsavedChangesGuard _unsavedChangesGuard;
    private readonly IManagementSecondaryRouteViewFactory _viewFactory;
    private readonly string _primaryRouteKey;

    public ManagementSecondaryRouteHost(
        SecondaryPageShell shell,
        string primaryRouteKey,
        IManagementNavigationService navigationService,
        IUnsavedChangesGuard unsavedChangesGuard,
        IManagementSecondaryRouteViewFactory viewFactory)
    {
        _shell = shell;
        _primaryRouteKey = primaryRouteKey;
        _navigationService = navigationService;
        _unsavedChangesGuard = unsavedChangesGuard;
        _viewFactory = viewFactory;

        _shell.BackRequested += Shell_BackRequested;
        _navigationService.RouteChanged += NavigationService_RouteChanged;
        Render();
    }

    public void Dispose()
    {
        _shell.BackRequested -= Shell_BackRequested;
        _navigationService.RouteChanged -= NavigationService_RouteChanged;
    }

    public void Refresh()
    {
        Render();
    }

    public bool TryNavigate(string routeKey, string targetDescription)
    {
        if (!ConfirmNavigation(targetDescription))
        {
            return false;
        }

        return _navigationService.TryNavigate(routeKey);
    }

    public bool GoBack(string targetDescription)
    {
        if (!_navigationService.CanGoBack)
        {
            return false;
        }

        if (!ConfirmNavigation(targetDescription))
        {
            return false;
        }

        return _navigationService.GoBack();
    }

    private bool ConfirmNavigation(string targetDescription)
    {
        if (!_viewFactory.HasUnsavedChanges)
        {
            return true;
        }

        if (!_unsavedChangesGuard.ConfirmLeave(targetDescription))
        {
            return false;
        }

        _viewFactory.DiscardPendingChanges();
        return true;
    }

    private void Shell_BackRequested(object sender, System.Windows.RoutedEventArgs e)
    {
        GoBack("返回主列表");
    }

    private void NavigationService_RouteChanged(object? sender, EventArgs e)
    {
        Render();
    }

    private void Render()
    {
        var secondaryRouteKey = _navigationService.CurrentRoute?.ParentKey == _primaryRouteKey
            ? _navigationService.CurrentSecondaryRouteKey
            : null;
        var descriptor = _viewFactory.Create(secondaryRouteKey);

        _shell.Title = descriptor.Title;
        _shell.Subtitle = descriptor.Subtitle;
        _shell.HeaderActions = descriptor.HeaderActions;
        _shell.BodyContent = descriptor.BodyContent;
        _shell.FooterContent = descriptor.FooterContent;
        _shell.BackLabel = descriptor.BackLabel;
        _shell.CanGoBack = !string.IsNullOrWhiteSpace(secondaryRouteKey);
    }
}
