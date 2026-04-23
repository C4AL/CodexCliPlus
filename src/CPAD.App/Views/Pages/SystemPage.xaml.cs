using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

using CPAD.ViewModels.Pages;

namespace CPAD.Views.Pages;

public partial class SystemPage : Page
{
    private readonly SystemPageViewModel _viewModel;

    public SystemPage(SystemPageViewModel viewModel)
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

        ConnectionItems.ItemsSource = new[]
        {
            new ManagementKeyValueItem("基础地址", ManagementPageSupport.FormatValue(_viewModel.Connection?.BaseUrl, "未知")),
            new ManagementKeyValueItem("管理地址", ManagementPageSupport.FormatValue(_viewModel.Connection?.ManagementApiBaseUrl, "未知")),
            new ManagementKeyValueItem("管理密钥", string.IsNullOrWhiteSpace(_viewModel.Connection?.ManagementKey) ? "未配置" : "已配置"),
            new ManagementKeyValueItem("最新版本", ManagementPageSupport.FormatValue(_viewModel.LatestVersion?.LatestVersion, "未知")),
            new ManagementKeyValueItem("请求日志", ManagementPageSupport.FormatBoolean(_viewModel.Config?.RequestLog))
        };

        ModelItems.ItemsSource = _viewModel.Models.Select(model =>
            $"{model.Id} · {ManagementPageSupport.FormatValue(model.DisplayName, "未命名")} · {ManagementPageSupport.FormatValue(model.OwnedBy, "未知来源")}")
            .ToArray();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAndRenderAsync();
    }

    private async void ToggleRequestLogButton_Click(object sender, RoutedEventArgs e)
    {
        var enabled = _viewModel.Config?.RequestLog == true;
        await _viewModel.SetRequestLogAsync(!enabled);
        Render();
    }

    private static void OpenLink(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void OpenCliProxyApiButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLink("https://github.com/router-for-me/CLIProxyAPI");
    }

    private void OpenManagementCenterButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLink("https://github.com/router-for-me/Cli-Proxy-API-Management-Center");
    }

    private void OpenBetterGiButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLink("https://github.com/babalae/better-genshin-impact");
    }
}
