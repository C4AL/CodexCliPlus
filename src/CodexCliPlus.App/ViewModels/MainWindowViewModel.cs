using CommunityToolkit.Mvvm.ComponentModel;
using CodexCliPlus.Core.Abstractions.Build;
using CodexCliPlus.Core.Constants;

namespace CodexCliPlus.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private string _title;
    private string _subtitle = "官方 Web 管理界面";

    public MainWindowViewModel(IBuildInfo buildInfo)
    {
        _title = $"{AppConstants.DisplayName} 桌面版 {buildInfo.ApplicationVersion}";
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        set => SetProperty(ref _subtitle, value);
    }
}
