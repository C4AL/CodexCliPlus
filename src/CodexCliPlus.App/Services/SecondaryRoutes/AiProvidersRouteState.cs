using CodexCliPlus.Core.Abstractions.Management;

namespace CodexCliPlus.Services.SecondaryRoutes;

public sealed class AiProvidersRouteState
{
    private const string ScopeName = "AI Providers";
    private readonly IUnsavedChangesGuard _unsavedChangesGuard;
    private readonly Dictionary<string, int> _selectedIndices = new(StringComparer.OrdinalIgnoreCase);

    public AiProvidersRouteState(IUnsavedChangesGuard unsavedChangesGuard)
    {
        _unsavedChangesGuard = unsavedChangesGuard;
    }

    public string SelectedProviderKey { get; private set; } = "gemini";

    public string? SelectedRouteKey { get; private set; }

    public bool IsDirty { get; private set; }

    public int GetSelectedIndex(string providerKey, int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            return 0;
        }

        return _selectedIndices.TryGetValue(providerKey, out var index)
            ? Math.Clamp(index, 0, maxExclusive - 1)
            : 0;
    }

    public void SetRoute(string providerKey, string? routeKey, int selectedIndex)
    {
        SelectedProviderKey = providerKey;
        SelectedRouteKey = routeKey;
        _selectedIndices[providerKey] = Math.Max(selectedIndex, 0);
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
