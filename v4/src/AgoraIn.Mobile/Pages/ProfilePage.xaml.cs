namespace AgoraIn.Mobile.Pages;

public partial class ProfilePage : ContentPage
{
    public ProfilePage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var user = App.Api.CurrentUser;
        UsernameLabel.Text = user?.Username ?? "未登录";
        RoleLabel.Text = user == null ? "" : $"角色：{RoleText(user.Role)}";
        ServerEntry.Text = App.Api.BaseUrl;
        ServerLabel.Text = $"服务器：{App.Api.BaseUrl}";
        LoginButton.IsVisible = !App.Api.IsLoggedIn;
        LogoutButton.IsVisible = App.Api.IsLoggedIn;
    }

    private static string RoleText(string role) => role switch
    {
        "admin" => "管理员",
        "teacher" => "教师",
        "parent" => "家长",
        "student" => "学生",
        _ => role,
    };

    private void OnLoginClicked(object? sender, EventArgs e)
        => AppShell.SwitchToLogin();

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var ok = await DisplayAlertAsync("退出登录", "确定要退出当前账户吗？", "退出", "取消");
        if (!ok) return;
        App.Api.Logout();
        AppShell.SwitchToLogin();
    }
}
