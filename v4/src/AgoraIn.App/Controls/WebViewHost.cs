using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace AgoraIn.App.Controls;

/// <summary>
/// 内嵌服务端网页控件：Windows 上使用 WebView2（Win10/11 自带 Edge 运行时），
/// 其他平台不支持内嵌（<see cref="IsSupported"/> 为 false，由调用方给出降级 UI）。
///
/// 实现要点：以 <see cref="NativeControlHost"/> 提供子窗口句柄，交给 WebView2 控制器渲染；
/// 尺寸随控件变化同步到控制器 Bounds（WebView2 不会自动跟随父窗口缩放）。
/// </summary>
public sealed class WebViewHost : NativeControlHost
{
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<WebViewHost, string?>(nameof(Source));

    /// <summary>要加载的网页地址。</summary>
    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>是否支持内嵌（仅 Windows + 已安装 WebView2 运行时）。</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>加载失败信息（供 UI 显示降级提示）。</summary>
    public string? LastError { get; private set; }

    /// <summary>加载失败时触发（参数为错误说明）。</summary>
    public event EventHandler<string>? LoadFailed;

    private WebView2Bridge? _bridge;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        if (!IsSupported) return handle;

        try
        {
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgoraIn", "WebView2");
            _bridge = new WebView2Bridge(handle.Handle, dataDir);
            _bridge.NavigationFailed += (_, message) => LoadFailed?.Invoke(this, message);

            var topLevel = TopLevel.GetTopLevel(this);
            var scale = topLevel?.RenderScaling ?? 1.0;
            _bridge.Resize(Bounds.Width, Bounds.Height, scale);
            _bridge.Navigate(Source ?? Core.AppConstants.WebAdminUrl);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LoadFailed?.Invoke(this, ex.Message);
        }

        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _bridge?.Dispose();
        _bridge = null;
        base.DestroyNativeControlCore(control);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty && change.NewValue is string url && url.Length > 0)
        {
            _bridge?.Navigate(url);
        }
        else if (change.Property == BoundsProperty)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var scale = topLevel?.RenderScaling ?? 1.0;
            _bridge?.Resize(Bounds.Width, Bounds.Height, scale);
        }
    }
}
