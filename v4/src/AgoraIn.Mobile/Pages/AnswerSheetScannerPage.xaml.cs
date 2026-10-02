using AgoraIn.Mobile.Services;
using Microsoft.Maui.Controls;

namespace AgoraIn.Mobile.Pages;

/// <summary>
/// 动态扫卡：内嵌网页端 /scan（摄像头实时取流 → 必须读到卡面右下角页码二维码 →
/// 同一页保持 2 秒自动上传）。试卷选择、多页归并、识别与考号闸门都在网页/服务端，
/// 本页只负责：要相机权限 → 承载 WebView（网页里 getUserMedia 需由 Chrome Client 授权，
/// 见 <see cref="Platforms.CameraWebViewHandler"/>）。
/// </summary>
public partial class AnswerSheetScannerPage : ContentPage
{
    private bool _loaded;

    public AnswerSheetScannerPage()
    {
        InitializeComponent();
        Web.Navigated += OnNavigated;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 网页里的 getUserMedia 最终走 Android 相机权限：先进 app 层把权限要到手
        var status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
            LoadingBox.IsVisible = false;
            ErrorBox.IsVisible = true;
            ErrorLabel.Text = "未授予相机权限，无法动态扫卡；请到系统设置里允许 AgoraIn 使用相机";
            return;
        }

        if (_loaded) return;
        _loaded = true;

        var url = $"{ApiClient.ServerBaseUrl}/scan?token={Uri.EscapeDataString(App.Api.Token)}&app=1";
        Web.Source = new UrlWebViewSource { Url = url };
    }

    private void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        // token 注入后网页会 replace 掉 query，第一跳完成即认为加载成功
        if (e.Result == WebNavigationResult.Success && ErrorBox.IsVisible)
            ErrorBox.IsVisible = false;
        if (e.Result == WebNavigationResult.Success)
            LoadingBox.IsVisible = false;
        else if (e.Result == WebNavigationResult.Failure)
        {
            LoadingBox.IsVisible = false;
            ErrorBox.IsVisible = true;
            ErrorLabel.Text = "页面加载失败，请检查网络后重试";
            _loaded = false;
        }
    }

    private async void OnRetry(object? sender, EventArgs e)
    {
        ErrorBox.IsVisible = false;
        LoadingBox.IsVisible = true;
        _loaded = false;
        OnAppearing();
        await Task.CompletedTask;
    }
}
