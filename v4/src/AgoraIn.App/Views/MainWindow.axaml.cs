using System;
using System.Runtime.InteropServices;
using AgoraIn.App.Models;
using AgoraIn.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AgoraIn.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainWindowViewModel();
        DataContext = _vm;
        _vm.RequestShowStudentList += OnShowStudentList;
    }

    // ═══ 标签栏 ═══

    private void OnTabTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.Tag is TabInfo tab && DataContext is MainWindowViewModel vm)
        {
            vm.ActiveTab = vm.Tabs.FirstOrDefault(t => t.Id == tab.Id);
        }
    }

    private void OnAddTabTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.AddTabCommand.Execute(null);
        }
    }

    // ═══ 模式切换 ═══

    private void OnModeClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.CycleMode();
    }

    // ═══ 日历 ═══

    private void OnCalendarDayClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is CalendarDayItem day && DataContext is MainWindowViewModel vm)
        {
            vm.ClassHours.SelectCalendarDay(day);
        }
    }

    // ═══ 学生管理 ═══

    private async void OnShowStudentList(object? sender, EventArgs e)
    {
        if (_vm?.ActiveTab == null) return;

        var names = new Services.TaskService(AppDomain.CurrentDomain.BaseDirectory)
            .ReadStudentNames(_vm.ActiveTab.Id);

        var dialog = new StudentListDialog();
        var vm = new StudentListDialogViewModel(
            _vm.ActiveTab.Id,
            names,
            newNames =>
            {
                var svc = new Services.TaskService(AppDomain.CurrentDomain.BaseDirectory);
                svc.WriteStudentNames(_vm.ActiveTab.Id, newNames);
                _vm.ReloadActiveTab();
            });

        dialog.DataContext = vm;
        vm.RequestClose += (_, _) => dialog.Close();
        dialog.ShowDialog(this);
    }

    // ═══ 窗口控制 ═══

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyRoundedCornerPreference();
    }

    private void ApplyRoundedCornerPreference()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;
            var preference = 2;
            DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
