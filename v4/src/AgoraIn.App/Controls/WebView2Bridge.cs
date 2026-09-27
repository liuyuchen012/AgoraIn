using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;

namespace AgoraIn.App.Controls;

/// <summary>
/// WebView2 桥接：只在本类的方法体内触碰 WebView2 类型，字段一律用 <c>object</c> 保存，
/// 以保证非 Windows 平台加载 <see cref="WebViewHost"/> 类型时不会因缺少 WebView2 程序集而失败。
///
/// 线程约定：构造与所有公开方法都必须在 Avalonia UI 线程调用（该线程有消息泵，
/// 且 <c>await</c> 续体会因 SynchronizationContext 回到 UI 线程，满足 WebView2 的线程亲和性要求）。
/// </summary>
internal sealed class WebView2Bridge : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly string _dataDir;

    // 用 object 保存，避免类型加载依赖 WebView2 程序集
    private object? _controller;   // CoreWebView2Controller
    private object? _webView;      // CoreWebView2

    private string? _pendingUrl;
    private string? _lastUrl;
    private double _scale = 1.0;
    private int _width;
    private int _height;
    private bool _disposed;

    /// <summary>页面加载失败（网络不可达 / 服务器未部署等）时触发，参数为说明文字。</summary>
    public event EventHandler<string>? NavigationFailed;

    internal WebView2Bridge(IntPtr hwnd, string dataDir)
    {
        _hwnd = hwnd;
        _dataDir = dataDir;
        _ = InitAsync();
    }

    /// <summary>导航到指定地址（初始化完成前先暂存）。</summary>
    public void Navigate(string url)
    {
        if (_disposed) return;

        _lastUrl = url;

        if (_webView is CoreWebView2 webView)
        {
            webView.Navigate(url);
        }
        else
        {
            _pendingUrl = url;
        }
    }

    /// <summary>同步控件尺寸到 WebView2（物理像素，需乘以缩放比例）。</summary>
    public void Resize(double width, double height, double scale)
    {
        if (_disposed) return;

        _width = (int)Math.Max(0, width * scale);
        _height = (int)Math.Max(0, height * scale);
        _scale = scale;

        ApplyBounds();
    }

    private void ApplyBounds()
    {
        if (_controller is not CoreWebView2Controller controller) return;
        if (_width <= 0 || _height <= 0) return;

        controller.RasterizationScale = _scale;
        controller.Bounds = new System.Drawing.Rectangle(0, 0, _width, _height);
    }

    private async Task InitAsync()
    {
        try
        {
            Directory.CreateDirectory(_dataDir);

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _dataDir,
                options: null);

            var controller = await environment.CreateCoreWebView2ControllerAsync(_hwnd);
            if (_disposed)
            {
                controller.Close();
                return;
            }

            _controller = controller;

            // 禁用不必要的浏览器特性（无右键菜单/无状态栏提示），保持与桌面端一致的观感
            var settings = controller.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;

            _webView = controller.CoreWebView2;

            // 加载失败（服务器不可达 / 页面错误）时通知上层显示降级提示
            controller.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess)
                {
                    NavigationFailed?.Invoke(this,
                        $"无法加载服务端页面（{e.WebErrorStatus}）：{_lastUrl}\n" +
                        "请确认服务器已部署且网络可访问，或改用系统浏览器打开。");
                }
            };

            ApplyBounds();

            if (!string.IsNullOrEmpty(_pendingUrl))
            {
                controller.CoreWebView2.Navigate(_pendingUrl);
                _pendingUrl = null;
            }
        }
        catch (Exception ex)
        {
            // WebView2 运行时缺失或初始化失败：抛出给上层显示降级 UI
            throw new InvalidOperationException(
                $"WebView2 初始化失败：{ex.Message}（请确认已安装 Microsoft Edge WebView2 运行时）", ex);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_controller is CoreWebView2Controller controller)
            {
                controller.Close();
            }
        }
        catch
        {
            // 窗口销毁过程中的关闭异常可忽略
        }

        _controller = null;
        _webView = null;
    }
}
