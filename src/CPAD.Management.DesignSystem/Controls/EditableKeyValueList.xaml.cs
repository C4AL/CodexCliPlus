using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace CPAD.Management.DesignSystem.Controls;

public partial class EditableKeyValueList : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IList), typeof(EditableKeyValueList), new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty KeyHeaderProperty =
        DependencyProperty.Register(nameof(KeyHeader), typeof(string), typeof(EditableKeyValueList), new PropertyMetadata("键"));

    public static readonly DependencyProperty ValueHeaderProperty =
        DependencyProperty.Register(nameof(ValueHeader), typeof(string), typeof(EditableKeyValueList), new PropertyMetadata("值"));

    public static readonly DependencyProperty AddButtonTextProperty =
        DependencyProperty.Register(nameof(AddButtonText), typeof(string), typeof(EditableKeyValueList), new PropertyMetadata("添加一行"));

    private INotifyCollectionChanged? _collectionNotifier;

    public EditableKeyValueList()
    {
        InitializeComponent();
    }

    public event EventHandler? Changed;

    public IList? ItemsSource
    {
        get => (IList?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string KeyHeader
    {
        get => (string)GetValue(KeyHeaderProperty);
        set => SetValue(KeyHeaderProperty, value);
    }

    public string ValueHeader
    {
        get => (string)GetValue(ValueHeaderProperty);
        set => SetValue(ValueHeaderProperty, value);
    }

    public string AddButtonText
    {
        get => (string)GetValue(AddButtonTextProperty);
        set => SetValue(AddButtonTextProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((EditableKeyValueList)d).HandleItemsSourceChanged((IList?)e.OldValue, (IList?)e.NewValue);
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
        ItemsSource?.Add(new EditableKeyValueItem());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EditableKeyValueItem item })
        {
            ItemsSource?.Remove(item);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
