using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Handlers;

namespace AgoraIn.Mobile.Controls
{
    /// <summary>
    /// 允许网页调用摄像头的 WebView（动态扫卡页用）：
    /// Android 上 getUserMedia 需要 WebChromeClient.OnPermissionRequest 显式授予，
    /// MAUI 默认的 Chrome Client 不授予 —— 网页会一直拿不到摄像头。
    /// 其他平台用系统默认 Handler（iOS/WKWebView 自行管理相机权限）。
    /// </summary>
    public class CameraWebView : WebView
    {
        public static readonly BindableProperty SkipFileFallbackProperty =
            BindableProperty.Create(nameof(SkipFileFallback), typeof(bool), typeof(CameraWebView), false);

        /// <summary>App 内嵌时置 true：隐藏网页里依赖文件选择器的"拍照上传"兜底（Chrome Client 未实现文件选择）。</summary>
        public bool SkipFileFallback
        {
            get => (bool)GetValue(SkipFileFallbackProperty);
            set => SetValue(SkipFileFallbackProperty, value);
        }
    }

#if ANDROID
    public class CameraWebViewHandler : WebViewHandler
    {
        protected override void ConnectHandler(Android.Webkit.WebView platformView)
        {
            base.ConnectHandler(platformView);
            platformView.SetWebChromeClient(new GrantClient());
        }

        protected override void DisconnectHandler(Android.Webkit.WebView platformView)
        {
            platformView.SetWebChromeClient(new Android.Webkit.WebChromeClient());
            base.DisconnectHandler(platformView);
        }

        /// <summary>授予网页请求的摄像头/麦克风权限（动态扫卡只要 CAMERA）。</summary>
        private sealed class GrantClient : Android.Webkit.WebChromeClient
        {
            public override void OnPermissionRequest(Android.Webkit.PermissionRequest request)
            {
                MainThread.BeginInvokeOnMainThread(() => request.Grant(request.GetResources()));
            }
        }
    }
#endif
}
