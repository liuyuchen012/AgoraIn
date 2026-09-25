namespace AgoraIn.Core.Entities;

/// <summary>资源类型。</summary>
public enum ResourceKind
{
    /// <summary>文件。</summary>
    File = 0,

    /// <summary>图片。</summary>
    Image = 1,

    /// <summary>视频。</summary>
    Video = 2,

    /// <summary>外部链接。</summary>
    Link = 3,
}

/// <summary>资源库：文件/图片/视频/链接，按班级/科目/标签归档，可单独下发给家长。</summary>
public sealed class Resource
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>标题。</summary>
    public string Title { get; set; } = "";

    /// <summary>类型。</summary>
    public ResourceKind Kind { get; set; } = ResourceKind.File;

    /// <summary>存储路径（IFileStorage 相对键）或外部 URL。</summary>
    public string Location { get; set; } = "";

    /// <summary>所属班级（可空 = 全局）。</summary>
    public string? ClassId { get; set; }

    /// <summary>科目（可空）。</summary>
    public string? Subject { get; set; }

    /// <summary>标签（JSON 数组，可空）。</summary>
    public string? TagsJson { get; set; }

    /// <summary>上传人。</summary>
    public string UploadedBy { get; set; } = "";

    /// <summary>是否已下发到家长端。</summary>
    public bool PublishedToParents { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>班级通知公告（可定时发布、可附件）。</summary>
public sealed class Notice
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>班级。</summary>
    public string ClassId { get; set; } = "";

    /// <summary>标题。</summary>
    public string Title { get; set; } = "";

    /// <summary>正文。</summary>
    public string Content { get; set; } = "";

    /// <summary>附件资源 Id（JSON 数组，可空）。</summary>
    public string? AttachmentsJson { get; set; }

    /// <summary>发布时间（定时发布 = 未来时刻）。</summary>
    public DateTime PublishAt { get; set; } = DateTime.Now;

    /// <summary>发布人。</summary>
    public string PublishedBy { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>通知已读回执（家长维度；未读名单 = 已绑定家长 - 回执）。</summary>
public sealed class NoticeReadReceipt
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>通知。</summary>
    public string NoticeId { get; set; } = "";

    /// <summary>家长用户标识。</summary>
    public string ParentUserId { get; set; } = "";

    /// <summary>阅读时间。</summary>
    public DateTime ReadAt { get; set; } = DateTime.Now;
}

/// <summary>消息发送方角色。</summary>
public enum MessageSenderRole
{
    /// <summary>教师。</summary>
    Teacher = 0,

    /// <summary>家长。</summary>
    Parent = 1,

    /// <summary>系统。</summary>
    System = 2,
}

/// <summary>
/// 会话消息：教师 ↔ 家长一对一留言（文本 + 图片），会话按"班级 + 学生 + 家长用户"定位。
/// 服务端发送前做敏感词过滤，命中时置 <see cref="Flagged"/>（管理员审核开关控制放行）。
/// </summary>
public sealed class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>班级。</summary>
    public string ClassId { get; set; } = "";

    /// <summary>关联学生（会话主题，可空）。</summary>
    public string? StudentId { get; set; }

    /// <summary>家长用户标识（会话另一方）。</summary>
    public string ParentUserId { get; set; } = "";

    /// <summary>发送方角色。</summary>
    public MessageSenderRole SenderRole { get; set; } = MessageSenderRole.Teacher;

    /// <summary>发送人名称。</summary>
    public string SenderName { get; set; } = "";

    /// <summary>内容（文本或图片存储键）。</summary>
    public string Content { get; set; } = "";

    /// <summary>是否图片。</summary>
    public bool IsImage { get; set; }

    /// <summary>是否被敏感词过滤标记（待审核）。</summary>
    public bool Flagged { get; set; }

    /// <summary>发送时间。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>家长绑定状态。</summary>
public enum ParentBindingStatus
{
    /// <summary>已生成邀请码待扫码。</summary>
    Pending = 0,

    /// <summary>已绑定。</summary>
    Bound = 1,

    /// <summary>已解绑（保留审计记录）。</summary>
    Unbound = 2,
}

/// <summary>
/// 家长-学生绑定：教师生成邀请码/二维码，家长扫码绑定；
/// 解绑与换绑保留记录（审计）。一名学生可绑定多名家长。
/// </summary>
public sealed class ParentBinding
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>学生。</summary>
    public string StudentId { get; set; } = "";

    /// <summary>绑定邀请码。</summary>
    public string InviteCode { get; set; } = "";

    /// <summary>家长用户标识（绑定后回填）。</summary>
    public string? ParentUserId { get; set; }

    /// <summary>家长备注称呼（如"张三爸爸"）。</summary>
    public string? ParentAlias { get; set; }

    /// <summary>状态。</summary>
    public ParentBindingStatus Status { get; set; } = ParentBindingStatus.Pending;

    /// <summary>绑定时间。</summary>
    public DateTime? BoundAt { get; set; }

    /// <summary>解绑时间。</summary>
    public DateTime? UnboundAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
