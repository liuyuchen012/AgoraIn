using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AgoraIn.App.Views;

namespace AgoraIn.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 启动时应用已保存的主题（明/暗/跟随系统）
            var basePath = AppContext.BaseDirectory;
            var config = Services.AppConfig.Load(basePath);
            ApplyTheme(config.Theme);

            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>应用明暗主题；非法值回落跟随系统。</summary>
    public static void ApplyTheme(string? theme)
    {
        Application.Current!.RequestedThemeVariant = theme switch
        {
            "light" => Avalonia.Styling.ThemeVariant.Light,
            "dark" => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };
    }
}
