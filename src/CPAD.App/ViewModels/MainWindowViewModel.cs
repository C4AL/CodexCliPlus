using CommunityToolkit.Mvvm.ComponentModel;
using CPAD.Core.Abstractions.Build;

namespace CPAD.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private string _title;
    private string _subtitle = "官方 Web 管理界面";

    public MainWindowViewModel(IBuildInfo buildInfo)
    {
        _title = $"CPAD 桌面版 {buildInfo.ApplicationVersion}";
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
