using CPAD.Core.Abstractions.Management;
using CPAD.Core.Models.Management;

namespace CPAD.Services.SecondaryRoutes;

public sealed class AuthFilesRouteState
{
    private const string ScopeName = "Auth Files";
    private readonly IUnsavedChangesGuard _unsavedChangesGuard;

    public AuthFilesRouteState(IUnsavedChangesGuard unsavedChangesGuard)
    {
        _unsavedChangesGuard = unsavedChangesGuard;
    }

    public string? SelectedFileName { get; private set; }

    public string? SelectedRouteKey { get; private set; }

    public IReadOnlyList<ManagementModelDescriptor> SelectedModels { get; private set; } = [];

    public string? SelectedPrefix { get; private set; }

    public string? SelectedProxyUrl { get; private set; }

    public IReadOnlyDictionary<string, string> SelectedHeaders { get; private set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool IsDirty { get; private set; }

    public void SetSelectedFile(string? fileName)
    {
        SelectedFileName = fileName;
    }

    public void SetSelectedRoute(string? routeKey)
    {
        SelectedRouteKey = routeKey;
    }

    public void SetSelectedModels(IReadOnlyList<ManagementModelDescriptor> models)
    {
        SelectedModels = models;
    }

    public void SetDraftFields(string? prefix, string? proxyUrl, IReadOnlyDictionary<string, string> headers)
    {
        SelectedPrefix = prefix;
        SelectedProxyUrl = proxyUrl;
        SelectedHeaders = headers;
    }

    public void MarkDirty()
    {
        if (IsDirty)
        {
            return;
        }

        IsDirty = true;
        _unsavedChangesGuard.SetDirty(ScopeName, true);
    }

    public void MarkClean()
    {
        if (!IsDirty)
        {
            return;
        }

        IsDirty = false;
        _unsavedChangesGuard.SetDirty(ScopeName, false);
    }
}
