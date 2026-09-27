using System;
using System.Diagnostics;
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

        // 内嵌网页面板：初始化失败时降级为「用浏览器打开」
        WebHost.LoadFailed += OnWebHostLoadFailed;
    }

    // ═══ 内嵌服务端网页（控制 / 教师模式）═══

    private void OnWebHostLoadFailed(object? sender, string message)
    {
        if (_vm == null) return;
        _vm.WebPanelReady = false;
        _vm.WebPanelHint = message;
    }

    private void OnOpenWebPanelInBrowser(object? sender, RoutedEventArgs e)
    {
        var url = _vm?.WebPanelUrl ?? Core.AppConstants.WebAdminUrl;
        OpenInBrowser(url);
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch
        {
            // 无可用浏览器时静默失败（地址已在界面上展示）
        }
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

    // ═══ 设置 ═══

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var vm = new SettingsDialogViewModel
        {
            ButtonRows = _vm.ButtonRows,
            ButtonCols = _vm.ButtonCols,
            HoursPerHour = _vm.ClassHours.HoursPerHour,
            AutoDeduct = _vm.ClassHours.AutoDeduct,
            StartupModeIndex = _vm.CurrentMode switch
            {
                Core.AppMode.Control => 1,
                Core.AppMode.Teacher => 2,
                _ => 0,
            },
            TimetableDriven = _vm.TimetableDriver.Enabled,
            RemindMinutesBefore = _vm.TimetableDriver.RemindMinutesBefore,
        };

        var dialog = new SettingsDialog();
        dialog.DataContext = vm;
        var closed = dialog.ShowDialog(this);
        _ = closed.ContinueWith(_ =>
        {
            // 保存设置（对话框关闭后统一落盘）
            _vm.ApplySettings(vm);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ═══ 学生管理 ═══

    private void OnShowStudentList(object? sender, EventArgs e)
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
