using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgoraIn.App.ViewModels;

/// <summary>设置对话框 ViewModel：左侧选项卡 + 右侧内容切换。</summary>
public partial class SettingsDialogViewModel : ObservableObject
{
    public IReadOnlyList<string> Tabs { get; } = ["基本设置", "远程连接", "更新检查", "关于"];

    [ObservableProperty] private string _selectedTab = "基本设置";

    public bool IsBasic => SelectedTab == "基本设置";
    public bool IsRemote => SelectedTab == "远程连接";
    public bool IsUpdate => SelectedTab == "更新检查";
    public bool IsAbout => SelectedTab == "关于";

    // 基本设置
    [ObservableProperty] private int _buttonRows = 6;
    [ObservableProperty] private int _buttonCols = 6;
    [ObservableProperty] private double _hoursPerHour = 1;
    [ObservableProperty] private bool _autoDeduct;
    [ObservableProperty] private int _startupModeIndex; // 0=大屏 1=控制 2=教师
    public IReadOnlyList<string> StartupModes { get; } = ["大屏模式", "控制模式", "教师模式"];

    [ObservableProperty] private int _themeIndex; // 0=跟随系统 1=明 2=暗
    public IReadOnlyList<string> Themes { get; } = ["跟随系统", "明亮", "暗黑"];

    // 远程连接（服务器地址已锁定为官方域名，不可编辑）
    /// <summary>固定服务端地址（只读展示）。</summary>
    public string ServerAddress => Core.AppConstants.ServerBaseUrl;

    /// <summary>服务端主机名（只读展示）。</summary>
    public string ServerHostName => Core.AppConstants.ServerHost;

    [ObservableProperty] private bool _onlineMode = true;
    [ObservableProperty] private int _remindMinutesBefore = 2;
    [ObservableProperty] private bool _timetableDriven;

    // 更新检查
    [ObservableProperty] private bool _checking;
    [ObservableProperty] private string _updateStatus = "点击「检查更新」查看是否有新版本";
    [ObservableProperty] private string _currentVersion = "v4.0";
    [ObservableProperty] private string _latestVersion = "";
    [ObservableProperty] private bool _hasUpdate;
    [ObservableProperty] private string _downloadUrl = "";

    partial void OnSelectedTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsBasic));
        OnPropertyChanged(nameof(IsRemote));
        OnPropertyChanged(nameof(IsUpdate));
        OnPropertyChanged(nameof(IsAbout));
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        Checking = true;
        UpdateStatus = "正在检查更新…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = $"{Core.AppConstants.ServerBaseUrl}/api/v4/update/check";
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                UpdateStatus = "检查更新失败（服务器未响应）";
                return;
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var latestVersion = root.GetProperty("latestVersion").GetString() ?? "";
            var downloadUrl = root.GetProperty("downloadUrl").GetString() ?? "";
            var currentVersion = root.GetProperty("currentVersion").GetString() ?? "";
            var hasUpdate = root.GetProperty("hasUpdate").GetBoolean();

            CurrentVersion = currentVersion;
            LatestVersion = latestVersion;
            HasUpdate = hasUpdate;
            DownloadUrl = downloadUrl;

            if (hasUpdate)
                UpdateStatus = $"发现新版本 {latestVersion}，可前往下载";
            else
                UpdateStatus = $"当前已是最新版本（{currentVersion}）";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"检查更新失败：{ex.Message}";
        }
        finally
        {
            Checking = false;
        }
    }
}
