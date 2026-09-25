using System.Collections.Generic;
using System.Linq;
using AgoraIn.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgoraIn.App.ViewModels;

/// <summary>模式下拉框选项。</summary>
public sealed record ModeOption(AppMode Mode, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 主窗口 ViewModel：顶部模式下拉框（大屏 / 控制 / 教师三档）与在线状态指示灯。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    public IReadOnlyList<ModeOption> ModeOptions { get; } =
        Enum.GetValues<AppMode>().Select(m => new ModeOption(m, m.ToDisplayName())).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMode))]
    [NotifyPropertyChangedFor(nameof(IsLargeScreen))]
    [NotifyPropertyChangedFor(nameof(IsControlMode))]
    [NotifyPropertyChangedFor(nameof(IsTeacherMode))]
    private ModeOption _selectedMode;

    /// <summary>服务器在线状态（沿用绿/红在线指示灯语义；同步引擎在 P5 接入）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnlineText))]
    private bool _isOnline;

    /// <summary>状态行文案（大屏模式右侧状态区）。</summary>
    public string OnlineText => IsOnline ? "服务器: 在线" : "服务器: 离线";

    public MainWindowViewModel()
    {
        _selectedMode = ModeOptions[0];
        _isOnline = false;
    }

    public AppMode CurrentMode => SelectedMode.Mode;

    public bool IsLargeScreen => CurrentMode == AppMode.LargeScreen;

    public bool IsControlMode => CurrentMode == AppMode.Control;

    public bool IsTeacherMode => CurrentMode == AppMode.Teacher;
}
