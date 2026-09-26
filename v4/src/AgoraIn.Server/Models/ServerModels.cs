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

    // ── 答题卡与 AI 阅卷 ──
    public DbSet<ExamPaper> ExamPapers => Set<ExamPaper>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerSheetSubmission> AnswerSheetSubmissions => Set<AnswerSheetSubmission>();
    public DbSet<QuestionResult> QuestionResults => Set<QuestionResult>();
    public DbSet<AgoraIn.Core.Entities.ClassInfo> Classes => Set<AgoraIn.Core.Entities.ClassInfo>();
    public DbSet<AgoraIn.Core.Entities.Student> Students => Set<AgoraIn.Core.Entities.Student>();
    public DbSet<AgoraIn.Core.Entities.CheckInTask> CheckInTasks => Set<AgoraIn.Core.Entities.CheckInTask>();
    public DbSet<AgoraIn.Core.Entities.CheckInRecord> CheckInRecords => Set<AgoraIn.Core.Entities.CheckInRecord>();
    public DbSet<AgoraIn.Core.Entities.ClassHourAccount> ClassHourAccounts => Set<AgoraIn.Core.Entities.ClassHourAccount>();
    public DbSet<AgoraIn.Core.Entities.ClassHourRecord> ClassHourRecords => Set<AgoraIn.Core.Entities.ClassHourRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<Device>().HasIndex(d => d.DeviceUuid).IsUnique();
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
