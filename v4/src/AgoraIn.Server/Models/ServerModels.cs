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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<Device>().HasIndex(d => d.DeviceUuid).IsUnique();

        modelBuilder.Entity<AgoraIn.Core.Entities.ParentBinding>()
            .HasIndex(b => b.InviteCode).IsUnique();
        modelBuilder.Entity<AgoraIn.Core.Entities.NoticeReadReceipt>()
            .HasIndex(r => new { r.NoticeId, r.ParentUserId }).IsUnique();
        modelBuilder.Entity<AgoraIn.Core.Entities.AppSetting>()
            .HasKey(s => s.Key);
    }
}

/// <summary>系统用户（Web 管理面板 + API 鉴权）。</summary>
public sealed class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "admin"; // admin / teacher / viewer
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
