namespace AgoraIn.Core.Entities;

/// <summary>讲台方位。</summary>
public enum PodiumPosition
{
    /// <summary>上方（默认，大屏视角远端）。</summary>
    Top = 0,

    /// <summary>下方（讲台靠近大屏）。</summary>
    Bottom = 1,

    /// <summary>左侧。</summary>
    Left = 2,

    /// <summary>右侧。</summary>
    Right = 3,
}

/// <summary>
/// 座位图：教室布局（行列、讲台方位、分组），一名班级可存多套布局（日常/考试）与历史快照。
/// </summary>
public sealed class SeatChart
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属班级。</summary>
    public string ClassId { get; set; } = "";

    /// <summary>布局名称（如"日常布局"/"考试布局"）。</summary>
    public string Name { get; set; } = "日常布局";

    /// <summary>行数。</summary>
    public int Rows { get; set; } = 6;

    /// <summary>列数。</summary>
    public int Cols { get; set; } = 6;

    /// <summary>讲台方位。</summary>
    public PodiumPosition Podium { get; set; } = PodiumPosition.Top;

    /// <summary>是否为该班级当前生效布局。</summary>
    public bool IsActive { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>座位：布局中的一个格位，可绑定学生（可为空表示空位）。</summary>
public sealed class Seat
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属布局。</summary>
    public string ChartId { get; set; } = "";

    /// <summary>行号（0 起）。</summary>
    public int Row { get; set; }

    /// <summary>列号（0 起）。</summary>
    public int Col { get; set; }

    /// <summary>是否禁用（空位/过道）。</summary>
    public bool Disabled { get; set; }

    /// <summary>分组名（小组/大列，可空）。</summary>
    public string? GroupName { get; set; }

    /// <summary>就座学生（null = 空位）。</summary>
    public string? StudentId { get; set; }
}
