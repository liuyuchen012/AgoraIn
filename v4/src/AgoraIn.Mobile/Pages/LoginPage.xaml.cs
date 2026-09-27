namespace AgoraIn.Mobile.Pages;

public partial class LoginPage : ContentPage
{
    public LoginPage()
    {
        InitializeComponent();

        // 回填上次的服务器地址与用户名
        var lastUser = Preferences.Default.Get("agorain_last_username", "");
        if (!string.IsNullOrEmpty(lastUser)) UsernameEntry.Text = lastUser;
        var lastServer = App.Api.BaseUrl;
        if (!string.IsNullOrEmpty(lastServer)) ServerEntry.Text = lastServer;
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        var server = ServerEntry.Text?.Trim() ?? "";
        var username = UsernameEntry.Text?.Trim() ?? "";
        var password = PasswordEntry.Text ?? "";

        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("请填写服务器地址、用户名和密码");
            return;
        }

        if (!server.StartsWith("http://") && !server.StartsWith("https://"))
            server = "http://" + server;

        SetBusy(true);
        try
        {
            var user = await App.Api.LoginAsync(server, username, password);
            Preferences.Default.Set("agorain_last_username", username);
            await Shell.Current.GoToAsync("//home");
        }
        catch (Exception ex)
        {
            ShowError($"登录失败：{ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowError(string msg)
    {
        ErrorLabel.Text = msg;
        ErrorLabel.IsVisible = true;
    }

    private void SetBusy(bool busy)
    {
        Busy.IsRunning = busy;
        Busy.IsVisible = busy;
        LoginButton.IsEnabled = !busy;
    }
}
