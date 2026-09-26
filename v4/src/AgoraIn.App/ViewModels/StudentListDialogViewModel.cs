using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgoraIn.App.ViewModels;

/// <summary>学生管理对话框 ViewModel。</summary>
public partial class StudentListDialogViewModel : ObservableObject
{
    private readonly string _tabId;
    private readonly Action<IReadOnlyList<string>> _saveAction;

    public ObservableCollection<StudentEntry> Students { get; } = new();

    [ObservableProperty] private string _newName = "";

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand SaveCommand { get; }

    public event EventHandler? RequestClose;

    public StudentListDialogViewModel(string tabId, IReadOnlyList<string> existingNames, Action<IReadOnlyList<string>> saveAction)
    {
        _tabId = tabId;
        _saveAction = saveAction;

        foreach (var name in existingNames)
        {
            Students.Add(new StudentEntry { Name = name });
        }

        AddCommand = new RelayCommand(DoAdd);
        RemoveCommand = new RelayCommand<StudentEntry?>(DoRemove);
        RemoveSelectedCommand = new RelayCommand(DoRemoveSelected);
        SaveCommand = new RelayCommand(DoSave);
    }

    private void DoAdd()
    {
        var name = NewName.Trim();
        if (name.Length == 0) return;
        if (Students.Any(s => s.Name == name)) return;
        Students.Add(new StudentEntry { Name = name });
        NewName = "";
    }

    private void DoRemove(StudentEntry? entry)
    {
        if (entry == null) return;
        Students.Remove(entry);
    }

    private void DoRemoveSelected()
    {
        var selected = Students.Where(s => s.IsSelected).ToList();
        foreach (var s in selected) Students.Remove(s);
    }

    private void DoSave()
    {
        _saveAction(Students.Select(s => s.Name).ToList());
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>学生列表条目。</summary>
public sealed class StudentEntry : INotifyPropertyChanged
{
    private bool _isSelected;
    public string Name { get; set; } = "";
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
