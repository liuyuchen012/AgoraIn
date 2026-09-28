using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgoraIn.Core.Entities;

namespace AgoraIn.App.Models;

/// <summary>任务树节点（文件夹 / 任务两级）。</summary>
public sealed class TaskTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isSelected;

    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsFolder { get; set; }
    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public string? TabId { get; set; }
    public ObservableCollection<TaskTreeNode> Children { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>学生模型（打卡面板 + 排名绑定）。</summary>
public sealed class StudentModel : INotifyPropertyChanged
{
    private string _name = "";
    private int _count;
    private DateTime? _firstTime;
    private bool _isCheckedIn;

    public string Id { get; set; } = "";
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public int Count { get => _count; set { _count = value; OnPropertyChanged(); } }
    public DateTime? FirstTime
    {
        get => _firstTime;
        set { _firstTime = value; IsCheckedIn = value != null; OnPropertyChanged(); }
    }
    public bool IsCheckedIn { get => _isCheckedIn; set { _isCheckedIn = value; OnPropertyChanged(); } }
    public List<string> History { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>排名条目。</summary>
public sealed class RankingItem
{
    public int Rank { get; set; }
    public string Name { get; set; } = "";
    public string Time { get; set; } = "";

    /// <summary>金银铜高亮（沿用 v3 语义，视图按此绑定 Classes）。</summary>
    public bool IsFirst => Rank == 1;
    public bool IsSecond => Rank == 2;
    public bool IsThird => Rank == 3;
}

/// <summary>标签页配置（data/tabs/{id}/config.json）。</summary>
public sealed class TabConfig
{
    public string Name { get; set; } = "";
    public string Km { get; set; } = "";
    public int ButtonRows { get; set; } = 6;
    public int ButtonCols { get; set; } = 6;
    public bool OnlineMode { get; set; } = true;
    public bool IsSignInTask { get; set; }
    public string? SignInTaskId { get; set; }
}

/// <summary>月历日期条目。</summary>
public sealed class CalendarDayItem : INotifyPropertyChanged
{
    private bool _isSelected;
    public DateOnly Date { get; set; }
    public int Day { get; set; }
    public bool IsCurrentMonth { get; set; }
    public bool IsToday { get; set; }
    public bool IsOffDay { get; set; }
    public int ScheduledCount { get; set; }
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

    public string DisplayText => IsOffDay ? "休" : ScheduledCount > 0 ? ScheduledCount.ToString() : Day.ToString();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
