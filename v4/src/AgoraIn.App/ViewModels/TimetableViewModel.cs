using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgoraIn.App.ViewModels;

/// <summary>课表编辑器 ViewModel：科目管理、时间布局、周视图网格。</summary>
public partial class TimetableViewModel : ObservableObject
{
    private readonly Services.TimetableStore _store;
    private Services.TimetableData _data = new();

    public ObservableCollection<TtSubjectEntry> Subjects { get; } = new();
    public ObservableCollection<TtLayoutEntry> Layouts { get; } = new();

    /// <summary>周视图网格：7行×最多10列，每格=科目ID或空。</summary>
    public ObservableCollection<string> WeekGrid { get; } = new();
    public int GridColumns { get; private set; } = 8;

    private string _selectedDay = "周一";
    public string SelectedDay { get => _selectedDay; set { if (SetProperty(ref _selectedDay, value)) RefreshGrid(); } }

    private string _status = "课表编辑器";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    public IReadOnlyList<string> WeekDays { get; } = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];

    public ICommand AddSubjectCommand { get; }
    public ICommand SaveCommand { get; }

    public TimetableViewModel(string baseDir)
    {
        _store = new Services.TimetableStore(baseDir);
        AddSubjectCommand = new RelayCommand(DoAddSubject);
        SaveCommand = new RelayCommand(DoSave);

        Load();
        RefreshGrid();
    }

    private void Load()
    {
        _data = _store.Load();

        Subjects.Clear();
        foreach (var s in _data.Subjects)
            Subjects.Add(new TtSubjectEntry { Id = s.Id, Name = s.Name, Color = s.Color ?? "#4285f4" });

        Layouts.Clear();
        foreach (var tl in _data.TimeLayouts)
            Layouts.Add(new TtLayoutEntry { Id = tl.Id, Name = tl.Name, EntryCount = tl.Entries.Count });

        Status = $"已加载 {Subjects.Count} 科目，{Layouts.Count} 布局";
    }

    private void RefreshGrid()
    {
        WeekGrid.Clear();
        var dayIndex = Array.IndexOf(WeekDays.ToArray(), SelectedDay);
        var plan = _data.ClassPlans.FirstOrDefault(p => p.IsActive) ?? _data.ClassPlans.FirstOrDefault();
        if (plan == null)
        {
            for (var i = 0; i < 8; i++) WeekGrid.Add("");
            return;
        }

        var layout = _data.TimeLayouts.FirstOrDefault(t => t.Id == plan.TimeLayoutId);
        var maxSlot = layout?.Entries.Count ?? 8;
        GridColumns = Math.Max(1, maxSlot);

        for (var sl = 0; sl < maxSlot; sl++)
        {
            var key = dayIndex * 100 + sl;
            var subId = plan.Entries.TryGetValue(key, out var id) ? id : "";
            var sub = _data.Subjects.FirstOrDefault(s => s.Id == subId);
            WeekGrid.Add(sub?.Name ?? "");
        }

        OnPropertyChanged(nameof(GridColumns));
    }

    private void DoAddSubject()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var sub = new Services.TtSubject { Id = id, Name = $"科目{Subjects.Count + 1}" };
        _data.Subjects.Add(sub);
        Subjects.Add(new TtSubjectEntry { Id = id, Name = sub.Name, Color = "#4285f4" });
        Status = $"已添加科目：{sub.Name}";
    }

    private void DoSave()
    {
        _store.Save(_data);
        Status = "课表已保存";
    }
}

public sealed class TtSubjectEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4285f4";
}

public sealed class TtLayoutEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int EntryCount { get; set; }
}
