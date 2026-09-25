namespace AgoraIn.Core.Entities;

/// <summary>值日轮换周期。</summary>
public enum DutyCycle
{
    /// <summary>按周轮换。</summary>
    Weekly = 0,

    /// <summary>按日轮换。</summary>
    Daily = 1,
}

/// <summary>值日岗位：名称、图标、人数。</summary>
public sealed class DutyPost
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>班级。</summary>
    public string ClassId { get; set; } = "";

    /// <summary>岗位名称（扫地、擦黑板、倒垃圾…）。</summary>
    public string Name { get; set; } = "";

    /// <summary>图标（emoji 或图标名，可空）。</summary>
    public string? Icon { get; set; }

    /// <summary>该岗位每期人数（默认 1）。</summary>
    public int Capacity { get; set; } = 1;

    /// <summary>展示排序。</summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// 值日表：班级的轮换规则（按周/按日）与轮换顺序（生成后可手工微调）。
/// </summary>
public sealed class DutyRoster
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>班级。</summary>
    public string ClassId { get; set; } = "";

    /// <summary>轮换周期。</summary>
    public DutyCycle Cycle { get; set; } = DutyCycle.Weekly;

    /// <summary>生效起始日（轮换指针的基准日）。</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>是否生效。</summary>
    public bool Active { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 值日表条目：岗位 × 期次 → 学生。期次序号由轮换周期推进（按周/按日），
/// 领域服务按轮换顺序生成后允许手工微调（直接改 StudentId）。
/// </summary>
public sealed class DutyRosterEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属值日表。</summary>
    public string RosterId { get; set; } = "";

    /// <summary>岗位。</summary>
    public string PostId { get; set; } = "";

    /// <summary>岗位内座位序号（一个岗位多人时 0..Capacity-1）。</summary>
    public int SlotIndex { get; set; }

    /// <summary>期次序号（0 = 起始期）。</summary>
    public int PeriodIndex { get; set; }

    /// <summary>该期该岗位的值日学生。</summary>
    public string StudentId { get; set; } = "";
}

/// <summary>值日完成记录：完成情况 + 未完成扣分联动。</summary>
public sealed class DutyRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>班级。</summary>
    public string ClassId { get; set; } = "";

    /// <summary>岗位。</summary>
    public string PostId { get; set; } = "";

    /// <summary>值日学生。</summary>
    public string StudentId { get; set; } = "";

    /// <summary>值日日期。</summary>
    public DateOnly Date { get; set; }

    /// <summary>是否完成。</summary>
    public bool Completed { get; set; }

    /// <summary>备注。</summary>
    public string Note { get; set; } = "";

    /// <summary>关联积分流水 Id（完成加分/未完成扣分联动，可空）。</summary>
    public string? PointRecordId { get; set; }

    /// <summary>记录时间。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
