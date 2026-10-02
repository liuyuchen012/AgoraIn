namespace AgoraIn.Mobile.Pages;

/// <summary>
/// 逐题批改的 WebView 容器：直接加载网页端独立路由 /grading/{id}?token=，
/// 与电脑端同一套界面（参考答案 / 本题切图 / 快捷记分 / 全屏）。
/// 返回键走系统返回（MAUI Shell 自动处理 Navigation 返回栈）。
/// </summary>
public partial class GradingWebViewPage : ContentPage
{
    public GradingWebViewPage(string url)
    {
        InitializeComponent();
        Browser.Source = new UrlWebViewSource { Url = url };
    }
}
