using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

using CPAD.Core.Models.Management;
using CPAD.ViewModels.Pages;

using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace CPAD.Views.Pages;

public partial class UsagePage : Page
{
    private readonly UsagePageViewModel _viewModel;

    public UsagePage(UsagePageViewModel viewModel)
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

        var snapshot = _viewModel.Snapshot;
        if (snapshot is null)
        {
            MetricItems.ItemsSource = Array.Empty<ManagementMetricItem>();
            ProviderItems.ItemsSource = Array.Empty<UsageApiSummaryItem>();
            return;
        }

        MetricItems.ItemsSource = new[]
        {
            new ManagementMetricItem(snapshot.TotalRequests.ToString(), "总请求数", "全部请求累计"),
            new ManagementMetricItem(snapshot.SuccessCount.ToString(), "成功数", "成功返回的请求"),
            new ManagementMetricItem(snapshot.FailureCount.ToString(), "失败数", "失败请求累计"),
            new ManagementMetricItem(snapshot.TotalTokens.ToString(), "总 Token", "输入、输出、缓存与思考总量")
        };

        RequestsTrendChart.Points = snapshot.RequestsByDay;
        TokensTrendChart.Points = snapshot.TokensByDay;
        ProviderItems.ItemsSource = snapshot.Apis.Select(pair => new UsageApiSummaryItem(
            pair.Key,
            pair.Value.TotalRequests,
            pair.Value.TotalTokens,
            pair.Value.Models.Count)).ToArray();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAndRenderAsync();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var payload = await _viewModel.ExportAsync();
        var dialog = new SaveFileDialog
        {
            Filter = "JSON 文件|*.json",
            FileName = $"usage-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(payload.Value, ManagementPageSupport.JsonOptions));
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON 文件|*.json"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        var payload = JsonSerializer.Deserialize<ManagementUsageExportPayload>(
            File.ReadAllText(dialog.FileName),
            ManagementPageSupport.JsonOptions);
        if (payload is null)
        {
            ManagementPageSupport.ShowInfo(this, "用量", "无法解析导入文件。");
            return;
        }

        await _viewModel.ImportAsync(payload);
        Render();
    }
}
