using AgoraIn.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Data;

/// <summary>
/// AgoraIn v4 本地数据库上下文（SQLite，本地优先架构）。
/// 领域实体见 AgoraIn.Core.Entities；本上下文只负责映射与约束。
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // ── 班级基础 ──
    public DbSet<School> Schools => Set<School>();
    public DbSet<ClassInfo> Classes => Set<ClassInfo>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Student> Students => Set<Student>();

    // ── 课时与排课 ──
    public DbSet<ClassHourAccount> ClassHourAccounts => Set<ClassHourAccount>();
    public DbSet<ClassHourRecord> ClassHourRecords => Set<ClassHourRecord>();
    public DbSet<CourseScheduleEntry> CourseScheduleEntries => Set<CourseScheduleEntry>();
    public DbSet<ScheduleOffDay> ScheduleOffDays => Set<ScheduleOffDay>();

    // ── 打卡 ──
    public DbSet<CheckInTask> CheckInTasks => Set<CheckInTask>();
    public DbSet<TaskRosterEntry> TaskRosterEntries => Set<TaskRosterEntry>();
    public DbSet<CheckInRecord> CheckInRecords => Set<CheckInRecord>();

    // ── 座位 ──
    public DbSet<SeatChart> SeatCharts => Set<SeatChart>();
    public DbSet<Seat> Seats => Set<Seat>();

    // ── 点名 ──
    public DbSet<RollCallSession> RollCallSessions => Set<RollCallSession>();
    public DbSet<RollCallRecord> RollCallRecords => Set<RollCallRecord>();

    // ── 积分 ──
    public DbSet<PointRule> PointRules => Set<PointRule>();
    public DbSet<PointRecord> PointRecords => Set<PointRecord>();

    // ── 值日 ──
    public DbSet<DutyPost> DutyPosts => Set<DutyPost>();
    public DbSet<DutyRoster> DutyRosters => Set<DutyRoster>();
    public DbSet<DutyRosterEntry> DutyRosterEntries => Set<DutyRosterEntry>();
    public DbSet<DutyRecord> DutyRecords => Set<DutyRecord>();

    // ── CSES 课表 ──
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<TimeLayout> TimeLayouts => Set<TimeLayout>();
    public DbSet<TimeLayoutEntry> TimeLayoutEntries => Set<TimeLayoutEntry>();
    public DbSet<ClassPlan> ClassPlans => Set<ClassPlan>();
    public DbSet<ClassPlanEntry> ClassPlanEntries => Set<ClassPlanEntry>();

    // ── 答题卡与 AI 阅卷 ──
    public DbSet<ExamPaper> ExamPapers => Set<ExamPaper>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerSheetTemplate> AnswerSheetTemplates => Set<AnswerSheetTemplate>();
    public DbSet<AnswerSheetSubmission> AnswerSheetSubmissions => Set<AnswerSheetSubmission>();
    public DbSet<QuestionResult> QuestionResults => Set<QuestionResult>();

    // ── 资源库与家长沟通 ──
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<NoticeReadReceipt> NoticeReadReceipts => Set<NoticeReadReceipt>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ParentBinding> ParentBindings => Set<ParentBinding>();

    // ── 同步与设置 ──
    public DbSet<SyncLog> SyncLogs => Set<SyncLog>();
    public DbSet<ConflictPolicy> ConflictPolicies => Set<ConflictPolicy>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ClassHourAccount>();
        e.HasIndex(a => a.StudentId).IsUnique();

        var r = modelBuilder.Entity<ClassHourRecord>();
        r.HasIndex(x => new { x.StudentId, x.Date });
        // SlotKey 幂等：唯一索引仅约束非空值（SQLite 部分索引）
        r.HasIndex(x => x.SlotKey).IsUnique().HasFilter("[SlotKey] IS NOT NULL");

        modelBuilder.Entity<CourseScheduleEntry>()
            .HasIndex(x => new { x.Date, x.StudentId });

        modelBuilder.Entity<CheckInTask>()
            .HasIndex(x => x.ParentId);

        modelBuilder.Entity<TaskRosterEntry>()
            .HasIndex(x => new { x.TaskId, x.StudentId }).IsUnique();

        modelBuilder.Entity<CheckInRecord>()
            .HasIndex(x => new { x.TaskId, x.StudentId, x.CheckedAt });

        modelBuilder.Entity<Seat>()
            .HasIndex(x => new { x.ChartId, x.Row, x.Col }).IsUnique();

        modelBuilder.Entity<Student>()
            .HasIndex(x => new { x.ClassId, x.Name }).IsUnique();

        modelBuilder.Entity<RollCallRecord>()
            .HasIndex(x => x.SessionId);

        modelBuilder.Entity<PointRecord>()
            .HasIndex(x => new { x.StudentId, x.CreatedAt });

        modelBuilder.Entity<DutyRosterEntry>()
            .HasIndex(x => new { x.RosterId, x.PeriodIndex, x.PostId, x.SlotIndex }).IsUnique();

        modelBuilder.Entity<TimeLayoutEntry>()
            .HasIndex(x => new { x.TimeLayoutId, x.Index }).IsUnique();

        modelBuilder.Entity<ClassPlanEntry>()
            .HasIndex(x => new { x.ClassPlanId, x.WeekDay, x.SlotIndex }).IsUnique();

        modelBuilder.Entity<Question>()
            .HasIndex(x => new { x.PaperId, x.Index }).IsUnique();

        modelBuilder.Entity<QuestionResult>()
            .HasIndex(x => new { x.SubmissionId, x.QuestionId }).IsUnique();

        modelBuilder.Entity<NoticeReadReceipt>()
            .HasIndex(x => new { x.NoticeId, x.ParentUserId }).IsUnique();

        modelBuilder.Entity<ParentBinding>()
            .HasIndex(x => x.InviteCode).IsUnique();

        modelBuilder.Entity<ConflictPolicy>()
            .HasKey(x => x.EntityName);

        modelBuilder.Entity<AppSetting>()
            .HasKey(x => x.Key);

        modelBuilder.Entity<ScheduleOffDay>()
            .HasKey(x => x.Date);
    }
}
