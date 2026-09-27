using Microsoft.EntityFrameworkCore;
using AgoraIn.Server.Models;

namespace AgoraIn.Server;
/// <summary>
/// 服务端数据库上下文（SQLite）。
/// 与桌面端 Core 实体共享，服务端额外维护 User、Device 实体。
/// </summary>
public sealed class ServerDbContext : DbContext
{
    public ServerDbContext(DbContextOptions<ServerDbContext> options) : base(options) { }

    // ── 用户与设备 ──
    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();

    // ── 答题卡与 AI 阅卷（复用 Core 实体） ──
    public DbSet<AgoraIn.Core.Entities.ExamPaper> ExamPapers => Set<AgoraIn.Core.Entities.ExamPaper>();
    public DbSet<AgoraIn.Core.Entities.Question> Questions => Set<AgoraIn.Core.Entities.Question>();
    public DbSet<AgoraIn.Core.Entities.AnswerSheetSubmission> AnswerSheetSubmissions => Set<AgoraIn.Core.Entities.AnswerSheetSubmission>();
    public DbSet<AgoraIn.Core.Entities.QuestionResult> QuestionResults => Set<AgoraIn.Core.Entities.QuestionResult>();

    // ── 共享 Core 实体（复用 AgoraIn.Core.Entities） ──
    public DbSet<AgoraIn.Core.Entities.ClassInfo> Classes => Set<AgoraIn.Core.Entities.ClassInfo>();
    public DbSet<AgoraIn.Core.Entities.Student> Students => Set<AgoraIn.Core.Entities.Student>();
    public DbSet<AgoraIn.Core.Entities.CheckInTask> CheckInTasks => Set<AgoraIn.Core.Entities.CheckInTask>();
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

    // ── ClassIsland 呼叫（兼容层） ──
    public DbSet<CallEntity> Calls => Set<CallEntity>();

    // ── 离线授权（单行表） ──
    public DbSet<LicenseEntity> License => Set<LicenseEntity>();

    // ── 邮件服务与邮箱验证码 ──
    public DbSet<Services.SmtpSettings> SmtpSettings => Set<Services.SmtpSettings>();
    public DbSet<EmailCodeEntity> EmailCodes => Set<EmailCodeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<Device>().HasIndex(d => d.DeviceUuid).IsUnique();

        // 邮箱验证码：按邮箱+用途+时间查询，并支持过期清理
        modelBuilder.Entity<EmailCodeEntity>().HasIndex(c => new { c.Email, c.Purpose, c.CreatedAt });

        modelBuilder.Entity<AgoraIn.Core.Entities.ParentBinding>()
            .HasIndex(b => b.InviteCode).IsUnique();
        modelBuilder.Entity<AgoraIn.Core.Entities.NoticeReadReceipt>()
            .HasIndex(r => new { r.NoticeId, r.ParentUserId }).IsUnique();
        modelBuilder.Entity<AgoraIn.Core.Entities.AppSetting>()
            .HasKey(s => s.Key);
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
