namespace AgoraIn.Core.Entities;

/// <summary>点名模式。</summary>
public enum RollCallMode
{
    /// <summary>随机点名（大屏滚动/转盘，落点可加权）。</summary>
    Random = 0,

    /// <summary>顺序点名（按学号轮流）。</summary>
    Sequential = 1,

    /// <summary>指定点名（从座位图或名单点选）。</summary>
    Assigned = 2,
}

/// <summary>点名结果。</summary>
public enum RollCallResult
{
    /// <summary>答到。</summary>
    Present = 0,

    /// <summary>缺勤（自动写入当日考勤并与打卡数据互查）。</summary>
    Absent = 1,

    /// <summary>迟到。</summary>
    Late = 2,

    /// <summary>已点未应答（等待教师标记结果）。</summary>
    Pending = 3,
}

/// <summary>点名场次：一次课可建立点名会话，结束后生成报表（出勤率、被点次数、关联积分）。</summary>
public sealed class RollCallSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>班级。</summary>
    public string ClassId { get; set; } = "";

    /// <summary>点名模式。</summary>
    public RollCallMode Mode { get; set; } = RollCallMode.Random;

    /// <summary>科目（可空，用于按课程筛选历史）。</summary>
    public string? Subject { get; set; }

    /// <summary>开始时间。</summary>
    public DateTime StartedAt { get; set; } = DateTime.Now;

    /// <summary>结束时间（null = 进行中）。</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>随机点名是否启用"近期未点优先"加权（会话级配置）。</summary>
    public bool WeightFairness { get; set; } = true;
}

/// <summary>点名记录：被点学生与结果。</summary>
public sealed class RollCallRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属场次。</summary>
    public string SessionId { get; set; } = "";

    /// <summary>被点学生。</summary>
    public string StudentId { get; set; } = "";

    /// <summary>结果。</summary>
    public RollCallResult Result { get; set; } = RollCallResult.Pending;

    /// <summary>被点时间。</summary>
    public DateTime CalledAt { get; set; } = DateTime.Now;

    /// <summary>结果标记时间（null = 尚未标记）。</summary>
    public DateTime? ResultAt { get; set; }

    /// <summary>关联积分流水 Id（答到 +X 分联动，可空）。</summary>
    public string? PointRecordId { get; set; }
}
