using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgoraIn.App.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
    }

    private void OnOpenDownload(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SettingsDialogViewModel vm && !string.IsNullOrEmpty(vm.DownloadUrl))
        {
            try { Process.Start(new ProcessStartInfo(vm.DownloadUrl) { UseShellExecute = true }); }
            catch { /* 静默 */ }
        }
    }
}
