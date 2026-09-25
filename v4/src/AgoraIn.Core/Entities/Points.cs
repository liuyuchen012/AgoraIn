namespace AgoraIn.Core.Entities;

/// <summary>积分来源事件类型。</summary>
public enum PointSource
{
    /// <summary>教师手工加/减分。</summary>
    Manual = 0,

    /// <summary>打卡联动。</summary>
    CheckIn = 1,

    /// <summary>点名联动（答到/缺勤/迟到）。</summary>
    RollCall = 2,

    /// <summary>值日完成联动。</summary>
    Duty = 3,

    /// <summary>答题卡正确率联动。</summary>
    AnswerSheet = 4,

    /// <summary>系统修正（需填原因并留痕）。</summary>
    Correction = 5,
}

/// <summary>
/// 积分规则：名称、分值（正负）、每日上限（可选）、适用班级（null = 全部班级）。
/// </summary>
public sealed class PointRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>规则名称（如"课堂发言""作业优秀"）。</summary>
    public string Name { get; set; } = "";

    /// <summary>默认分值（正 = 加分，负 = 扣分）。</summary>
    public int DefaultDelta { get; set; } = 1;

    /// <summary>每日上限（按学生按规则按日；null = 不限）。</summary>
    public int? DailyCap { get; set; }

    /// <summary>适用班级（null = 全部班级通用）。</summary>
    public string? ClassId { get; set; }

    /// <summary>是否启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>是否置顶显示（最近使用的规则置顶由运行时统计，这里是教师手工置顶）。</summary>
    public bool Pinned { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 积分流水：学生、规则、分值、操作人、时间、来源事件（可关联点名/答题卡/值日）。
/// 教师修正流水须以 <see cref="PointSource.Correction"/> 记录并填写原因。
/// </summary>
public sealed class PointRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>学生。</summary>
    public string StudentId { get; set; } = "";

    /// <summary>规则（手工/修正流水可为 null）。</summary>
    public string? RuleId { get; set; }

    /// <summary>分值变化（正 = 加分，负 = 扣分）。</summary>
    public int Delta { get; set; }

    /// <summary>原因/评语（修正流水必填）。</summary>
    public string Reason { get; set; } = "";

    /// <summary>操作人（教师名或系统）。</summary>
    public string OperatorName { get; set; } = "";

    /// <summary>来源事件。</summary>
    public PointSource Source { get; set; } = PointSource.Manual;

    /// <summary>关联事件 Id（点名记录/打卡记录/值日记录等，可空）。</summary>
    public string? SourceId { get; set; }

    /// <summary>发生时间。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
