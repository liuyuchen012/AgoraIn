using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgoraIn.App.ViewModels;

/// <summary>教师模式 ViewModel：聚合点名/积分/值日/座位四个子面板。</summary>
public partial class TeacherViewModel : ObservableObject
{
    private readonly string _baseDir;

    [ObservableProperty] private string _selectedNav = "点名";
    public bool IsRollCall => SelectedNav == "点名";
    public bool IsPoints => SelectedNav == "积分";
    public bool IsDuty => SelectedNav == "值日";
    public bool IsSeats => SelectedNav == "座位";

    // ═══ 点名 ═══
    public ObservableCollection<RollCallStudent> RollCallStudents { get; } = new();
    [ObservableProperty] private string _rollCallStatus = "点击「开始点名」随机抽取学生";

    // ═══ 积分 ═══
    public ObservableCollection<PointRuleEntry> PointRules { get; } = new();
    public ObservableCollection<PointRecordEntry> PointRecords { get; } = new();
    [ObservableProperty] private string _pointStatus = "选择学生和规则进行加减分";

    // ═══ 值日 ═══
    public ObservableCollection<DutyPostEntry> DutyPosts { get; } = new();
    public ObservableCollection<DutyTodayEntry> DutyToday { get; } = new();
    [ObservableProperty] private string _dutyStatus = "今日值日安排";

    // ═══ 座位 ═══
    public ObservableCollection<string> SeatStudents { get; } = new();

    public ICommand StartRollCallCommand { get; }
    public ICommand AddPointRuleCommand { get; }
    public ICommand AddDutyPostCommand { get; }

    partial void OnSelectedNavChanged(string value)
    {
        OnPropertyChanged(nameof(IsRollCall));
        OnPropertyChanged(nameof(IsPoints));
        OnPropertyChanged(nameof(IsDuty));
        OnPropertyChanged(nameof(IsSeats));
    }

    public TeacherViewModel(string baseDir)
    {
        _baseDir = baseDir;
        StartRollCallCommand = new RelayCommand(DoStartRollCall);
        AddPointRuleCommand = new RelayCommand(DoAddPointRule);
        AddDutyPostCommand = new RelayCommand(DoAddDutyPost);
        Load();
    }

    private void Load()
    {
        LoadRollCallData();
        LoadPointData();
        LoadDutyData();
    }

    // ═══ 点名 ═══

    private void LoadRollCallData()
    {
        RollCallStudents.Clear();
        var namesPath = Path.Combine(_baseDir, "data", "tabs");
        if (!Directory.Exists(namesPath)) return;

        foreach (var dir in Directory.GetDirectories(namesPath))
        {
            var nameFile = Path.Combine(dir, "name.txt");
            if (!File.Exists(nameFile)) continue;
            foreach (var name in File.ReadAllLines(nameFile).Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                if (RollCallStudents.All(s => s.Name != name))
                    RollCallStudents.Add(new RollCallStudent { Name = name });
            }
        }
    }

    private void DoStartRollCall()
    {
        if (RollCallStudents.Count == 0) { RollCallStatus = "无学生数据"; return; }
        var rng = new Random();
        var picked = RollCallStudents[rng.Next(RollCallStudents.Count)];
        RollCallStatus = $"🎲 点到：{picked.Name}";
    }

    // ═══ 积分 ═══

    private void LoadPointData()
    {
        PointRules.Clear();
        PointRules.Add(new PointRuleEntry { Name = "课堂发言", Delta = 1 });
        PointRules.Add(new PointRuleEntry { Name = "作业优秀", Delta = 2 });
        PointRules.Add(new PointRuleEntry { Name = "迟到", Delta = -1 });
        PointRules.Add(new PointRuleEntry { Name = "课堂违纪", Delta = -2 });
    }

    private void DoAddPointRule()
    {
        PointRules.Add(new PointRuleEntry { Name = "新规则", Delta = 1 });
    }

    // ═══ 值日 ═══

    private void LoadDutyData()
    {
        DutyPosts.Clear();
        DutyPosts.Add(new DutyPostEntry { Name = "扫地", Icon = "🧹", Capacity = 2 });
        DutyPosts.Add(new DutyPostEntry { Name = "擦黑板", Icon = "📋", Capacity = 1 });
        DutyPosts.Add(new DutyPostEntry { Name = "倒垃圾", Icon = "🗑️", Capacity = 1 });
    }

    private void DoAddDutyPost()
    {
        DutyPosts.Add(new DutyPostEntry { Name = "新岗位", Capacity = 1 });
    }
}

// ═══ 辅助模型 ═══

public sealed class RollCallStudent : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    private string _result = "";
    public string Result { get => _result; set { _result = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class PointRuleEntry
{
    public string Name { get; set; } = "";
    public int Delta { get; set; } = 1;
}

public sealed class PointRecordEntry
{
    public string Student { get; set; } = "";
    public string Rule { get; set; } = "";
    public int Delta { get; set; }
    public string Time { get; set; } = "";
}

public sealed class DutyPostEntry
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "📌";
    public int Capacity { get; set; } = 1;
}

public sealed class DutyTodayEntry
{
    public string Student { get; set; } = "";
    public string Post { get; set; } = "";
    public bool Completed { get; set; }
}
