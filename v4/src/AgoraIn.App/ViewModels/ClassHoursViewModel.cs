using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using AgoraIn.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgoraIn.App.ViewModels;

/// <summary>课时划消面板 ViewModel（控制模式 → 划课）。</summary>
public partial class ClassHoursViewModel : ObservableObject
{
    private readonly string _baseDir;
    private ClassHourData _data = new();

    public ObservableCollection<ChStudentEntry> Students { get; } = new();
    public ObservableCollection<ChRecordEntry> Records { get; } = new();
    public ObservableCollection<CalendarDayItem> CalendarDays { get; } = new();

    private ChStudentEntry? _selectedStudent;
    public ChStudentEntry? SelectedStudent
    {
        get => _selectedStudent;
        set
        {
            if (SetProperty(ref _selectedStudent, value) && value != null)
                RefreshRecords(value);
        }
    }

    private string _selectedDetail = "";
    public string SelectedDetail { get => _selectedDetail; set => SetProperty(ref _selectedDetail, value); }

    private double _operateHours;
    public double OperateHours { get => _operateHours; set => SetProperty(ref _operateHours, value); }

    private string _operateNote = "";
    public string OperateNote { get => _operateNote; set => SetProperty(ref _operateNote, value); }

    private double _hoursPerHour = 1;
    public double HoursPerHour
    {
        get => _hoursPerHour;
        set { if (SetProperty(ref _hoursPerHour, value)) { _data.HoursPerHour = value; Save(); } }
    }

    private bool _autoDeduct;
    public bool AutoDeduct
    {
        get => _autoDeduct;
        set { if (SetProperty(ref _autoDeduct, value)) { _data.AutoDeduct = value; Save(); } }
    }

    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly SelectedDate
    {
        get => _selectedDate;
        set { if (SetProperty(ref _selectedDate, value)) RefreshScheduleDetail(); }
    }

    private DateOnly _currentMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateOnly CurrentMonth
    {
        get => _currentMonth;
        set { if (SetProperty(ref _currentMonth, value)) BuildCalendar(); }
    }

    public ICommand AddStudentCommand { get; }
    public ICommand DeleteStudentCommand { get; }
    public ICommand DeductCommand { get; }
    public ICommand GiftCommand { get; }
    public ICommand PrevMonthCommand { get; }
    public ICommand NextMonthCommand { get; }
    public ICommand ToggleOffDayCommand { get; }

    public ClassHoursViewModel(string baseDir)
    {
        _baseDir = baseDir;
        AddStudentCommand = new RelayCommand(DoAddStudent);
        DeleteStudentCommand = new RelayCommand<ChStudentEntry?>(DoDeleteStudent);
        DeductCommand = new RelayCommand(DoDeduct);
        GiftCommand = new RelayCommand(DoGift);
        PrevMonthCommand = new RelayCommand(() => CurrentMonth = CurrentMonth.AddMonths(-1));
        NextMonthCommand = new RelayCommand(() => CurrentMonth = CurrentMonth.AddMonths(1));
        ToggleOffDayCommand = new RelayCommand(DoToggleOffDay);

        Load();
        BuildCalendar();
    }

    // ═══ 数据加载/保存 ═══

    private void Load()
    {
        var path = Path.Combine(_baseDir, "data", "classhours.json");
        if (File.Exists(path))
        {
            try
            {
                _data = JsonSerializer.Deserialize<ClassHourData>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ClassHourData();
            }
            catch { _data = new ClassHourData(); }
        }

        HoursPerHour = _data.HoursPerHour;
        AutoDeduct = _data.AutoDeduct;
        ReloadStudents();
    }

