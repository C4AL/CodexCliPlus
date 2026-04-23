using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CPAD.Management.DesignSystem.Controls;

public abstract class EditableItemBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class EditableKeyValueItem : EditableItemBase
{
    private string _key = string.Empty;
    private string _value = string.Empty;

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

public sealed class EditableStringItem : EditableItemBase
{
    private string _value = string.Empty;

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

public sealed class EditableModelAliasItem : EditableItemBase
{
    private string _name = string.Empty;
    private string _alias = string.Empty;
    private string _priority = string.Empty;
    private string _testModel = string.Empty;
    private bool _fork;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Alias
    {
        get => _alias;
        set => SetProperty(ref _alias, value);
    }

    public string Priority
    {
        get => _priority;
        set => SetProperty(ref _priority, value);
    }

    public string TestModel
    {
        get => _testModel;
        set => SetProperty(ref _testModel, value);
    }

    public bool Fork
    {
        get => _fork;
        set => SetProperty(ref _fork, value);
    }
}
