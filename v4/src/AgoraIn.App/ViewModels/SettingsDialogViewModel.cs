using CommunityToolkit.Mvvm.ComponentModel;

namespace AgoraIn.App.ViewModels;

/// <summary>设置对话框 ViewModel：左侧选项卡 + 右侧内容切换。</summary>
public partial class SettingsDialogViewModel : ObservableObject
{
    public IReadOnlyList<string> Tabs { get; } = ["基本设置", "远程连接", "关于"];

    [ObservableProperty] private string _selectedTab = "基本设置";

    public bool IsBasic => SelectedTab == "基本设置";
    public bool IsRemote => SelectedTab == "远程连接";
    public bool IsAbout => SelectedTab == "关于";

    // 基本设置
    [ObservableProperty] private int _buttonRows = 6;
    [ObservableProperty] private int _buttonCols = 6;
    [ObservableProperty] private double _hoursPerHour = 1;
    [ObservableProperty] private bool _autoDeduct;

    // 远程连接
    [ObservableProperty] private string _serverIp = "";
    [ObservableProperty] private int _serverPort = 5250;
    [ObservableProperty] private bool _onlineMode;

    partial void OnSelectedTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsBasic));
        OnPropertyChanged(nameof(IsRemote));
        OnPropertyChanged(nameof(IsAbout));
    }
}
