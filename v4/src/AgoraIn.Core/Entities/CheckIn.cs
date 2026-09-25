namespace AgoraIn.Core.Entities;

/// <summary>任务树节点类型（文件夹 / 任务两级）。</summary>
public enum CheckInTaskKind
{
    /// <summary>任务（可打卡）。</summary>
    Task = 0,

    /// <summary>文件夹（仅分组）。</summary>
    Folder = 1,
}

/// <summary>打卡来源。</summary>
public enum CheckInSource
{
    /// <summary>大屏点击。</summary>
    Click = 0,

    /// <summary>扫码签到。</summary>
    Scan = 1,

    /// <summary>网页端。</summary>
    Web = 2,

    /// <summary>v3 数据迁移导入。</summary>
    Import = 3,
}

/// <summary>
/// 打卡任务（任务树：文件夹/任务两级，文件夹可嵌套任务）。
/// 与 v3.2 标签页（data/tabs/&lt;id&gt;/）语义等价：任务名、科目、按钮行列、签到任务标记。
/// </summary>
public sealed class CheckInTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>父文件夹任务 Id（根节点为 null）。</summary>
    public string? ParentId { get; set; }

    /// <summary>节点类型。</summary>
    public CheckInTaskKind Kind { get; set; } = CheckInTaskKind.Task;

    /// <summary>显示名称（如"1203 数学 签到 - 数学"）。</summary>
    public string Name { get; set; } = "";

    /// <summary>科目（v3 TabConfig.Km）。</summary>
    public string Subject { get; set; } = "";

    /// <summary>学生按钮网格行数。</summary>
    public int ButtonRows { get; set; } = 6;

    /// <summary>学生按钮网格列数。</summary>
    public int ButtonCols { get; set; } = 6;

    /// <summary>是否为二维码签到任务。</summary>
    public bool IsSignInTask { get; set; }

    /// <summary>签到任务的服务器 TaskId（如 signin_abc123）。</summary>
    public string? SignInTaskId { get; set; }

    /// <summary>树中排序号。</summary>
    public int SortOrder { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>任务名单：任务与学生的关联（v3 name.txt 语义，含展示排序）。</summary>
public sealed class TaskRosterEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>任务。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>学生。</summary>
    public string StudentId { get; set; } = "";

    /// <summary>展示排序（按钮网格顺序）。</summary>
    public int SortOrder { get; set; }
}

/// <summary>打卡记录（时间 + 来源）。</summary>
public sealed class CheckInRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>任务。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>学生。</summary>
    public string StudentId { get; set; } = "";

    /// <summary>打卡时间。</summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>来源。</summary>
    public CheckInSource Source { get; set; } = CheckInSource.Click;

    /// <summary>备注（迁移溯源等）。</summary>
    public string Note { get; set; } = "";
}
