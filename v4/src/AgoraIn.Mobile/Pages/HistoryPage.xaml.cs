using AgoraIn.Mobile.Services;

namespace AgoraIn.Mobile.Pages;

public partial class HistoryPage : ContentPage
{
    private readonly List<HistoryItem> _items = new();

    public HistoryPage()
    {
        InitializeComponent();
        RecordList.ItemsSource = _items;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!App.Api.IsLoggedIn)
        {
            AppShell.SwitchToLogin();
            return;
        }
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
            var user = App.Api.CurrentUser;
            var records = await App.Api.GetHistoryAsync(user?.Username ?? "");
            _items.Clear();
            if (records != null)
            {
                foreach (var r in records)
                {
                    _items.Add(new HistoryItem
                    {
                        Title = string.IsNullOrEmpty(r.TaskId) ? "课堂签到" : r.TaskId,
                        TimeText = r.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    });
                }
            }
            RecordList.ItemsSource = null;
            RecordList.ItemsSource = _items;
        }
        catch
        {
            // 网络异常时保留现有列表
        }
        finally
        {
            Busy.IsRunning = false;
            Busy.IsVisible = false;
        }
    }
}

public sealed class HistoryItem
{
    public string Title { get; set; } = "";
    public string TimeText { get; set; } = "";
}
