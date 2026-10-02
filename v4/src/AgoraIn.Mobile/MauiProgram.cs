using Microsoft.Extensions.Logging;
using ZXing.Net.Maui.Controls;

namespace AgoraIn.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseBarcodeReader() // ZXing.Net.MAUI 真机扫码
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// 注册服务
		builder.Services.AddSingleton<Services.ApiClient>();

		// 动态扫卡页的 WebView 要让网页用摄像头（getUserMedia）：
		// Android 上由自定义 Handler 的 Chrome Client 授权；其余平台用系统默认。
		builder.ConfigureMauiHandlers(handlers =>
		{
#if ANDROID
			handlers.AddHandler<Controls.CameraWebView, Controls.CameraWebViewHandler>();
#else
			handlers.AddHandler<Controls.CameraWebView, Microsoft.Maui.Handlers.WebViewHandler>();
#endif
		});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
