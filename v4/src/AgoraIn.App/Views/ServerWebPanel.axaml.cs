using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgoraIn.App.Views;

/// <summary>
/// 集控平台 Web 面板：启动系统默认浏览器打开服务端管理页面。
/// （Avalonia WebView2 需要额外 CEF 依赖，优先用系统浏览器方案）
/// </summary>
public partial class ServerWebPanel : Window
{
    public ServerWebPanel()
    {
        InitializeComponent();
    }

    private void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "http://" + url;
        OpenBrowser(url);
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        OnLoadClick(sender, e);
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch { }
    }
}
