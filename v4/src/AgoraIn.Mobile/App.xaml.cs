using AgoraIn.Mobile.Services;

namespace AgoraIn.Mobile;

public partial class App : Application
{
    /// <summary>全局 API 客户端（JWT 与服务器地址本地持久化）。</summary>
    public static ApiClient Api { get; } = new();

    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
