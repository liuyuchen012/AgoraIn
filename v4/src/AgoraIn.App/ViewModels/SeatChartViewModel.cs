using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgoraIn.App.ViewModels;

/// <summary>座位编排 ViewModel：网格编辑器、学生分配、随机排座、快照。</summary>
public partial class SeatChartViewModel : ObservableObject
{
    private readonly string _baseDir;
    private List<string> _allStudents = new();
    private readonly Random _rng = new();

    public ObservableCollection<SeatCell> Seats { get; } = new();
    public ObservableCollection<string> UnseatedStudents { get; } = new();

    private int _rows = 6;
    public int Rows { get => _rows; set { if (SetProperty(ref _rows, value)) RebuildGrid(); } }

    private int _cols = 6;
    public int Cols { get => _cols; set { if (SetProperty(ref _cols, value)) RebuildGrid(); } }

    private string _status = "点击座位分配学生，或点击「随机排座」自动分配";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    private SeatCell? _selectedSeat;
    public SeatCell? SelectedSeat { get => _selectedSeat; set => SetProperty(ref _selectedSeat, value); }

    public ICommand RebuildCommand { get; }
    public ICommand RandomAssignCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand SaveCommand { get; }

    public SeatChartViewModel(string baseDir)
    {
        _baseDir = baseDir;
        RebuildCommand = new RelayCommand(RebuildGrid);
        RandomAssignCommand = new RelayCommand(DoRandomAssign);
        ClearAllCommand = new RelayCommand(DoClearAll);
        SaveCommand = new RelayCommand(DoSave);

        LoadStudents();
        RebuildGrid();
        LoadSnapshot();
    }

    private void LoadStudents()
    {
        _allStudents.Clear();
        var tabsRoot = Path.Combine(_baseDir, "data", "tabs");
        if (!Directory.Exists(tabsRoot)) return;
        foreach (var dir in Directory.GetDirectories(tabsRoot))
        {
            var nameFile = Path.Combine(dir, "name.txt");
            if (!File.Exists(nameFile)) continue;
            foreach (var name in File.ReadAllLines(nameFile).Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                if (!_allStudents.Contains(name))
                    _allStudents.Add(name.Trim());
            }
        }
    }

    private void RebuildGrid()
    {
        Seats.Clear();
        for (var r = 0; r < Rows; r++)
        {
            for (var c = 0; c < Cols; c++)
            {
                Seats.Add(new SeatCell
                {
                    Row = r,
                    Col = c,
                    TapCommand = new RelayCommand<SeatCell?>(OnSeatTapped),
                });
            }
        }
        RefreshUnseated();
    }

    private void OnSeatTapped(SeatCell? seat)
    {
        if (seat == null) return;
        SelectedSeat = seat;

        if (!string.IsNullOrEmpty(seat.StudentName))
        {
            // 已有学生：移除，放回未分配列表
            seat.StudentName = "";
            RefreshUnseated();
            Status = $"已移除座位 ({seat.Row + 1},{seat.Col + 1}) 的学生";
            return;
        }

        // 空位：从未分配列表取第一个学生
        if (UnseatedStudents.Count > 0)
        {
            var name = UnseatedStudents[0];
            UnseatedStudents.RemoveAt(0);
            seat.StudentName = name;
            Status = $"{name} → 座位 ({seat.Row + 1},{seat.Col + 1})";
        }
    }

    private void DoRandomAssign()
    {
        // 清空当前分配
        foreach (var s in Seats) { s.StudentName = ""; }

        // 随机打乱学生顺序
        var shuffled = _allStudents.OrderBy(_ => _rng.Next()).ToList();
        var idx = 0;
        foreach (var seat in Seats)
        {
            if (idx >= shuffled.Count) break;
            seat.StudentName = shuffled[idx++];
        }

        RefreshUnseated();
        Status = $"已随机排座 {Seats.Count(s => !s.IsEmpty)} 人";
    }

    private void DoClearAll()
    {
        foreach (var s in Seats) { s.StudentName = ""; }
        RefreshUnseated();
        Status = "已清空所有座位";
    }

    private void RefreshUnseated()
    {
        UnseatedStudents.Clear();
        var seated = Seats.Where(s => !s.IsEmpty).Select(s => s.StudentName).ToHashSet();
        foreach (var name in _allStudents)
        {
            if (!seated.Contains(name))
                UnseatedStudents.Add(name);
        }
    }

    // ═══ 快照保存/加载 ═══

    private void DoSave()
    {
        var snapshot = new SeatSnapshot
        {
            Rows = Rows,
            Cols = Cols,
            Assignments = Seats.Where(s => !s.IsEmpty)
                .Select(s => new SeatAssignment { Row = s.Row, Col = s.Col, Student = s.StudentName })
                .ToList(),
        };
        var path = Path.Combine(_baseDir, "data", "seat-chart.json");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        Status = $"座位图已保存（{snapshot.Assignments.Count} 人）";
    }

    private void LoadSnapshot()
    {
        var path = Path.Combine(_baseDir, "data", "seat-chart.json");
        if (!File.Exists(path)) return;
        try
        {
            var snapshot = JsonSerializer.Deserialize<SeatSnapshot>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (snapshot == null) return;
            Rows = snapshot.Rows;
            Cols = snapshot.Cols;
            foreach (var a in snapshot.Assignments)
            {
                var seat = Seats.FirstOrDefault(s => s.Row == a.Row && s.Col == a.Col);
                if (seat != null)
                {
                    seat.StudentName = a.Student;
                }
            }
            RefreshUnseated();
            Status = $"已加载座位图（{snapshot.Assignments.Count} 人）";
        }
        catch { }
    }
}

/// <summary>座位格子。</summary>
public sealed class SeatCell : INotifyPropertyChanged
{
    public int Row { get; set; }
    public int Col { get; set; }

    private string _studentName = "";
    public string StudentName { get => _studentName; set { _studentName = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(DisplayText)); } }

    public bool IsEmpty => string.IsNullOrEmpty(StudentName);
    public string DisplayText => IsEmpty ? $"{Row + 1}-{Col + 1}" : StudentName;

    public RelayCommand<SeatCell?>? TapCommand { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>座位图快照（持久化）。</summary>
public sealed class SeatSnapshot
{
    public int Rows { get; set; } = 6;
    public int Cols { get; set; } = 6;
    public List<SeatAssignment> Assignments { get; set; } = new();
}

public sealed class SeatAssignment
{
    public int Row { get; set; }
    public int Col { get; set; }
    public string Student { get; set; } = "";
}
