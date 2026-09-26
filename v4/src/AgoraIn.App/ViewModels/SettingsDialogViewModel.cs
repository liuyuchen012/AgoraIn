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
    [ObservableProperty] private int _startupModeIndex; // 0=大屏 1=控制 2=教师
    public IReadOnlyList<string> StartupModes { get; } = ["大屏模式", "控制模式", "教师模式"];

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
