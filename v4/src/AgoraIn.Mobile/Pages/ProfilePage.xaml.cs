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
        ServerLabel.Text = string.IsNullOrEmpty(App.Api.BaseUrl) ? "" : $"服务器：{App.Api.BaseUrl}";
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

    private async void OnSaveServerClicked(object? sender, EventArgs e)
    {
        var url = ServerEntry.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(url))
        {
            await DisplayAlertAsync("提示", "请输入服务器地址", "知道了");
            return;
        }
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "http://" + url;

        App.Api.BaseUrl = url;
        await DisplayAlertAsync("已保存", $"服务器地址已更新：{url}", "好的");
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//login");

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var ok = await DisplayAlertAsync("退出登录", "确定要退出当前账户吗？", "退出", "取消");
        if (!ok) return;
        App.Api.Logout();
        await Shell.Current.GoToAsync("//login");
    }
}
