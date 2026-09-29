using AgoraIn.App.Services;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgoraIn.App.Views;

/// <summary>
/// 首次启动向导：班级名称、按钮网格、启动场景、主题；完成后写 AppConfig（WizardDone）。
/// </summary>
public partial class FirstRunWizard : Window
{
    public static readonly string[] Modes = ["大屏模式", "控制模式", "教师模式"];
    public static readonly string[] Themes = ["跟随系统", "明亮", "暗黑"];

    private readonly AppConfig _config;

    public FirstRunWizard(AppConfig config)
    {
        InitializeComponent();
        _config = config;

        if (!string.IsNullOrEmpty(config.ClassName))
        {
            var box = this.FindControl<TextBox>("ClassNameBox");
            if (box != null) box.Text = config.ClassName;
        }
        this.FindControl<NumericUpDown>("RowsBox")!.Value = config.ButtonRows;
        this.FindControl<NumericUpDown>("ColsBox")!.Value = config.ButtonCols;
        this.FindControl<ComboBox>("ModeBox")!.SelectedIndex = config.StartupMode;
        this.FindControl<ComboBox>("ThemeBox")!.SelectedIndex = config.Theme switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0,
        };
    }

    private void OnFinishClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _config.ClassName = this.FindControl<TextBox>("ClassNameBox")?.Text?.Trim() ?? "";
        _config.ButtonRows = (int)(this.FindControl<NumericUpDown>("RowsBox")?.Value ?? 6);
        _config.ButtonCols = (int)(this.FindControl<NumericUpDown>("ColsBox")?.Value ?? 6);
        _config.StartupMode = this.FindControl<ComboBox>("ModeBox")?.SelectedIndex ?? 0;
        _config.Theme = (this.FindControl<ComboBox>("ThemeBox")?.SelectedIndex ?? 0) switch
        {
            1 => "light",
            2 => "dark",
            _ => "system",
        };
        _config.WizardDone = true;
        _config.Save();

        App.ApplyTheme(_config.Theme);
        Close();
    }
}
