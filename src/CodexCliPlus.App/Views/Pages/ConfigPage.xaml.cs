using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

using CodexCliPlus.Management.DesignSystem.Controls;
using CodexCliPlus.ViewModels.Pages;
using MessageBox = System.Windows.MessageBox;

namespace CodexCliPlus.Views.Pages;

public partial class ConfigPage : Page
{
    private readonly ConfigPageViewModel _viewModel;
    private bool _hasLoaded;

    public ConfigPage(ConfigPageViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += ConfigPage_Loaded;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.State.PropertyChanged += State_PropertyChanged;
    }

    private async void ConfigPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            Render();
            return;
        }

        _hasLoaded = true;
        await _viewModel.RefreshAsync();
        Render();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConfigPageViewModel.Status) or nameof(ConfigPageViewModel.Error) or nameof(ConfigPageViewModel.IsBusy))
        {
            Render();
        }
    }

    private void State_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConfigPageState.HasAnyChanges) or
            nameof(ConfigPageState.CanSaveFields) or
            nameof(ConfigPageState.CanSaveYaml))
        {
            Render();
        }
    }

    private void Render()
    {
        StatusBadge.Text = _viewModel.Status;
        StatusBadge.Tone = ManagementPageSupport.GetTone(_viewModel.Error);
        ErrorTextBlock.Text = _viewModel.Error;
        ErrorTextBlock.Visibility = string.IsNullOrWhiteSpace(_viewModel.Error) ? Visibility.Collapsed : Visibility.Visible;

        YamlEditor.IsReadOnly = _viewModel.IsBusy;
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        if (_viewModel.State.HasAnyChanges && !ConfirmDiscard("重新加载会覆盖当前字段和高级 YAML 草稿，是否继续？"))
        {
            return;
        }

        await _viewModel.RefreshAsync();
        Render();
    }

    private void DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        if (!_viewModel.State.HasAnyChanges)
        {
            return;
        }

        if (!ConfirmDiscard("放弃当前本地修改后将恢复到最近一次服务端快照，是否继续？"))
        {
            return;
        }

        _viewModel.State.RestoreFromServer();
        Render();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        await _viewModel.SaveAsync();
        Render();
    }

    private void ReloadAdvancedYamlButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        if (_viewModel.State.HasYamlChanges &&
            !ConfirmDiscard("重置高级 YAML 草稿后，当前 YAML 修改将被放弃，是否继续？"))
        {
            return;
        }

        _viewModel.State.AdvancedYamlDraft = _viewModel.State.ServerYaml;
        Render();
    }

    private async void ApplyAdvancedYamlButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        if (!_viewModel.State.HasYamlChanges)
        {
            return;
        }

        var changedLines = ManagementPageSupport.CountChangedLines(_viewModel.ServerYaml, _viewModel.State.AdvancedYamlDraft);
        var summary = $"即将写回 config.yaml，并覆盖当前服务端配置。\n变更行数：{changedLines}";
        var confirmed = DiffDialog.Confirm(
            Window.GetWindow(this),
            _viewModel.ServerYaml,
            _viewModel.State.AdvancedYamlDraft,
            summary);

        if (!confirmed)
        {
            return;
        }

        await _viewModel.SaveAdvancedYamlAsync();
        Render();
    }

    private static bool ConfirmDiscard(string message)
    {
        return MessageBox.Show(
            message,
            "配置",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning) == MessageBoxResult.OK;
    }
}
