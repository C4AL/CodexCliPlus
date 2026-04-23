using System.Windows;

namespace CPAD.Services.SecondaryRoutes;

public sealed record ManagementSecondaryRouteDescriptor(
    string Title,
    string Subtitle,
    UIElement BodyContent,
    UIElement? HeaderActions = null,
    UIElement? FooterContent = null,
    string BackLabel = "返回列表");
