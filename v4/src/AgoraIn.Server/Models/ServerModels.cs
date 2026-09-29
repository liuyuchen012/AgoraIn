using Microsoft.EntityFrameworkCore;
using AgoraIn.Server.Models;

namespace AgoraIn.Server;
/// <summary>
/// 服务端数据库上下文（SQLite）。
/// 与桌面端 Core 实体共享，服务端额外维护 User、Device 实体。
///
/// **多区域数据隔离**：教学数据实体通过影子属性 "RegionId"（不污染 Core 实体、不影响桌面端本地库）
/// 按区域隔离——全局查询过滤器按 <see cref="CurrentRegion"/>（来自 JWT region claim）过滤读取，
/// SaveChanges 对新增实体自动回填当前区域。历史数据由 DbSchemaPatch 回填为 manager。
/// </summary>
public sealed class ServerDbContext : DbContext
{
    public ServerDbContext(DbContextOptions<ServerDbContext> options) : base(options) { }

    /// <summary>
    /// 当前请求的区域标识（实例属性：EF 查询过滤器按执行时值参数化，避免静态属性被查询计划缓存固化）。
    /// </summary>
    public string CurrentRegion => AgoraIn.Server.Security.RegionContext.Current;

    /// <summary>
    /// 需要按区域隔离的实体类型（影子属性 RegionId）。
    /// 仅含服务端已映射的实体；不含 User（手动过滤）、Device、Region、AiCallLog、CallEntity、
    /// License、SmtpSettings、EmailCode、AppSetting（全局数据）。
    /// </summary>
    private static readonly Type[] RegionScopedEntities =
    [
        typeof(AgoraIn.Core.Entities.ClassInfo),
        typeof(AgoraIn.Core.Entities.Student),
        typeof(AgoraIn.Core.Entities.CheckInTask),
        typeof(AgoraIn.Core.Entities.TaskRosterEntry),
        typeof(AgoraIn.Core.Entities.CheckInRecord),
        typeof(AgoraIn.Core.Entities.ClassHourAccount),
        typeof(AgoraIn.Core.Entities.ClassHourRecord),
        typeof(AgoraIn.Core.Entities.PointRule),
        typeof(AgoraIn.Core.Entities.PointRecord),
        typeof(AgoraIn.Core.Entities.DutyPost),
        typeof(AgoraIn.Core.Entities.DutyRecord),
        typeof(AgoraIn.Core.Entities.RollCallSession),
        typeof(AgoraIn.Core.Entities.RollCallRecord),
        typeof(AgoraIn.Core.Entities.SeatChart),
        typeof(AgoraIn.Core.Entities.Seat),
        typeof(AgoraIn.Core.Entities.SignInCode),
        typeof(AgoraIn.Core.Entities.Notice),
        typeof(AgoraIn.Core.Entities.NoticeReadReceipt),
        typeof(AgoraIn.Core.Entities.Resource),
        typeof(AgoraIn.Core.Entities.Message),
        typeof(AgoraIn.Core.Entities.ParentBinding),
        typeof(AgoraIn.Core.Entities.ExamPaper),
        typeof(AgoraIn.Core.Entities.Question),
        typeof(AgoraIn.Core.Entities.AnswerSheetSubmission),
        typeof(AgoraIn.Core.Entities.QuestionResult),
        typeof(AgoraIn.Core.Entities.Subject),
        typeof(AgoraIn.Core.Entities.TimeLayout),
        typeof(AgoraIn.Core.Entities.TimeLayoutEntry),
        typeof(AgoraIn.Core.Entities.ClassPlan),
        typeof(AgoraIn.Core.Entities.ClassPlanEntry),
    ];

    /// <summary>指定实体是否按区域隔离。</summary>
    public static bool IsRegionScoped(Type entityType)
        => Array.IndexOf(RegionScopedEntities, entityType) >= 0;

    /// <summary>隔离实体的表名（供 DbSchemaPatch 生成补列 SQL；须在模型定稿后访问）。</summary>
    public IReadOnlyList<string> RegionScopedTableNames => RegionScopedEntities
        .Select(t => Model.FindEntityType(t)?.GetTableName())
        .Where(n => n != null)
        .Select(n => n!)
        .ToList();

    // ── 用户与设备 ──
    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Region> Regions => Set<Region>();

