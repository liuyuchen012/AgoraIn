using AgoraIn.Mobile.Services;

namespace AgoraIn.Mobile.Pages;

public partial class HomePage : ContentPage
{
    public HomePage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 未登录跳登录页
        if (!App.Api.IsLoggedIn)
        {
            await Shell.Current.GoToAsync("//login");
            return;
        }

        var user = App.Api.CurrentUser;
        WelcomeLabel.Text = $"你好，{user?.Username ?? "同学"}";
        RoleLabel.Text = RoleText(user?.Role);
        DashboardButton.IsVisible = user?.Role is "admin" or "teacher";

        await LoadClassesAsync();
    }

    private async Task LoadClassesAsync()
    {
        try
        {
            var classes = await App.Api.GetClassesAsync();
            ClassesLabel.Text = classes is { Count: > 0 }
                ? string.Join("\n", classes.Select(c => $"· {c.Name}"))
                : "暂无班级";
        }
        catch
        {
            ClassesLabel.Text = "无法加载班级（请检查服务器连接）";
        }
    }

    private static string RoleText(string? role) => role switch
    {
        "admin" => "管理员",
        "teacher" => "教师",
        "parent" => "家长",
        "student" => "学生",
        _ => "用户",
    };

    private async void OnScanClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//scan");

    private async void OnTasksClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("tasks");

    private async void OnDashboardClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("dashboard");
}
