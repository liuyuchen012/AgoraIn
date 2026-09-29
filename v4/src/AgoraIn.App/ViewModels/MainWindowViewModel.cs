using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using AgoraIn.App.Models;
using AgoraIn.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgoraIn.App.ViewModels;

/// <summary>模式下拉框选项。</summary>
public sealed record ModeOption(AppMode Mode, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 主窗口 ViewModel：任务树、标签页、打卡面板、排名、控制模式导航、菜单命令。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly Services.TaskService _taskService;
    private readonly Services.AppConfig _appConfig;
    private readonly string _baseDir;
    private readonly Dictionary<string, StudentModel> _students = new(StringComparer.Ordinal);
    private string _activeTabId = "";

    // ═══ 模式切换 ═══
    public IReadOnlyList<ModeOption> ModeOptions { get; } =
        Enum.GetValues<AppMode>().Select(m => new ModeOption(m, m.ToDisplayName())).ToList();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CurrentMode))]
    [NotifyPropertyChangedFor(nameof(IsLargeScreen))] [NotifyPropertyChangedFor(nameof(IsControlMode))]
    [NotifyPropertyChangedFor(nameof(IsTeacherMode))]
    [NotifyPropertyChangedFor(nameof(IsWebPanelMode))] [NotifyPropertyChangedFor(nameof(WebPanelUrl))]
    private ModeOption _selectedMode;

    public AppMode CurrentMode => SelectedMode.Mode;
    public bool IsLargeScreen => CurrentMode == AppMode.LargeScreen;
    public bool IsControlMode => CurrentMode == AppMode.Control;
    public bool IsTeacherMode => CurrentMode == AppMode.Teacher;
    public bool IsTimetable => Teacher.SelectedNav == "课表";

    // ═══ 内嵌服务端 Web 面板（控制 / 教师模式）═══

    /// <summary>控制/教师模式均使用内嵌网页（大屏模式保留原生打卡界面）。</summary>
    public bool IsWebPanelMode => CurrentMode is AppMode.Control or AppMode.Teacher;

    /// <summary>当前模式对应的服务端页面地址（服务器地址已锁定为官方域名）。</summary>
    public string WebPanelUrl => Core.AppConstants.ServerBaseUrl + (CurrentMode switch
    {
        AppMode.Teacher => Core.AppConstants.WebAdminTeacherPath,
        _ => Core.AppConstants.WebAdminControlPath,
    });

    private bool _webPanelReady = Controls.WebViewHost.IsSupported;
    /// <summary>内嵌网页是否可用（非 Windows 或 WebView2 缺失时为 false，显示降级提示）。</summary>
    public bool WebPanelReady { get => _webPanelReady; set => SetProperty(ref _webPanelReady, value); }

    private string _webPanelHint = "当前系统不支持内嵌浏览器（仅 Windows + WebView2 支持）。可在浏览器中打开下方地址使用完整管理功能。";
    public string WebPanelHint { get => _webPanelHint; set => SetProperty(ref _webPanelHint, value); }

    /// <summary>当前主题设置（供设置对话框回显："system"/"light"/"dark"）。</summary>
    public string AppConfigTheme => _appConfig.Theme;

    /// <summary>应用配置（首次启动向导写入用）。</summary>
    public Services.AppConfig AppConfig => _appConfig;

    /// <summary>循环切换模式：大屏→控制→教师→大屏。</summary>
    public void CycleMode()
    {
        var next = CurrentMode switch
        {
            AppMode.LargeScreen => AppMode.Control,
            AppMode.Control => AppMode.Teacher,
            _ => AppMode.LargeScreen,
        };
        SelectedMode = ModeOptions.First(o => o.Mode == next);
    }

    /// <summary>应用设置对话框的修改（对话框关闭后调用）并落盘。</summary>
    public void ApplySettings(SettingsDialogViewModel vm)
    {
        ButtonRows = vm.ButtonRows;
        ButtonCols = vm.ButtonCols;
        ClassHours.HoursPerHour = vm.HoursPerHour;
        ClassHours.AutoDeduct = vm.AutoDeduct;

        TimetableDriver.Enabled = vm.TimetableDriven;
        TimetableDriver.RemindMinutesBefore = vm.RemindMinutesBefore;

        _appConfig.ButtonRows = vm.ButtonRows;
        _appConfig.ButtonCols = vm.ButtonCols;
        _appConfig.HoursPerHour = vm.HoursPerHour;
        _appConfig.AutoDeduct = vm.AutoDeduct;
        _appConfig.TimetableDriven = vm.TimetableDriven;
        _appConfig.RemindMinutesBefore = vm.RemindMinutesBefore;
        _appConfig.StartupMode = vm.StartupModeIndex;
        _appConfig.OnlineMode = vm.OnlineMode;
        _appConfig.Theme = vm.ThemeIndex switch
        {
            1 => "light",
            2 => "dark",
            _ => "system",
        };
        App.ApplyTheme(_appConfig.Theme);
        _appConfig.Save();

        StatusMessage = "设置已保存";
    }

    // ═══ 在线状态 ═══
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OnlineText))]
    private bool _isOnline;
    public string OnlineText => IsOnline ? "服务器: 在线" : "服务器: 离线";

    // ═══ 任务树 ═══
    public ObservableCollection<TaskTreeNode> TaskTree { get; } = new();

    // ═══ 标签页 ═══
    public ObservableCollection<TabInfo> Tabs { get; } = new();

    private TabInfo? _activeTab;
    public TabInfo? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (SetProperty(ref _activeTab, value) && value != null)
            {
                _activeTabId = value.Id;
                LoadTabData(value.Id);
            }
        }
    }

    public bool HasActiveTab => ActiveTab != null;

    // ═══ 打卡面板 ═══
    public ObservableCollection<StudentModel> Students { get; } = new();
    public ObservableCollection<RankingItem> Ranking { get; } = new();

    private string _statusMessage = "就绪";
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    private string _punchInfo = "总人数: 0 | 已打卡: 0 (0%)";
    public string PunchInfo { get => _punchInfo; set => SetProperty(ref _punchInfo, value); }

    private int _buttonRows = 6;
    public int ButtonRows { get => _buttonRows; set => SetProperty(ref _buttonRows, value); }

    private int _buttonCols = 6;
    public int ButtonCols { get => _buttonCols; set => SetProperty(ref _buttonCols, value); }

    // ═══ 控制模式导航 ═══
    private string _controlNavSelected = "划课";
    public string ControlNavSelected { get => _controlNavSelected; set => SetProperty(ref _controlNavSelected, value); }

    // ═══ 课时划消面板 ═══
    public ClassHoursViewModel ClassHours { get; }

    // ═══ 教师模式面板 ═══
    public TeacherViewModel Teacher { get; }

    // ═══ 课表编辑器 ═══
    public TimetableViewModel Timetable { get; }

    /// <summary>课表驱动服务（上下课自动切换模式 / 点名提醒 / 课时划消联动）。</summary>
    public Services.TimetableDriver TimetableDriver { get; }

    // ═══ 命令 ═══
    public ICommand AddTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand CheckInCommand { get; }
    public ICommand CancelCheckInCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand NewTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand ShowStudentListCommand { get; }

    public MainWindowViewModel() : this(AppDomain.CurrentDomain.BaseDirectory) { }

    public MainWindowViewModel(string baseDir)
    {
        _baseDir = baseDir;
        _taskService = new Services.TaskService(baseDir);
        _appConfig = Services.AppConfig.Load(baseDir);
        _selectedMode = ModeOptions[Math.Clamp(_appConfig.StartupMode, 0, 2)];
        ClassHours = new ClassHoursViewModel(baseDir);
        Teacher = new TeacherViewModel(baseDir);
        Timetable = new TimetableViewModel(baseDir);

        // 课表驱动：上课切大屏、下课切控制；上课前提醒点名
        TimetableDriver = new Services.TimetableDriver(baseDir)
        {
            Enabled = _appConfig.TimetableDriven,
            RemindMinutesBefore = _appConfig.RemindMinutesBefore,
        };
        TimetableDriver.ClassStateChanged += (_, isClass) =>
        {
            SelectedMode = ModeOptions.First(o =>
                o.Mode == (isClass ? AppMode.LargeScreen : AppMode.Control));
            StatusMessage = isClass ? "上课中：已切换大屏模式" : "课间/下课：已切换控制模式";
        };
        TimetableDriver.ClassStartingSoon += (_, e) =>
        {
            StatusMessage = $"⏰ {e.SlotName} 还有 {e.MinutesBefore} 分钟上课，可开始点名";
        };

        AddTabCommand = new RelayCommand(AddTab);
        CloseTabCommand = new RelayCommand<string?>(CloseTab);
        CheckInCommand = new RelayCommand<StudentModel?>(DoCheckIn);
        CancelCheckInCommand = new RelayCommand<StudentModel?>(DoCancelCheckIn);
        ClearAllCommand = new RelayCommand(DoClearAll);
        ExportCommand = new RelayCommand(DoExport);
        ImportCommand = new RelayCommand(DoImport);
        NewTaskCommand = new RelayCommand(DoNewTask);
        DeleteTaskCommand = new RelayCommand<string?>(DoDeleteTask);
        ShowStudentListCommand = new RelayCommand(DoShowStudentList);

        LoadWorkspace();
    }

    /// <summary>外部调用：重新加载当前活动标签页数据（学生列表修改后刷新面板）。</summary>
    public void ReloadActiveTab()
    {
        if (_activeTabId.Length > 0)
        {
            LoadTabData(_activeTabId);
        }
    }

    // ═══ 工作区加载 ═══

    private void LoadWorkspace()
    {
        // 加载任务树
        RefreshTaskTree();

        // 加载标签页
        var tabsFile = Path.Combine(_baseDir, "workspace.json");
        if (File.Exists(tabsFile))
        {
            try
            {
                var ws = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(tabsFile),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (ws?.Tabs is { Count: > 0 })
                {
                    foreach (var info in ws.Tabs)
                    {
                        Tabs.Add(info);
                    }

                    ActiveTab = Tabs.FirstOrDefault(t => t.Id == ws.ActiveTabId) ?? Tabs[0];
                    return;
                }
            }
            catch { }
        }

        // 全新安装：创建默认标签
        var id = _taskService.CreateTab("默认任务", "数学");
        var tab = new TabInfo { Id = id, Name = "默认任务" };
        Tabs.Add(tab);
        ActiveTab = tab;
        SaveWorkspace();
        RefreshTaskTree();
    }

    private void SaveWorkspace()
    {
        var ws = new WorkspaceState
        {
            Tabs = Tabs.Select(t => new TabInfo { Id = t.Id, Name = t.Name }).ToList(),
            ActiveTabId = ActiveTab?.Id,
        };
        var path = Path.Combine(_baseDir, "workspace.json");
        File.WriteAllText(path, JsonSerializer.Serialize(ws,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private void RefreshTaskTree()
    {
        TaskTree.Clear();
        foreach (var node in _taskService.BuildTaskTree())
        {
            TaskTree.Add(node);
        }
    }

    // ═══ 标签页数据加载 ═══

    private void LoadTabData(string tabId)
    {
        Students.Clear();
        Ranking.Clear();

        var names = _taskService.ReadStudentNames(tabId);
        var loaded = _taskService.LoadAttendance(tabId, names);

        _students.Clear();
        foreach (var kvp in loaded)
        {
            _students[kvp.Key] = kvp.Value;
            Students.Add(kvp.Value);
        }

        var configPath = Path.Combine(_baseDir, "data", "tabs", tabId, "config.json");
        if (File.Exists(configPath))
        {
            try
            {
                var config = JsonSerializer.Deserialize<TabConfig>(File.ReadAllText(configPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (config != null)
                {
                    ButtonRows = config.ButtonRows;
                    ButtonCols = config.ButtonCols;
                }
            }
            catch { }
        }

        RefreshRanking();
        StatusMessage = $"已加载 {names.Count} 名学生";
    }

    // ═══ 打卡 / 取消 ═══

    private void DoCheckIn(StudentModel? student)
    {
        if (student == null || student.IsCheckedIn) return;
        Services.TaskService.CheckIn(student);
        RefreshRanking();
        SaveAttendance();
        StatusMessage = $"{student.Name} 已打卡";
    }

    private void DoCancelCheckIn(StudentModel? student)
    {
        if (student == null || !student.IsCheckedIn) return;
        Services.TaskService.CancelCheckIn(student);
        RefreshRanking();
        SaveAttendance();
        StatusMessage = $"已取消 {student.Name} 的打卡";
    }

    private void DoClearAll()
    {
        Services.TaskService.ClearAllCheckIn(_students);
        RefreshRanking();
        SaveAttendance();
        StatusMessage = "已清空所有打卡记录";
    }

    private void RefreshRanking()
    {
        Ranking.Clear();
        foreach (var item in Services.TaskService.ComputeRanking(_students))
        {
            Ranking.Add(item);
        }

        var total = _students.Count;
        var checkedIn = _students.Values.Count(s => s.IsCheckedIn);
        var pct = total > 0 ? checkedIn * 100 / total : 0;
        PunchInfo = $"总人数: {total} ｜ 已打卡: {checkedIn} ({pct}%)";
    }

    private void SaveAttendance()
    {
        if (_activeTabId.Length > 0)
        {
            _taskService.SaveAttendance(_activeTabId, _students);
        }
    }

    // ═══ 导入导出 ═══

    private void DoExport()
    {
        var csv = Services.TaskService.ExportCsv(_students);
        var path = Path.Combine(_baseDir, $"打卡数据_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        File.WriteAllText(path, csv, System.Text.Encoding.UTF8);
        StatusMessage = $"已导出到 {Path.GetFileName(path)}";
    }

    private void DoImport()
    {
        // P2: 简单导入——从同目录下最新 CSV 文件导入
        var latest = Directory.GetFiles(_baseDir, "打卡数据_*.csv")
            .OrderByDescending(f => f)
            .FirstOrDefault();
        if (latest == null)
        {
            StatusMessage = "未找到可导入的 CSV 文件";
            return;
        }

        var csv = File.ReadAllText(latest, System.Text.Encoding.UTF8);
        var count = Services.TaskService.ImportCsv(csv, _students);
        Students.Clear();
        foreach (var s in _students.Values) Students.Add(s);
        RefreshRanking();
        SaveAttendance();
        StatusMessage = $"已导入 {count} 条记录（{Path.GetFileName(latest)}）";
    }

    // ═══ 任务管理 ═══

    private void AddTab()
    {
        var id = _taskService.CreateTab("新任务", "数学");
        var tab = new TabInfo { Id = id, Name = "新任务" };
        Tabs.Add(tab);
        ActiveTab = tab;
        SaveWorkspace();
        RefreshTaskTree();
    }

    private void CloseTab(string? tabId)
    {
        if (tabId == null) return;
        var tab = Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null) return;

        Tabs.Remove(tab);
        if (ActiveTab?.Id == tabId)
        {
            ActiveTab = Tabs.FirstOrDefault();
        }

        SaveWorkspace();
    }

    private void DoNewTask()
    {
        var id = _taskService.CreateTab("新任务", "数学");
        var tab = new TabInfo { Id = id, Name = "新任务" };
        Tabs.Add(tab);
        ActiveTab = tab;
        SaveWorkspace();
        RefreshTaskTree();
    }

    private void DoDeleteTask(string? tabId)
    {
        if (string.IsNullOrEmpty(tabId)) return;
        _taskService.DeleteTab(tabId);
        var tab = Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab != null)
        {
            Tabs.Remove(tab);
            if (ActiveTab?.Id == tabId) ActiveTab = Tabs.FirstOrDefault();
        }

        SaveWorkspace();
        RefreshTaskTree();
        StatusMessage = "已删除任务";
    }

    private void DoShowStudentList()
    {
        RequestShowStudentList?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>外部（MainWindow）订阅此事件以打开学生管理对话框。</summary>
    public event EventHandler? RequestShowStudentList;
}

/// <summary>标签页引用（workspace.json 持久化）。</summary>
public sealed class TabInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>工作区状态（workspace.json）。</summary>
public sealed class WorkspaceState
{
    public List<TabInfo> Tabs { get; set; } = new();
    public string? ActiveTabId { get; set; }
}