    // ── 答题卡与 AI 阅卷（复用 Core 实体） ──
    public DbSet<AgoraIn.Core.Entities.ExamPaper> ExamPapers => Set<AgoraIn.Core.Entities.ExamPaper>();
    public DbSet<AgoraIn.Core.Entities.Question> Questions => Set<AgoraIn.Core.Entities.Question>();
    public DbSet<AgoraIn.Core.Entities.AnswerSheetSubmission> AnswerSheetSubmissions => Set<AgoraIn.Core.Entities.AnswerSheetSubmission>();
    public DbSet<AgoraIn.Core.Entities.QuestionResult> QuestionResults => Set<AgoraIn.Core.Entities.QuestionResult>();

    // ── 共享 Core 实体（复用 AgoraIn.Core.Entities） ──
    public DbSet<AgoraIn.Core.Entities.ClassInfo> Classes => Set<AgoraIn.Core.Entities.ClassInfo>();
    public DbSet<AgoraIn.Core.Entities.Student> Students => Set<AgoraIn.Core.Entities.Student>();
    public DbSet<AgoraIn.Core.Entities.CheckInTask> CheckInTasks => Set<AgoraIn.Core.Entities.CheckInTask>();
    public DbSet<AgoraIn.Core.Entities.TaskRosterEntry> TaskRosterEntries => Set<AgoraIn.Core.Entities.TaskRosterEntry>();
    public DbSet<AgoraIn.Core.Entities.CheckInRecord> CheckInRecords => Set<AgoraIn.Core.Entities.CheckInRecord>();
    public DbSet<AgoraIn.Core.Entities.ClassHourAccount> ClassHourAccounts => Set<AgoraIn.Core.Entities.ClassHourAccount>();
    public DbSet<AgoraIn.Core.Entities.ClassHourRecord> ClassHourRecords => Set<AgoraIn.Core.Entities.ClassHourRecord>();

    // ── 家长端相关（复用 Core 实体） ──
    public DbSet<AgoraIn.Core.Entities.ParentBinding> ParentBindings => Set<AgoraIn.Core.Entities.ParentBinding>();
    public DbSet<AgoraIn.Core.Entities.PointRecord> PointRecords => Set<AgoraIn.Core.Entities.PointRecord>();
    public DbSet<AgoraIn.Core.Entities.Notice> Notices => Set<AgoraIn.Core.Entities.Notice>();
    public DbSet<AgoraIn.Core.Entities.NoticeReadReceipt> NoticeReadReceipts => Set<AgoraIn.Core.Entities.NoticeReadReceipt>();
    public DbSet<AgoraIn.Core.Entities.Message> Messages => Set<AgoraIn.Core.Entities.Message>();
    public DbSet<AgoraIn.Core.Entities.AppSetting> AppSettings => Set<AgoraIn.Core.Entities.AppSetting>();

    // ── 积分 / 值日 / 资源库 ──
    public DbSet<AgoraIn.Core.Entities.PointRule> PointRules => Set<AgoraIn.Core.Entities.PointRule>();
    public DbSet<AgoraIn.Core.Entities.DutyPost> DutyPosts => Set<AgoraIn.Core.Entities.DutyPost>();
    public DbSet<AgoraIn.Core.Entities.DutyRecord> DutyRecords => Set<AgoraIn.Core.Entities.DutyRecord>();
    public DbSet<AgoraIn.Core.Entities.Resource> Resources => Set<AgoraIn.Core.Entities.Resource>();

    // ── CSES 课表 ──
    public DbSet<AgoraIn.Core.Entities.Subject> Subjects => Set<AgoraIn.Core.Entities.Subject>();
    public DbSet<AgoraIn.Core.Entities.TimeLayout> TimeLayouts => Set<AgoraIn.Core.Entities.TimeLayout>();
    public DbSet<AgoraIn.Core.Entities.TimeLayoutEntry> TimeLayoutEntries => Set<AgoraIn.Core.Entities.TimeLayoutEntry>();
    public DbSet<AgoraIn.Core.Entities.ClassPlan> ClassPlans => Set<AgoraIn.Core.Entities.ClassPlan>();
    public DbSet<AgoraIn.Core.Entities.ClassPlanEntry> ClassPlanEntries => Set<AgoraIn.Core.Entities.ClassPlanEntry>();

