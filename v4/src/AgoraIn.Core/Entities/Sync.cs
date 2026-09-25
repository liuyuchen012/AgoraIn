namespace AgoraIn.Core.Entities;

/// <summary>同步方向。</summary>
public enum SyncDirection
{
    /// <summary>本地 → 服务器。</summary>
    Push = 0,

    /// <summary>服务器 → 本地。</summary>
    Pull = 1,
}

/// <summary>同步结果。</summary>
public enum SyncResult
{
    /// <summary>成功。</summary>
    Ok = 0,

    /// <summary>冲突（按冲突策略处理）。</summary>
    Conflict = 1,

    /// <summary>失败。</summary>
    Failed = 2,
}

/// <summary>冲突裁定策略。</summary>
public enum ConflictStrategy
{
    /// <summary>服务器时间戳裁定（默认）。</summary>
    ServerTimestampWins = 0,

    /// <summary>教师端最终确认（冲突入队人工裁决）。</summary>
    TeacherConfirm = 1,
}

/// <summary>
/// 同步日志：实体级时间戳 + 变更集拉取的执行留痕。
/// 服务器不可达时同步引擎静默降级并记录 Failed 日志。
/// </summary>
public sealed class SyncLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>实体名（如 Student / CheckInRecord）。</summary>
    public string EntityName { get; set; } = "";

    /// <summary>实体 Id。</summary>
    public string EntityId { get; set; } = "";

    /// <summary>方向。</summary>
    public SyncDirection Direction { get; set; }

    /// <summary>本地时间戳。</summary>
    public DateTime LocalTimestamp { get; set; } = DateTime.Now;

    /// <summary>服务器时间戳（可空）。</summary>
    public DateTime? ServerTimestamp { get; set; }

    /// <summary>结果。</summary>
    public SyncResult Result { get; set; }

    /// <summary>详情（冲突双方快照摘要/失败原因）。</summary>
    public string Detail { get; set; } = "";
}

/// <summary>冲突策略：按实体名配置裁定方式（默认服务器时间戳裁定 + 教师端最终确认）。</summary>
public sealed class ConflictPolicy
{
    /// <summary>实体名（主键语义）。</summary>
    public string EntityName { get; set; } = "";

    /// <summary>裁定策略。</summary>
    public ConflictStrategy Strategy { get; set; } = ConflictStrategy.ServerTimestampWins;

    /// <summary>更新时间。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>键值应用设置（键为语义名，值为 JSON 文本；如全局服务器配置、迁移标记）。</summary>
public sealed class AppSetting
{
    /// <summary>设置键（主键）。</summary>
    public string Key { get; set; } = "";

    /// <summary>JSON 值。</summary>
    public string Value { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
