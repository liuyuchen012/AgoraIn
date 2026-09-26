using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AgoraIn.App.ViewModels;

namespace AgoraIn.App.Views;

public partial class StudentListDialog : Window
{
    public StudentListDialog()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private void OnNewNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is StudentListDialogViewModel vm)
        {
            vm.AddCommand.Execute(null);
        }
    }
}
