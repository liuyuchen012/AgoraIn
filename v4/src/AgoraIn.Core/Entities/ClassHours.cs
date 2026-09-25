namespace AgoraIn.Core.Entities;

/// <summary>课时流水来源。</summary>
public enum ClassHourSource
{
    /// <summary>教师手工划消/赠送。</summary>
    Manual = 0,

    /// <summary>排课结束自动扣减（带 SlotKey 幂等键）。</summary>
    AutoSchedule = 1,

    /// <summary>v3 数据迁移导入。</summary>
    Import = 2,
}

/// <summary>
/// 课时账户：一名学生的课时总账。余额 = TotalHours - UsedHours。
/// 与 v3.2 classhours.json 语义等价（划消增加 UsedHours，赠送增加 TotalHours）。
/// </summary>
public sealed class ClassHourAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>学生（全局唯一账户，一名学生一个账户）。</summary>
    public string StudentId { get; set; } = "";

    /// <summary>总课时（含赠送）。</summary>
    public double TotalHours { get; set; }

    /// <summary>已划课时数。</summary>
    public double UsedHours { get; set; }

    /// <summary>备注（v3 ChStudent.Remark 语义）。</summary>
    public string Remark { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>剩余课时（账户口径，不落库）。</summary>
    public double RemainingHours => TotalHours - UsedHours;
}

/// <summary>
/// 课时流水（正负 + 备注 + SlotKey 幂等键）。
/// 约定：<see cref="Delta"/> &gt; 0 = 增加课时（赠送，绿），&lt; 0 = 划消课时（红）。
/// </summary>
public sealed class ClassHourRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>学生。</summary>
    public string StudentId { get; set; } = "";

    /// <summary>业务日期（流水归属日）。</summary>
    public DateOnly Date { get; set; }

    /// <summary>课时变化量：正 = 增加，负 = 划消。</summary>
    public double Delta { get; set; }

    /// <summary>备注/原因。</summary>
    public string Note { get; set; } = "";

    /// <summary>
    /// 自动划消幂等键（yyyy-MM-dd|学生ID|上课时间），同一键只允许入账一次；
    /// 手工记录为 null（唯一索引仅约束非空值）。
    /// </summary>
    public string? SlotKey { get; set; }

    /// <summary>来源。</summary>
    public ClassHourSource Source { get; set; } = ClassHourSource.Manual;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 排课条目：某日给某学生排一节课（起止必填，支持跨天）。
/// 与 v3 ScheduleEntry 语义等价：下课时间 ≤ 上课时间视为跨天（顺延一天）。
/// </summary>
public sealed class CourseScheduleEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>上课日期。</summary>
    public DateOnly Date { get; set; }

    /// <summary>学生。</summary>
    public string StudentId { get; set; } = "";

    /// <summary>上课开始时间（HH:mm）。</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>下课结束时间（HH:mm）；≤ StartTime 表示跨天。</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>是否跨天（End ≤ Start 时由领域服务置位，落库避免查询时重复推导）。</summary>
    public bool CrossesMidnight { get; set; }

    /// <summary>备注。</summary>
    public string Note { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>课程结束时刻（跨天时为次日的结束时间）。</summary>
    public DateTime EndDateTime => CrossesMidnight
        ? Date.AddDays(1).ToDateTime(EndTime)
        : Date.ToDateTime(EndTime);

    /// <summary>课程开始时刻。</summary>
    public DateTime StartDateTime => Date.ToDateTime(StartTime);

    /// <summary>课程时长（小时）；无效排课为 0。</summary>
    public double DurationHours => EndDateTime > StartDateTime ? (EndDateTime - StartDateTime).TotalHours : 0;
}

/// <summary>不排课日（如节假日；当日排课视为无效/不自动扣课时）。</summary>
public sealed class ScheduleOffDay
{
    /// <summary>日期（主键）。</summary>
    public DateOnly Date { get; set; }

    /// <summary>原因（可空）。</summary>
    public string? Reason { get; set; }
}
