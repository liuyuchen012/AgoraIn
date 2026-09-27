namespace AgoraIn.Mobile.Pages;

public partial class ScanPage : ContentPage
{
    public ScanPage()
    {
        InitializeComponent();
    }

    private async void OnScanQrClicked(object? sender, EventArgs e)
    {
        try
        {
            var status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                await DisplayAlertAsync("提示", "需要相机权限才能扫码", "知道了");
                return;
            }

            var result = await ScanAsync();
            if (!string.IsNullOrEmpty(result))
            {
                CodeEntry.Text = ExtractCode(result);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("扫码失败", ex.Message, "知道了");
        }
    }

    /// <summary>调用平台扫码能力（MAUI 无内置扫码，此处留待接入 ZXing.Net.Maui）。</summary>
    private Task<string?> ScanAsync()
    {
        // 说明：接入 ZXing.Net.Maui 包后替换为真实扫码实现。
        // 当前保留手动输码路径，扫码为可选增强。
        return Task.FromResult<string?>(null);
    }

    /// <summary>从扫码结果中提取签到码（支持 URL 或纯码）。</summary>
    private static string ExtractCode(string raw)
    {
        // 形如 http://host:5250/signin?code=ABC123 或 ABC123
        var idx = raw.IndexOf("code=", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var code = raw[(idx + 5)..];
            var amp = code.IndexOf('&');
            return amp >= 0 ? code[..amp] : code;
        }
        return raw.Trim();
    }

    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        var code = CodeEntry.Text?.Trim() ?? "";
        var name = NameEntry.Text?.Trim() ?? "";
        var password = PasswordEntry.Text ?? "";

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name))
        {
            await DisplayAlertAsync("提示", "请填写签到码和姓名", "知道了");
            return;
        }

        SetBusy(true);
        try
        {
            var res = await App.Api.SubmitScanAsync(code, name, password);
            ShowResult(res?.Success == true,
                res?.Success == true ? "签到成功" : "签到失败",
                res?.Rank is int rank ? $"你是第 {rank} 位签到的同学" : res?.Message ?? "");
        }
        catch (Exception ex)
        {
            ShowResult(false, "签到失败", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowResult(bool ok, string title, string detail)
    {
        ResultCard.IsVisible = true;
        ResultCard.BackgroundColor = ok
            ? Color.FromArgb("#E6F4EA")
            : Color.FromArgb("#FCE8E6");
        ResultTitle.Text = title;
        ResultTitle.TextColor = ok ? Color.FromArgb("#137333") : Color.FromArgb("#C5221F");
        ResultDetail.Text = detail;
    }

    private void SetBusy(bool busy)
    {
        Busy.IsRunning = busy;
        Busy.IsVisible = busy;
        SubmitButton.IsEnabled = !busy;
    }
}
