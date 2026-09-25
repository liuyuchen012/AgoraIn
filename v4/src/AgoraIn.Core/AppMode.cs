namespace AgoraIn.Core;

/// <summary>
/// 桌面端工作模式（主窗口顶部下拉框三档）。
/// </summary>
public enum AppMode
{
    /// <summary>大屏模式：课堂投影场景的学生打卡面板。</summary>
    LargeScreen,

    /// <summary>控制模式：课时划消、月历排课、集控平台。</summary>
    Control,

    /// <summary>教师模式：点名 / 积分 / 值日 / 课表 / 答题卡的聚合入口（左侧导航）。</summary>
    Teacher,
}

/// <summary>
/// <see cref="AppMode"/> 的中文显示名映射。
/// </summary>
public static class AppModeExtensions
{
    /// <summary>返回模式的中文显示名。</summary>
    public static string ToDisplayName(this AppMode mode) => mode switch
    {
        AppMode.LargeScreen => "大屏模式",
        AppMode.Control => "控制模式",
        AppMode.Teacher => "教师模式",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的桌面端工作模式"),
    };
}
