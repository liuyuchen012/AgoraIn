using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using CheckIn.Client.Mobile.Services;

namespace CheckIn.Client.Mobile.ViewModels;

/// <summary>
/// 排课管理 ViewModel：月历视图、排课列表、课时学生管理
/// </summary>
public class ScheduleViewModel : INotifyPropertyChanged
{
    private readonly ApiService _api;

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }

    private string _message = "";
    public string Message { get => _message; set { _message = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrEmpty(Message);

    // 设备选择
    public ObservableCollection<DeviceItem> Devices { get; } = new();
    private DeviceItem? _selectedDevice;
    public DeviceItem? SelectedDevice
    {
        get => _selectedDevice;
        set { _selectedDevice = value; OnPropertyChanged(); LoadScheduleData(); }
    }

    // 日期选择
    private DateTime _selectedDate = DateTime.Today;
    public DateTime SelectedDate
    {
        get => _selectedDate;
        set { _selectedDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedDateText)); LoadDaySchedules(); }
    }
    public string SelectedDateText => SelectedDate.ToString("yyyy年M月d日 dddd");

    // 月历
    private int _calYear = DateTime.Now.Year;
    public int CalYear { get => _calYear; set { _calYear = value; OnPropertyChanged(); LoadCalendar(); } }
    private int _calMonth = DateTime.Now.Month;
    public int CalMonth { get => _calMonth; set { _calMonth = value; OnPropertyChanged(); OnPropertyChanged(nameof(CalMonthText)); LoadCalendar(); } }
    public string CalMonthText => $"{CalYear}年{CalMonth}月";

    public ObservableCollection<CalendarDayItem> CalendarDays { get; } = new();

    // 当日排课
    public ObservableCollection<ScheduleItem> DaySchedules { get; } = new();

    // 课时学生
    public ObservableCollection<ClassHourStudentItem> ClassHourStudents { get; } = new();

    // 设置
    private double _hoursPerHour = 1.0;
    public double HoursPerHour { get => _hoursPerHour; set { _hoursPerHour = value; OnPropertyChanged(); } }
    private bool _autoDeduct;
    public bool AutoDeduct { get => _autoDeduct; set { _autoDeduct = value; OnPropertyChanged(); } }

    public ICommand RefreshCommand { get; }
    public ICommand PrevMonthCommand { get; }
    public ICommand NextMonthCommand { get; }
    public ICommand DeleteScheduleCommand { get; }
    public ICommand? PickDateCommand { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScheduleViewModel(ApiService api)
    {
        _api = api;
        RefreshCommand = new Command(async () =>
        {
            try { await LoadDevicesAsync(); }
            catch { }
        });
        PrevMonthCommand = new Command(() =>
        {
            CalMonth--;
            if (CalMonth < 1) { CalMonth = 12; CalYear--; }
        });
        NextMonthCommand = new Command(() =>
        {
            CalMonth++;
            if (CalMonth > 12) { CalMonth = 1; CalYear++; }
        });
        DeleteScheduleCommand = new Command<int>(async (id) =>
        {
            try { await DeleteScheduleAsync(id); }
            catch { }
        });
    }

    public async Task LoadDevicesAsync()
    {
        IsLoading = true;
        try
        {
            var result = await _api.GetAsync("/api/mobile/devices");
            if (ApiService.GetError(result) != null) return;

            Devices.Clear();
            if (result.TryGetProperty("devices", out var arr))
            {
                foreach (var d in arr.EnumerateArray())
                {
                    Devices.Add(new DeviceItem
                    {
                        Uuid = ApiService.GetString(d, "uuid") ?? "",
                        Name = ApiService.GetString(d, "name") ?? "",
                        Online = d.TryGetProperty("online", out var ol) && ol.GetBoolean()
                    });
                }
            }
            if (Devices.Count > 0 && SelectedDevice == null)
                SelectedDevice = Devices[0];
        }
        catch (Exception ex)
        {
            Message = $"加载设备失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void LoadScheduleData()
    {
        if (SelectedDevice == null) return;
        LoadDaySchedules();
        LoadCalendar();
        LoadClassHourStudents();
        LoadSettings();
    }

    private async void LoadDaySchedules()
    {
        if (SelectedDevice == null) return;
        try
        {
            var dateStr = SelectedDate.ToString("yyyy-MM-dd");
            var result = await _api.GetAsync($"/api/mobile/schedules?machine_uuid={SelectedDevice.Uuid}&date={dateStr}");
            DaySchedules.Clear();
            if (result.TryGetProperty("schedules", out var arr))
            {
                foreach (var s in arr.EnumerateArray())
                {
                    DaySchedules.Add(new ScheduleItem
                    {
                        Id = s.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                        StudentName = ApiService.GetString(s, "student_name") ?? "",
                        StartTime = ApiService.GetString(s, "start_time") ?? "",
                        EndTime = ApiService.GetString(s, "end_time") ?? ""
                    });
                }
            }
        }
        catch { }
    }

    private async void LoadCalendar()
    {
        if (SelectedDevice == null) return;
        try
        {
            var result = await _api.GetAsync($"/api/mobile/schedules/calendar?machine_uuid={SelectedDevice.Uuid}&year={CalYear}&month={CalMonth}");
            CalendarDays.Clear();

            var countMap = new Dictionary<string, int>();
            if (result.TryGetProperty("schedule_counts", out var counts))
                foreach (var c in counts.EnumerateArray())
                {
                    var date = ApiService.GetString(c, "date") ?? "";
                    var count = c.TryGetProperty("count", out var cnt) ? cnt.GetInt32() : 0;
                    if (!string.IsNullOrEmpty(date)) countMap[date] = count;
                }

            var offDays = new HashSet<string>();
            if (result.TryGetProperty("off_days", out var odArr))
                foreach (var od in odArr.EnumerateArray())
                {
                    var d = od.GetString() ?? "";
                    if (!string.IsNullOrEmpty(d)) offDays.Add(d);
                }

            var firstDay = (int)new DateTime(CalYear, CalMonth, 1).DayOfWeek;
            var daysInMonth = DateTime.DaysInMonth(CalYear, CalMonth);
            var today = DateTime.Today.ToString("yyyy-MM-dd");

            // 填充前置空位
            for (int i = 0; i < firstDay; i++)
                CalendarDays.Add(new CalendarDayItem { Day = 0 });

            for (int d = 1; d <= daysInMonth; d++)
            {
                var dateStr = $"{CalYear}-{CalMonth:D2}-{d:D2}";
                countMap.TryGetValue(dateStr, out var scheduleCount);
                CalendarDays.Add(new CalendarDayItem
                {
                    Day = d,
                    Date = dateStr,
                    ScheduleCount = scheduleCount,
                    IsOffDay = offDays.Contains(dateStr),
                    IsToday = dateStr == today
                });
            }
        }
        catch { }
    }

    private async void LoadClassHourStudents()
    {
        if (SelectedDevice == null) return;
        try
        {
            var result = await _api.GetAsync($"/api/mobile/classhour-students?machine_uuid={SelectedDevice.Uuid}");
            ClassHourStudents.Clear();
            if (result.TryGetProperty("students", out var arr))
            {
                foreach (var s in arr.EnumerateArray())
                {
                    var total = s.TryGetProperty("total_hours", out var th) ? th.GetDouble() : 0;
                    var used = s.TryGetProperty("used_hours", out var uh) ? uh.GetDouble() : 0;
                    ClassHourStudents.Add(new ClassHourStudentItem
                    {
                        Id = s.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                        Name = ApiService.GetString(s, "name") ?? "",
                        TotalHours = total,
                        UsedHours = used,
                        RemainingHours = total - used
                    });
                }
            }
        }
        catch { }
    }

    private async void LoadSettings()
    {
        if (SelectedDevice == null) return;
        try
        {
            var result = await _api.GetAsync($"/api/mobile/classhour-settings?machine_uuid={SelectedDevice.Uuid}");
            HoursPerHour = result.TryGetProperty("hours_per_hour", out var h) ? h.GetDouble() : 1.0;
            AutoDeduct = result.TryGetProperty("auto_deduct", out var a) && a.GetBoolean();
        }
        catch { }
    }

    private async Task DeleteScheduleAsync(int id)
    {
        try
        {
            var result = await _api.DeleteAsync($"/api/mobile/schedules/{id}");
            if (ApiService.GetError(result) == null)
            {
                LoadDaySchedules();
                LoadCalendar();
            }
        }
        catch { }
    }

    public void PickDate(string dateStr)
    {
        if (DateTime.TryParse(dateStr, out var dt))
            SelectedDate = dt;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class DeviceItem
{
    public string Uuid { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Online { get; set; }
}

public class CalendarDayItem
{
    public int Day { get; set; }
    public string Date { get; set; } = "";
    public int ScheduleCount { get; set; }
    public bool IsOffDay { get; set; }
    public bool IsToday { get; set; }
}

public class ScheduleItem
{
    public int Id { get; set; }
    public string StudentName { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
}

public class ClassHourStudentItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double TotalHours { get; set; }
    public double UsedHours { get; set; }
    public double RemainingHours { get; set; }
}