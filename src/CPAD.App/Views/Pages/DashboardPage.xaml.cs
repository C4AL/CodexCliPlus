using System.Globalization;
using System.Windows;
using System.Windows.Controls;

using CPAD.ViewModels.Pages;

namespace CPAD.Views.Pages;

public partial class DashboardPage : Page
{
    private readonly DashboardPageViewModel _viewModel;

    public DashboardPage(DashboardPageViewModel viewModel)
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
            ConnectionItems.ItemsSource = Array.Empty<ManagementKeyValueItem>();
            ProviderItems.ItemsSource = Array.Empty<ManagementKeyValueItem>();
            return;
        }

        MetricItems.ItemsSource = new[]
        {
            new ManagementMetricItem(snapshot.ApiKeyCount.ToString(CultureInfo.CurrentCulture), "管理密钥", "当前后端配置中的 API Keys 数量"),
            new ManagementMetricItem(snapshot.AuthFileCount.ToString(CultureInfo.CurrentCulture), "认证文件", "已经注册的本地认证凭据"),
            new ManagementMetricItem(snapshot.Usage.TotalRequests.ToString(CultureInfo.CurrentCulture), "总请求数", "来自 Usage 统计快照"),
            new ManagementMetricItem(snapshot.Usage.TotalTokens.ToString(CultureInfo.CurrentCulture), "总 Token", "累计输入、输出、缓存与思考 Token")
        };

        ConnectionItems.ItemsSource = new[]
        {
            new ManagementKeyValueItem("管理地址", snapshot.ManagementApiBaseUrl),
            new ManagementKeyValueItem("服务版本", ManagementPageSupport.FormatValue(snapshot.ServerVersion, "未知")),
            new ManagementKeyValueItem("最新版本", ManagementPageSupport.FormatValue(snapshot.LatestVersion ?? snapshot.LatestVersionError, "未知")),
            new ManagementKeyValueItem("调试模式", ManagementPageSupport.FormatBoolean(snapshot.Config.Debug)),
            new ManagementKeyValueItem("文件日志", ManagementPageSupport.FormatBoolean(snapshot.Config.LoggingToFile)),
            new ManagementKeyValueItem("路由策略", ManagementPageSupport.FormatValue(snapshot.Config.RoutingStrategy))
        };

        ProviderItems.ItemsSource = new[]
        {
            new ManagementKeyValueItem("Gemini", snapshot.GeminiKeyCount.ToString(CultureInfo.CurrentCulture)),
            new ManagementKeyValueItem("Codex", snapshot.CodexKeyCount.ToString(CultureInfo.CurrentCulture)),
            new ManagementKeyValueItem("Claude", snapshot.ClaudeKeyCount.ToString(CultureInfo.CurrentCulture)),
            new ManagementKeyValueItem("Vertex", snapshot.VertexKeyCount.ToString(CultureInfo.CurrentCulture)),
            new ManagementKeyValueItem("OpenAI 兼容", snapshot.OpenAiCompatibilityCount.ToString(CultureInfo.CurrentCulture)),
            new ManagementKeyValueItem("可用模型", snapshot.AvailableModelCount?.ToString(CultureInfo.CurrentCulture) ?? ManagementPageSupport.FormatValue(snapshot.AvailableModelsError, "未知"))
        };
    }
}
