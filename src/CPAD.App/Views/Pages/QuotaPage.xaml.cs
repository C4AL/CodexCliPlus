using System.Windows;
using System.Windows.Controls;

using CPAD.ViewModels.Pages;

namespace CPAD.Views.Pages;

public partial class QuotaPage : Page
{
    private readonly QuotaPageViewModel _viewModel;

    public QuotaPage(QuotaPageViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAndRenderAsync();
    }

    private async Task RefreshAndRenderAsync()
    {
        await _viewModel.RefreshAsync();
        Render();
    }

    private void Render()
    {
        StatusBadge.Text = _viewModel.Status;
        StatusBadge.Tone = ManagementPageSupport.GetTone(_viewModel.Error);
        ErrorTextBlock.Text = _viewModel.Error;
        ErrorTextBlock.Visibility = string.IsNullOrWhiteSpace(_viewModel.Error) ? Visibility.Collapsed : Visibility.Visible;

        var quota = _viewModel.Config?.QuotaExceeded;
        QuotaItems.ItemsSource = new[]
        {
            new ManagementKeyValueItem("切换项目", ManagementPageSupport.FormatBoolean(quota?.SwitchProject)),
            new ManagementKeyValueItem("切换预览模型", ManagementPageSupport.FormatBoolean(quota?.SwitchPreviewModel)),
            new ManagementKeyValueItem("Antigravity Credits", ManagementPageSupport.FormatBoolean(quota?.AntigravityCredits))
        };
    }

    private async void ToggleSwitchProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var enabled = _viewModel.Config?.QuotaExceeded?.SwitchProject == true;
        await _viewModel.SetSwitchProjectAsync(!enabled);
        Render();
    }

    private async void ToggleSwitchPreviewModelButton_Click(object sender, RoutedEventArgs e)
    {
        var enabled = _viewModel.Config?.QuotaExceeded?.SwitchPreviewModel == true;
        await _viewModel.SetSwitchPreviewModelAsync(!enabled);
        Render();
    }
}
