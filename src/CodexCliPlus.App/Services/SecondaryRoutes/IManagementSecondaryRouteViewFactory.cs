namespace CodexCliPlus.Services.SecondaryRoutes;

public interface IManagementSecondaryRouteViewFactory
{
    bool HasUnsavedChanges { get; }

    void DiscardPendingChanges();

    ManagementSecondaryRouteDescriptor Create(string? routeKey);
}
