namespace AgoraIn.Mobile.Pages;

public partial class LoginPage : ContentPage
{
    public LoginPage()
    {
        InitializeComponent();

        // 回填上次用户名；服务器地址固定为官方域名
        var lastUser = Preferences.Default.Get("agorain_last_username", "");
        if (!string.IsNullOrEmpty(lastUser)) UsernameEntry.Text = lastUser;
        ServerLabel.Text = App.Api.BaseUrl;
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        var username = UsernameEntry.Text?.Trim() ?? "";
        var password = PasswordEntry.Text ?? "";

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("请填写用户名和密码");
            return;
        }

        SetBusy(true);
        try
        {
            await App.Api.LoginAsync(username, password);
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
