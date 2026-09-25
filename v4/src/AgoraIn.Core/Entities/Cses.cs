namespace AgoraIn.Core.Entities;

/// <summary>科目（CSES Subjects，可挂班级与教师名）。</summary>
public sealed class Subject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属班级（null = 全局科目库）。</summary>
    public string? ClassId { get; set; }

    /// <summary>科目名称（如"数学"）。</summary>
    public string Name { get; set; } = "";

    /// <summary>展示颜色（#RRGGBB，可空）。</summary>
    public string? Color { get; set; }

    /// <summary>教师姓名（可空）。</summary>
    public string? TeacherName { get; set; }
}

/// <summary>时间布局条目类型：节次或课间段。</summary>
public enum TimeEntryKind
{
    /// <summary>上课节次。</summary>
    Class = 0,

    /// <summary>课间/休息段。</summary>
    Break = 1,
}

/// <summary>
/// 时间布局（CSES TimeLayouts）：一组节次 + 上/下课时间（+ 可选课间段），可复制复制出模板。
/// </summary>
public sealed class TimeLayout
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属班级（null = 全局模板）。</summary>
    public string? ClassId { get; set; }

    /// <summary>布局名称（如"默认作息""冬季作息"）。</summary>
    public string Name { get; set; } = "默认作息";
}

/// <summary>时间布局条目：一节上课时段或课间段。</summary>
public sealed class TimeLayoutEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属时间布局。</summary>
    public string TimeLayoutId { get; set; } = "";

    /// <summary>节次序号（0 起，按当日顺序递增）。</summary>
    public int Index { get; set; }

    /// <summary>开始时间。</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>结束时间。</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>条目类型。</summary>
    public TimeEntryKind Kind { get; set; } = TimeEntryKind.Class;

    /// <summary>节次名称（如"早读""第一节"，可空）。</summary>
    public string? Name { get; set; }
}

/// <summary>班级课表（CSES ClassPlans）：按周几 × 节次 → 科目。</summary>
public sealed class ClassPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>班级。</summary>
    public string ClassId { get; set; } = "";

    /// <summary>使用的时间布局。</summary>
    public string TimeLayoutId { get; set; } = "";

    /// <summary>课表名称（如"2026 春季课表"）。</summary>
    public string Name { get; set; } = "课表";

    /// <summary>是否生效。</summary>
    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>课表条目：周几第几节上什么科目。</summary>
public sealed class ClassPlanEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属课表。</summary>
    public string ClassPlanId { get; set; } = "";

    /// <summary>周几（0 = 周一 … 6 = 周日）。</summary>
    public int WeekDay { get; set; }

    /// <summary>节次序号（对应时间布局条目 Index）。</summary>
    public int SlotIndex { get; set; }

    /// <summary>科目。</summary>
    public string SubjectId { get; set; } = "";
}
