namespace CPAD.Core.Abstractions.Management;

public interface IUnsavedChangesGuard
{
    bool HasUnsavedChanges { get; }

    string? DirtyScope { get; }

    void SetDirty(string scope, bool hasUnsavedChanges);

    void Clear();

    bool ConfirmLeave(string targetDescription);
}