    // ── 座位 / 点名 / 扫码签到（复用 Core 实体） ──
    public DbSet<AgoraIn.Core.Entities.SeatChart> SeatCharts => Set<AgoraIn.Core.Entities.SeatChart>();
    public DbSet<AgoraIn.Core.Entities.Seat> Seats => Set<AgoraIn.Core.Entities.Seat>();
    public DbSet<AgoraIn.Core.Entities.RollCallSession> RollCallSessions => Set<AgoraIn.Core.Entities.RollCallSession>();
    public DbSet<AgoraIn.Core.Entities.RollCallRecord> RollCallRecords => Set<AgoraIn.Core.Entities.RollCallRecord>();
    public DbSet<AgoraIn.Core.Entities.SignInCode> SignInCodes => Set<AgoraIn.Core.Entities.SignInCode>();

    // ── AI 调用日志（Token 消耗可查） ──
    public DbSet<AiCallLogEntity> AiCallLogs => Set<AiCallLogEntity>();

    // ── ClassIsland 呼叫（兼容层） ──
    public DbSet<CallEntity> Calls => Set<CallEntity>();

    // ── 离线授权（单行表） ──
    public DbSet<LicenseEntity> License => Set<LicenseEntity>();

    // ── 邮件服务与邮箱验证码 ──
    public DbSet<Services.SmtpSettings> SmtpSettings => Set<Services.SmtpSettings>();
    public DbSet<EmailCodeEntity> EmailCodes => Set<EmailCodeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 多区域：用户名在区域内唯一（登录格式 用户名@区域Id，用户名不得含 @）；区域代号与名称均全局唯一
        modelBuilder.Entity<Device>().HasIndex(d => d.DeviceUuid).IsUnique();
        modelBuilder.Entity<User>().HasIndex(u => new { u.Username, u.RegionId }).IsUnique();
        modelBuilder.Entity<Region>().HasIndex(r => r.RegionId).IsUnique();
        modelBuilder.Entity<Region>().HasIndex(r => r.Name).IsUnique();

        // 邮箱验证码：按邮箱+用途+时间查询，并支持过期清理
        modelBuilder.Entity<EmailCodeEntity>().HasIndex(c => new { c.Email, c.Purpose, c.CreatedAt });

        modelBuilder.Entity<AgoraIn.Core.Entities.ParentBinding>()
            .HasIndex(b => b.InviteCode).IsUnique();
        modelBuilder.Entity<AgoraIn.Core.Entities.NoticeReadReceipt>()
            .HasIndex(r => new { r.NoticeId, r.ParentUserId }).IsUnique();
        modelBuilder.Entity<AgoraIn.Core.Entities.AppSetting>()
            .HasKey(s => s.Key);

        // 座位 / 点名 / 签到码 / AI 日志的查询索引
        modelBuilder.Entity<AgoraIn.Core.Entities.SeatChart>().HasIndex(c => c.ClassId);
        modelBuilder.Entity<AgoraIn.Core.Entities.Seat>().HasIndex(s => s.ChartId);
        modelBuilder.Entity<AgoraIn.Core.Entities.RollCallSession>().HasIndex(s => new { s.ClassId, s.StartedAt });
        modelBuilder.Entity<AgoraIn.Core.Entities.RollCallRecord>().HasIndex(r => r.SessionId);
        modelBuilder.Entity<AgoraIn.Core.Entities.SignInCode>().HasIndex(c => c.Code);
        modelBuilder.Entity<AgoraIn.Core.Entities.TaskRosterEntry>().HasIndex(r => r.TaskId);
        modelBuilder.Entity<AiCallLogEntity>().HasIndex(l => l.CreatedAt);

