using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgoraIn.App.ViewModels;

namespace AgoraIn.App.Views;

/// <summary>
/// 主窗口：无边框圆角（DWM 圆角偏好，Win10 不支持时静默回退方角），
/// 顶部蓝栏 + 三模式下拉框，沿用 v3 视觉语言。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyRoundedCornerPreference();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnModeButtonClick(object? sender, RoutedEventArgs e)
    {
        ModePopup.IsOpen = !ModePopup.IsOpen;
        ModeButton.Classes.Set("open", ModePopup.IsOpen);
    }

    private void OnModeListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ModePopup.IsOpen)
        {
            ModePopup.IsOpen = false;
            ModeButton.Classes.Set("open", false);
        }
    }

    private void ApplyRoundedCornerPreference()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var preference = DwmWindowCornerPreference.Round;
            _ = DwmSetWindowAttribute(handle, DwmWindowAttribute.CornerPreference, ref preference, sizeof(int));
        }
        catch
        {
            // Windows 10 无 DWMWA_WINDOW_CORNER_PREFERENCE（Win11 起才有），回退方角即可
        }
    }

    private enum DwmWindowAttribute
    {
        CornerPreference = 33,
    }

    private enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3,
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, DwmWindowAttribute attribute, ref DwmWindowCornerPreference value, int size);
}