    private void Save()
    {
        var path = Path.Combine(_baseDir, "data", "classhours.json");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(_data,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ReloadStudents()
    {
        Students.Clear();
        foreach (var s in _data.Students)
        {
            Students.Add(new ChStudentEntry
            {
                Id = s.Id,
                Name = s.Name,
                TotalHours = s.TotalHours,
                UsedHours = s.UsedHours,
            });
        }
    }

    // ═══ 学生管理 ═══

    private void DoAddStudent()
    {
        var name = "新学生";
        var id = Guid.NewGuid().ToString("N")[..8];
        var s = new ChStudentV3 { Id = id, Name = name };
        _data.Students.Add(s);
        Students.Add(new ChStudentEntry { Id = id, Name = name });
        Save();
    }

    private void DoDeleteStudent(ChStudentEntry? entry)
    {
        if (entry == null) return;
        _data.Students.RemoveAll(s => s.Id == entry.Id);
        _data.Records.RemoveAll(r => r.StudentId == entry.Id);
        foreach (var day in _data.Schedule.Values)
            day.RemoveAll(e => e.StudentId == entry.Id);
        Students.Remove(entry);
        Save();
    }

    // ═══ 划消/赠送 ═══

    private void DoDeduct()
    {
        if (SelectedStudent == null || OperateHours <= 0) return;
        var entry = _data.Students.FirstOrDefault(s => s.Id == SelectedStudent.Id);
        if (entry == null) return;

        var actual = Math.Min(OperateHours, entry.TotalHours - entry.UsedHours);
        if (actual <= 0) return;

        entry.UsedHours += actual;
        var record = new ChRecordV3
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            StudentId = entry.Id,
            Date = DateTime.Now.ToString("yyyy-MM-dd"),
            Hours = -actual,
            Note = string.IsNullOrEmpty(OperateNote) ? "划消" : OperateNote,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        _data.Records.Add(record);
        Save();

        SelectedStudent.UsedHours = entry.UsedHours;
        RefreshRecords(SelectedStudent);
        OperateHours = 0;
        OperateNote = "";
    }

    private void DoGift()
    {
        if (SelectedStudent == null || OperateHours <= 0) return;
        var entry = _data.Students.FirstOrDefault(s => s.Id == SelectedStudent.Id);
        if (entry == null) return;

        entry.TotalHours += OperateHours;
        var record = new ChRecordV3
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            StudentId = entry.Id,
            Date = DateTime.Now.ToString("yyyy-MM-dd"),
            Hours = OperateHours,
            Note = string.IsNullOrEmpty(OperateNote) ? "赠送" : OperateNote,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        _data.Records.Add(record);
        Save();

        SelectedStudent.TotalHours = entry.TotalHours;
        RefreshRecords(SelectedStudent);
        OperateHours = 0;
        OperateNote = "";
    }

    private void RefreshRecords(ChStudentEntry student)
    {
        Records.Clear();
        var recs = _data.Records
            .Where(r => r.StudentId == student.Id)
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
        foreach (var r in recs)
        {
            Records.Add(new ChRecordEntry
            {
                Date = r.Date,
                Hours = r.Hours,
                HoursText = r.Hours < 0 ? $"{r.Hours:0.#}" : $"+{r.Hours:0.#}",
                Note = r.Note,
                IsDeduct = r.Hours < 0,
            });
        }

        var remaining = student.TotalHours - student.UsedHours;
        SelectedDetail = $"{student.Name}  剩余 {remaining:0.#} 课时";
    }

    // ═══ 月历 ═══

    private void BuildCalendar()
    {
        CalendarDays.Clear();
        var firstDay = new DateOnly(CurrentMonth.Year, CurrentMonth.Month, 1);
        var startOfWeek = firstDay.AddDays(-(((int)firstDay.DayOfWeek + 6) % 7)); // 周一起始
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var i = 0; i < 42; i++)
        {
            var date = startOfWeek.AddDays(i);
            var dayKey = date.ToString("yyyy-MM-dd");
            var isOff = _data.OffDays.Contains(dayKey);
            var schedCount = _data.Schedule.TryGetValue(dayKey, out var entries) ? entries.Count : 0;

            CalendarDays.Add(new CalendarDayItem
            {
                Date = date,
                Day = date.Day,
                IsCurrentMonth = date.Month == CurrentMonth.Month,
                IsToday = date == today,
                IsOffDay = isOff,
                ScheduledCount = schedCount,
            });
        }
    }

    public void SelectCalendarDay(CalendarDayItem item)
    {
        foreach (var d in CalendarDays) d.IsSelected = false;
        item.IsSelected = true;
        SelectedDate = item.Date;
    }

    private void RefreshScheduleDetail()
    {
        BuildCalendar(); // 刷新计数
        var dayKey = SelectedDate.ToString("yyyy-MM-dd");
        var isOff = _data.OffDays.Contains(dayKey);
        var count = _data.Schedule.TryGetValue(dayKey, out var entries) ? entries.Count : 0;
        SelectedDetail = isOff ? $"{SelectedDate:yyyy-MM-dd} 不排课日（休）" : $"{SelectedDate:yyyy-MM-dd}  排课 {count} 人";
    }

    private void DoToggleOffDay()
    {
        var dayKey = SelectedDate.ToString("yyyy-MM-dd");
        if (_data.OffDays.Contains(dayKey))
        {
            _data.OffDays.Remove(dayKey);
        }
        else
        {
            _data.OffDays.Add(dayKey);
            _data.Schedule.Remove(dayKey);
        }
        Save();
        RefreshScheduleDetail();
    }
}

// ═══ 辅助模型 ═══

public sealed class ChStudentEntry : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    private double _totalHours;
    public double TotalHours { get => _totalHours; set { _totalHours = value; OnPropertyChanged(); OnPropertyChanged(nameof(RemainingHours)); } }
    private double _usedHours;
    public double UsedHours { get => _usedHours; set { _usedHours = value; OnPropertyChanged(); OnPropertyChanged(nameof(RemainingHours)); } }
    public double RemainingHours => TotalHours - UsedHours;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class ChRecordEntry
{
    public string Date { get; set; } = "";
    public double Hours { get; set; }
    public string HoursText { get; set; } = "";
    public string Note { get; set; } = "";
    public bool IsDeduct { get; set; }
}

/// <summary>v3 classhours.json 模型（读写共用）。</summary>
public sealed class ClassHourData
{
    public int Version { get; set; } = 3;
    public List<ChStudentV3> Students { get; set; } = new();
    public List<ChRecordV3> Records { get; set; } = new();
    public Dictionary<string, List<ChScheduleEntryV3>> Schedule { get; set; } = new();
    public List<string> OffDays { get; set; } = new();
    public double HoursPerHour { get; set; } = 1;
    public bool AutoDeduct { get; set; }
}

public sealed class ChStudentV3
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double TotalHours { get; set; }
    public double UsedHours { get; set; }
    public string Remark { get; set; } = "";
}

public sealed class ChRecordV3
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Date { get; set; } = "";
    public double Hours { get; set; }
    public string Note { get; set; } = "";
    public string? SlotKey { get; set; }
    public string CreatedAt { get; set; } = "";
}

public sealed class ChScheduleEntryV3
{
    public string StudentId { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
}