        // ── 多区域隔离：影子属性 RegionId + 全局查询过滤器（读取按区域过滤） ──
        // 过滤器表达式手工构建（等价于内联写法 e => EF.Property<string>(e,"RegionId") == CurrentRegion），
        // CurrentRegion 以 this 常量为根的成员访问——EF 按上下文实例逐查询求值，不会被查询计划缓存固化
        foreach (var entityType in RegionScopedEntities)
        {
            var builder = modelBuilder.Entity(entityType);
            builder.Property<string>("RegionId").HasMaxLength(64);
            builder.HasIndex("RegionId");

            var p = System.Linq.Expressions.Expression.Parameter(entityType, "e");
            var regionProp = System.Linq.Expressions.Expression.Call(
                typeof(EF), nameof(EF.Property), [typeof(string)], p,
                System.Linq.Expressions.Expression.Constant("RegionId"));
            var currentRegion = System.Linq.Expressions.Expression.Property(
                System.Linq.Expressions.Expression.Constant(this), nameof(CurrentRegion));
            builder.HasQueryFilter(System.Linq.Expressions.Expression.Lambda(
                System.Linq.Expressions.Expression.Equal(regionProp, currentRegion), p));
        }
    }

    /// <summary>
    /// 新增区域隔离实体时自动回填当前区域（写入隔离的一半；另一半是全局查询过滤器）。
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added || !IsRegionScoped(entry.Entity.GetType())) continue;
            var region = entry.Property("RegionId").CurrentValue as string;
            if (string.IsNullOrEmpty(region))
            {
                entry.Property("RegionId").CurrentValue = AgoraIn.Server.Security.RegionContext.Current;
            }
        }
        return await base.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// 系统用户（Web 管理面板 + API 鉴权）。
/// 角色见 <see cref="AgoraIn.Core.Security.AppRoles"/>；子账户通过 <see cref="OwnerUserId"/> 归属主账户。
/// </summary>
public sealed class User
{
    public int Id { get; set; }

    /// <summary>登录名（全局唯一）。</summary>
    public string Username { get; set; } = "";

    /// <summary>密码哈希（BCrypt；兼容升级前的旧哈希时会自动重写）。</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>角色：admin / owner / teacher / parent / student。</summary>
    public string Role { get; set; } = AgoraIn.Core.Security.AppRoles.Teacher;

    /// <summary>显示名（可空，界面优先展示）。</summary>
    public string? DisplayName { get; set; }

    /// <summary>邮箱（用于注册验证与找回密码，可空）。</summary>
    public string? Email { get; set; }

    /// <summary>
    /// 所属主账户的用户 Id（子账户专有；null 表示主账户/系统管理员）。
    /// 主账户只能管理自己的子账户，实现分级权限。
    /// </summary>
    public int? OwnerUserId { get; set; }

    /// <summary>
    /// 所属区域："manager" = 主区域（房主，系统管理员与服务器运营方）；
    /// 区域代号 = 子区域（租户）；null = 旧数据（视为 manager，由 <see cref="RegionIdOrManager"/> 归一）。
    /// </summary>
    public string? RegionId { get; set; }

    /// <summary>归一后的区域标识（null 视为 manager，兼容旧数据）。</summary>
    public string RegionIdOrManager => string.IsNullOrEmpty(RegionId)
        ? AgoraIn.Server.Security.RegionContext.ManagerRegion
        : RegionId;

    /// <summary>是否启用（禁用后无法登录，历史数据保留）。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>最后登录时间。</summary>
    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 邮箱验证码（注册 / 重置密码）。
/// 相比 v3.2 的加固：记录尝试次数、支持过期清理、用途白名单。
/// </summary>
public sealed class EmailCodeEntity
{
    public int Id { get; set; }

    public string Email { get; set; } = "";

    /// <summary>6 位数字验证码（BCrypt 哈希存储，不落明文）。</summary>
    public string CodeHash { get; set; } = "";

    /// <summary>用途：register / reset。</summary>
    public string Purpose { get; set; } = "register";

    /// <summary>请求来源 IP（用于限流审计）。</summary>
    public string? RequestIp { get; set; }

    /// <summary>已尝试校验次数（超过阈值即失效，防暴力枚举）。</summary>
    public int Attempts { get; set; }

    /// <summary>是否已使用。</summary>
    public bool Used { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime ExpireAt { get; set; }

    /// <summary>是否仍可用（未用、未过期、尝试次数未超限）。</summary>
    public bool IsUsable(DateTime now, int maxAttempts = 5)
        => !Used && ExpireAt > now && Attempts < maxAttempts;
}

/// <summary>
/// 区域（租户）：每个注册用户（v3.2 语义）自动创建一个区域并成为其主账号（房主/租户）；
/// 系统管理员（主区域 manager）为服务器整体（离线激活），子区域由主区域颁发激活码激活。
/// 区域之间数据不互通（见 <see cref="ServerDbContext"/> 的 RegionId 全局查询过滤器）。
/// 登录格式：<c>用户名@区域Id</c>；主区域使用 <c>用户名@manager</c>。
/// </summary>
public sealed class Region
{
    public int Id { get; set; }

