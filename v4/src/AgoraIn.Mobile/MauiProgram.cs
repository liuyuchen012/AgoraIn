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

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
