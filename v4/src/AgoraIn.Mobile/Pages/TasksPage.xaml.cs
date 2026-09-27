namespace AgoraIn.Mobile.Pages;

public partial class TasksPage : ContentPage
{
    private readonly List<TaskItem> _items = new();

    public TasksPage()
    {
        InitializeComponent();
        TaskList.ItemsSource = _items;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadAsync();

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadAsync();
        Refresh.IsRefreshing = false;
    }

    private async Task LoadAsync()
    {
        Busy.IsRunning = true;
        Busy.IsVisible = true;
        try
        {
            var tasks = await App.Api.GetTasksAsync();
            _items.Clear();
            if (tasks != null)
            {
                foreach (var t in tasks)
                {
                    _items.Add(new TaskItem
                    {
                        Name = string.IsNullOrEmpty(t.Name) ? t.TaskId : t.Name,
                        ProgressText = $"已打卡 {t.CheckedCount} / {t.TotalCount} 人",
                        RateText = t.TotalCount > 0
                            ? $"{t.CheckedCount * 100 / t.TotalCount}%"
                            : "—",
                    });
                }
            }
            TaskList.ItemsSource = null;
            TaskList.ItemsSource = _items;
        }
        catch
        {
            // 网络异常保留现有列表
        }
        finally
        {
            Busy.IsRunning = false;
            Busy.IsVisible = false;
        }
    }
}

public sealed class TaskItem
{
    public string Name { get; set; } = "";
    public string ProgressText { get; set; } = "";
    public string RateText { get; set; } = "";
}
