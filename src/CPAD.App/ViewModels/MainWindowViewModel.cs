using CPAD.Core.Abstractions.Build;

using CommunityToolkit.Mvvm.ComponentModel;

namespace CPAD.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private string _title;
    private string _subtitle = "CPAD 原生管理中心";

    public MainWindowViewModel(IBuildInfo buildInfo)
    {
        _title = $"Cli Proxy API Desktop / CPAD {buildInfo.ApplicationVersion}";
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
