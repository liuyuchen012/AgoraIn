using AgoraIn.Mobile.Services;

namespace AgoraIn.Mobile;

public partial class App : Application
{
    /// <summary>全局 API 客户端（JWT 与服务器地址本地持久化）。</summary>
    public static ApiClient Api { get; } = new();

    public App()
    {
        InitializeComponent();

        // 全局异常处理：捕获未处理异常防止闪退
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            System.Diagnostics.Debug.WriteLine($"[FATAL] {ex?.Message}\n{ex?.StackTrace}");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"[Task] {e.Exception?.Message}");
            e.SetObserved();
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
