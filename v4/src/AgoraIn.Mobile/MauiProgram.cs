using Microsoft.Extensions.Logging;

namespace AgoraIn.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
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
