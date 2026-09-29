namespace AgoraIn.Mobile.Pages;

public partial class ScanPage : ContentPage
{
    private bool _cameraOn;

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

            // ZXing.Net.MAUI：相机视图通过 IsVisible 切换挂载/卸载，自动开始/停止预览
            _cameraOn = !_cameraOn;
            CameraCard.IsVisible = _cameraOn;
            ScanButton.Text = _cameraOn ? "📷 关闭相机" : "📷 开启相机扫码";
            if (_cameraOn)
            {
                CameraView.Options = new ZXing.Net.Maui.BarcodeReaderOptions
                {
                    Formats = ZXing.Net.Maui.BarcodeFormat.QrCode | ZXing.Net.Maui.BarcodeFormat.Code128,
                    AutoRotate = true,
                    Multiple = false,
                };
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("扫码失败", ex.Message, "知道了");
        }
    }

    /// <summary>识别到条码：提取签到码，关闭相机并自动提交。</summary>
    private void OnBarcodesDetected(object? sender, ZXing.Net.Maui.BarcodeDetectionEventArgs e)
    {
        var value = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrEmpty(value)) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (!_cameraOn) return;
            _cameraOn = false;
            CameraCard.IsVisible = false;
            ScanButton.Text = "📷 开启相机扫码";

            CodeEntry.Text = ExtractCode(value);
            SetBusy(true);
            try
            {
                await SubmitAsync();
            }
            finally
            {
                SetBusy(false);
            }
        });
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
        SetBusy(true);
        try
        {
            await SubmitAsync();
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

    private async Task SubmitAsync()
    {
        var code = CodeEntry.Text?.Trim() ?? "";
        var name = NameEntry.Text?.Trim() ?? "";
        var password = PasswordEntry.Text ?? "";

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name))
        {
            ShowResult(false, "请补全信息", "请填写签到码和姓名");
            return;
        }

        var res = await App.Api.SubmitScanAsync(code, name, password);
        ShowResult(res?.Success == true,
            res?.Success == true ? "签到成功" : "签到失败",
            res?.Rank is int rank ? $"你是第 {rank} 位签到的同学" : res?.Message ?? "");
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
