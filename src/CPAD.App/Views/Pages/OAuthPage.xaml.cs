using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using CPAD.ViewModels.Pages;

using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace CPAD.Views.Pages;

public partial class OAuthPage : Page
{
    private readonly OAuthPageViewModel _viewModel;
    private readonly Dictionary<string, OAuthProviderState> _states = new(StringComparer.OrdinalIgnoreCase)
    {
        ["codex"] = new("Codex"),
        ["anthropic"] = new("Anthropic"),
        ["antigravity"] = new("Antigravity"),
        ["gemini-cli"] = new("Gemini CLI"),
        ["kimi"] = new("Kimi")
    };

    public OAuthPage(OAuthPageViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += (_, _) => Render();
        Unloaded += (_, _) =>
        {
            foreach (var state in _states.Values)
            {
                state.Timer?.Stop();
            }
        };
    }

    private void Render()
    {
        StatusBadge.Text = _viewModel.Status;
        StatusBadge.Tone = ManagementPageSupport.GetTone(_viewModel.Error);
        ErrorTextBlock.Text = _viewModel.Error;
        ErrorTextBlock.Visibility = string.IsNullOrWhiteSpace(_viewModel.Error) ? Visibility.Collapsed : Visibility.Visible;

        CardsHost.Children.Clear();
        foreach (var (provider, state) in _states)
        {
            CardsHost.Children.Add(BuildProviderCard(provider, state));
        }
    }

    private Border BuildProviderCard(string provider, OAuthProviderState state)
    {
        var card = new Border
        {
            Width = 360,
            Margin = new Thickness(0, 0, 18, 18),
            Style = (Style)Application.Current.Resources["ManagementCardBorderStyle"]
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = state.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["ManagementPrimaryTextBrush"]
        });
        panel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Text = provider == "gemini-cli"
                ? "Gemini CLI 允许携带项目 ID。其它提供商直接发起 OAuth 授权。"
                : "点击开始授权后，会打开官方页面并轮询状态。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["ManagementSecondaryTextBrush"]
        });

        if (provider == "gemini-cli")
        {
            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 14, 0, 6),
                Text = "项目 ID（选填）",
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["ManagementPrimaryTextBrush"]
            });

            var projectBox = new TextBox
            {
                Text = state.ProjectId
            };
            projectBox.TextChanged += (_, _) => state.ProjectId = projectBox.Text;
            panel.Children.Add(projectBox);
        }

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var startButton = new Button
        {
            Content = "开始授权"
        };
        startButton.Click += async (_, _) =>
        {
            try
            {
                var response = await _viewModel.StartAsync(provider, string.IsNullOrWhiteSpace(state.ProjectId) ? null : state.ProjectId);
                state.Url = response.Value.Url;
                state.StateToken = response.Value.State ?? string.Empty;
                state.StatusText = "已获取授权链接，等待完成回调。";
                StartPolling(state);
                Render();
            }
            catch (Exception exception)
            {
                state.StatusText = exception.Message;
                Render();
            }
        };
        actionRow.Children.Add(startButton);

        if (!string.IsNullOrWhiteSpace(state.Url))
        {
            var openButton = new Button
            {
                Content = "打开链接"
            };
            openButton.Click += (_, _) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = state.Url,
                    UseShellExecute = true
                });
            };
            actionRow.Children.Add(openButton);

            var copyButton = new Button
            {
                Content = "复制链接"
            };
            copyButton.Click += (_, _) => Clipboard.SetText(state.Url);
            actionRow.Children.Add(copyButton);
        }

        panel.Children.Add(actionRow);

        panel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 14, 0, 6),
            Text = "回调链接（可选）",
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["ManagementPrimaryTextBrush"]
        });

        var callbackBox = new TextBox
        {
            Text = state.CallbackUrl
        };
        callbackBox.TextChanged += (_, _) => state.CallbackUrl = callbackBox.Text;
        panel.Children.Add(callbackBox);

        var submitButton = new Button
        {
            Margin = new Thickness(0, 14, 0, 0),
            Content = "提交回调"
        };
        submitButton.Click += async (_, _) =>
        {
            try
            {
                await _viewModel.SubmitCallbackAsync(provider, state.CallbackUrl);
                state.StatusText = "回调已提交。";
                Render();
            }
            catch (Exception exception)
            {
                state.StatusText = exception.Message;
                Render();
            }
        };
        panel.Children.Add(submitButton);

        if (!string.IsNullOrWhiteSpace(state.StatusText))
        {
            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 14, 0, 0),
                Text = state.StatusText,
                TextWrapping = TextWrapping.Wrap,
                Foreground = state.StatusText.Contains("失败", StringComparison.OrdinalIgnoreCase)
                    ? (System.Windows.Media.Brush)Application.Current.Resources["ManagementDangerBrush"]
                    : (System.Windows.Media.Brush)Application.Current.Resources["ManagementSecondaryTextBrush"]
            });
        }

        card.Child = panel;
        return card;
    }

    private void StartPolling(OAuthProviderState state)
    {
        state.Timer?.Stop();
        if (string.IsNullOrWhiteSpace(state.StateToken))
        {
            return;
        }

        state.Timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        state.Timer.Tick += async (_, _) =>
        {
            try
            {
                var result = await _viewModel.GetStatusAsync(state.StateToken);
                state.StatusText = result.Value.Status switch
                {
                    "ok" => "授权成功",
                    "error" => $"授权失败：{result.Value.Error}",
                    _ => "等待授权完成"
                };

                if (!string.Equals(result.Value.Status, "wait", StringComparison.OrdinalIgnoreCase))
                {
                    state.Timer?.Stop();
                }
            }
            catch (Exception exception)
            {
                state.StatusText = exception.Message;
                state.Timer?.Stop();
            }

            Render();
        };
        state.Timer.Start();
    }

    private sealed class OAuthProviderState
    {
        public OAuthProviderState(string title)
        {
            Title = title;
        }

        public string Title { get; }

        public string ProjectId { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        public string StateToken { get; set; } = string.Empty;

        public string CallbackUrl { get; set; } = string.Empty;

        public string StatusText { get; set; } = string.Empty;

        public DispatcherTimer? Timer { get; set; }
    }
}
