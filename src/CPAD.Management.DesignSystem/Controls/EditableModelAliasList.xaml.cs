using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace CPAD.Management.DesignSystem.Controls;

public partial class EditableModelAliasList : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IList), typeof(EditableModelAliasList), new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty AddButtonTextProperty =
        DependencyProperty.Register(nameof(AddButtonText), typeof(string), typeof(EditableModelAliasList), new PropertyMetadata("添加别名"));

    public static readonly DependencyProperty ShowPriorityProperty =
        DependencyProperty.Register(nameof(ShowPriority), typeof(bool), typeof(EditableModelAliasList), new PropertyMetadata(false));

    public static readonly DependencyProperty ShowTestModelProperty =
        DependencyProperty.Register(nameof(ShowTestModel), typeof(bool), typeof(EditableModelAliasList), new PropertyMetadata(false));

    public static readonly DependencyProperty ShowForkProperty =
        DependencyProperty.Register(nameof(ShowFork), typeof(bool), typeof(EditableModelAliasList), new PropertyMetadata(false));

    private INotifyCollectionChanged? _collectionNotifier;

    public EditableModelAliasList()
    {
        InitializeComponent();
    }

    public event EventHandler? Changed;

    public IList? ItemsSource
    {
        get => (IList?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string AddButtonText
    {
        get => (string)GetValue(AddButtonTextProperty);
        set => SetValue(AddButtonTextProperty, value);
    }

    public bool ShowPriority
    {
        get => (bool)GetValue(ShowPriorityProperty);
        set => SetValue(ShowPriorityProperty, value);
    }

    public bool ShowTestModel
    {
        get => (bool)GetValue(ShowTestModelProperty);
        set => SetValue(ShowTestModelProperty, value);
    }

    public bool ShowFork
    {
        get => (bool)GetValue(ShowForkProperty);
        set => SetValue(ShowForkProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((EditableModelAliasList)d).HandleItemsSourceChanged((IList?)e.OldValue, (IList?)e.NewValue);
    }

    private void HandleItemsSourceChanged(IList? oldValue, IList? newValue)
    {
        Unsubscribe(oldValue);
        Subscribe(newValue);
    }

    private void Subscribe(IList? items)
    {
        if (items is INotifyCollectionChanged collectionNotifier)
        {
            _collectionNotifier = collectionNotifier;
            _collectionNotifier.CollectionChanged += CollectionChanged;
        }

        if (items is null)
        {
            return;
        }

        foreach (var item in items.OfType<EditableItemBase>())
        {
            item.PropertyChanged += ItemPropertyChanged;
        }
    }

    private void Unsubscribe(IList? items)
    {
        if (_collectionNotifier is not null)
        {
            _collectionNotifier.CollectionChanged -= CollectionChanged;
            _collectionNotifier = null;
        }

        if (items is null)
        {
            return;
        }

        foreach (var item in items.OfType<EditableItemBase>())
        {
            item.PropertyChanged -= ItemPropertyChanged;
        }
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.OldItems?.OfType<EditableItemBase>() ?? [])
        {
            item.PropertyChanged -= ItemPropertyChanged;
        }

        foreach (var item in e.NewItems?.OfType<EditableItemBase>() ?? [])
        {
            item.PropertyChanged += ItemPropertyChanged;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        ItemsSource?.Add(new EditableModelAliasItem());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EditableModelAliasItem item })
        {
            ItemsSource?.Remove(item);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
