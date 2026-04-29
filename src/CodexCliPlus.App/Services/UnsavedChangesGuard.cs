using System.Windows;
using CodexCliPlus.Core.Abstractions.Management;
using MessageBox = System.Windows.MessageBox;

namespace CodexCliPlus.Services;

public sealed class UnsavedChangesGuard : IUnsavedChangesGuard
{
    private readonly HashSet<string> _dirtyScopes = new(StringComparer.OrdinalIgnoreCase);

    public bool HasUnsavedChanges => _dirtyScopes.Count > 0;

    public string? DirtyScope => _dirtyScopes.FirstOrDefault();

    public void SetDirty(string scope, bool hasUnsavedChanges)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return;
        }

        if (hasUnsavedChanges)
        {
            _dirtyScopes.Add(scope);
        }
        else
        {
            _dirtyScopes.Remove(scope);
        }
    }

    public void Clear()
    {
        _dirtyScopes.Clear();
    }

    public bool ConfirmLeave(string targetDescription)
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        var result = MessageBox.Show(
            $"当前有未保存的更改，继续前往“{targetDescription}”会丢失这些修改。\n\n是否继续？",
            "未保存更改",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning
        );

        if (result == MessageBoxResult.OK)
        {
            _dirtyScopes.Clear();
            return true;
        }

        return false;
    }
}
