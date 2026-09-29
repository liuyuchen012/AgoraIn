namespace AgoraIn.Mobile.Pages;

public partial class RegisterPage : ContentPage
{
    /// <summary>true = 家长加入已有区域（join）；false = 机构注册创建区域（region）。</summary>
    private bool JoinMode => ModePicker.SelectedIndex == 0;

    public RegisterPage()
    {
        InitializeComponent();
        ModePicker.SelectedIndex = 0;
        RegionNameEntry.IsVisible = false;
    }

    private void OnModeChanged(object? sender, EventArgs e)
    {
        RegionIdEntry.IsVisible = JoinMode;
        RegionNameEntry.IsVisible = !JoinMode;
    }

    private async void OnSendCodeClicked(object? sender, EventArgs e)
    {
        var email = EmailEntry.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(email))
        {
            await DisplayAlertAsync("提示", "请先填写邮箱", "知道了");
            return;
        }

        SendCodeButton.IsEnabled = false;
        try
        {
            var res = await App.Api.SendRegisterCodeAsync(email);
            await DisplayAlertAsync("验证码", res?.Message ?? "验证码已发送，请查收邮件", "知道了");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("发送失败", ex.Message, "知道了");
            SendCodeButton.IsEnabled = true;
            return;
        }

        // 60 秒冷却
        var seconds = 60;
        var timer = Application.Current?.Dispatcher.CreateTimer();
        if (timer != null)
        {
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (_, _) =>
            {
                seconds--;
                SendCodeButton.Text = seconds > 0 ? $"{seconds}s" : "获取验证码";
                if (seconds <= 0)
                {
                    timer.Stop();
                    SendCodeButton.IsEnabled = true;
                    SendCodeButton.Text = "获取验证码";
                }
            };
            timer.Start();
        }
    }

    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        var email = EmailEntry.Text?.Trim() ?? "";
        var code = CodeEntry.Text?.Trim() ?? "";
        var username = UsernameEntry.Text?.Trim() ?? "";
        var password = PasswordEntry.Text ?? "";
        var regionId = RegionIdEntry.Text?.Trim() ?? "";
        var regionName = RegionNameEntry.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(code) ||
            string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            await DisplayAlertAsync("提示", "请填写完整信息", "知道了");
            return;
        }
        if (JoinMode && string.IsNullOrEmpty(regionId))
        {
            await DisplayAlertAsync("提示", "请填写区域代号（向学校/机构索取）", "知道了");
            return;
        }
        if (!JoinMode && string.IsNullOrEmpty(regionName))
        {
            await DisplayAlertAsync("提示", "请填写区域名称", "知道了");
            return;
        }
        if (!AgreeCheck.IsChecked)
        {
            await DisplayAlertAsync("提示", "请先同意服务条款与隐私政策", "知道了");
            return;
        }

        SetBusy(true);
        try
        {
            var res = await App.Api.RegisterAsync(
                email, code, username, password,
                JoinMode ? "join" : "region",
                regionId,
                JoinMode ? null : regionName);

            await DisplayAlertAsync("注册成功",
                $"{res?.Message}{(string.IsNullOrEmpty(res?.RegionId) ? "" : $"\n区域代号：{res.RegionId}\n登录名：{res.LoginName}")}",
                "去登录");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("注册失败", ex.Message, "知道了");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnBackToLoginClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private void SetBusy(bool busy)
    {
        Busy.IsRunning = busy;
        Busy.IsVisible = busy;
        SubmitButton.IsEnabled = !busy;
    }
}