    /// <summary>区域代号（登录用，全局唯一，3-32 位字母/数字/下划线/连字符）。</summary>
    public string RegionId { get; set; } = "";

    /// <summary>区域名称（如"某某培训学校"，全局唯一不可重复）。</summary>
    public string Name { get; set; } = "";

    /// <summary>设备协议密码（设备端接入本区域用，可空）。</summary>
    public string? DevicePassword { get; set; }

    /// <summary>区域主账号用户 Id。</summary>
    public int OwnerUserId { get; set; }

    /// <summary>是否已激活（激活码由主区域颁发）。</summary>
    public bool Activated { get; set; }

    /// <summary>最近一次使用的激活码（脱敏留档）。</summary>
    public string? ActivationCode { get; set; }

    /// <summary>到期时间（激活时间 + 时长；null = 未激活）。</summary>
    public DateTime? ExpireAt { get; set; }

    /// <summary>设备数上限。</summary>
    public int MaxDevices { get; set; }

    /// <summary>激活时间。</summary>
    public DateTime? ActivatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>区域是否可用（已激活且未到期）。</summary>
    public bool IsActive => Activated && (ExpireAt == null || ExpireAt > DateTime.Now);
}

/// <summary>
/// AI 大模型调用日志：供管理端核对 Token 消耗与失败原因（<c>api/v4/settings/ai/logs</c>）。
/// </summary>
public sealed class AiCallLogEntity
{
    public long Id { get; set; }

    /// <summary>调用场景：recognize（答题卡识别）/ grade（主观题批改）。</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>实际使用的模型名。</summary>
    public string Model { get; set; } = "";

    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }

    /// <summary>调用耗时（毫秒）。</summary>
    public int DurationMs { get; set; }

    public bool Success { get; set; }

    /// <summary>失败原因摘要（成功为 null）。</summary>
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>已注册的桌面端设备。</summary>
public sealed class Device
{
    public int Id { get; set; }
    public string DeviceUuid { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public DateTime LastSeen { get; set; }
    public bool IsOnline => (DateTime.Now - LastSeen).TotalSeconds < 30;
}

/// <summary>
/// 离线授权记录（单行表；授权码由 <c>Tools/LicenseTool</c> 离线签发，格式与 v3.2 兼容）。
/// 指纹在首次激活时固定写入，避免硬件变化导致授权漂移失效。
/// </summary>
public sealed class LicenseEntity
{
    public int Id { get; set; }

    /// <summary>激活时固定的机器指纹（大写 16 位 hex）。</summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>授权类型：server / region。</summary>
    public string Type { get; set; } = "server";

    /// <summary>本次激活码包含的时长（月）。</summary>
    public int Months { get; set; }

    /// <summary>允许的设备台数。</summary>
    public int MaxDevices { get; set; }

    /// <summary>客户名称。</summary>
    public string? Customer { get; set; }

    /// <summary>授权流水号。</summary>
    public string? Serial { get; set; }

    /// <summary>激活时间。</summary>
    public DateTime ActivatedAt { get; set; } = DateTime.Now;

    /// <summary>到期时间（续期时在原有基础上叠加）。</summary>
    public DateTime ExpireAt { get; set; }

    /// <summary>原始激活码（保存以便续期/审计）。</summary>
    public string Code { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 呼叫记录（ClassIsland 兼容层）：待下课通知 / 上课应急 / 下课传唤。
/// 契约见 v4/docs/api-contract-classisland.md。
/// </summary>
public sealed class CallEntity
{
    public int Id { get; set; }

    /// <summary>prenotice=待下课通知, summon=下课传唤, emergency=上课应急。</summary>
    public string Type { get; set; } = "prenotice";

    public string Title { get; set; } = "";
    public string Message { get; set; } = "";

    /// <summary>提前分钟数（prenotice 用）。</summary>
    public int MinutesBefore { get; set; }

    /// <summary>涉及学生姓名（逗号分隔）。</summary>
    public string StudentNames { get; set; } = "";

    public string Sender { get; set; } = "";

    /// <summary>定向设备 UUID（null/空 = 广播到所有接收端）。</summary>
    public string? TargetUuid { get; set; }

    public bool Acked { get; set; }
    public DateTime? AckedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
