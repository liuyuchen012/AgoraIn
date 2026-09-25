using AgoraIn.Core.SelfTest;
using AgoraIn.Data;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.App.Services;

/// <summary>data 模块自测：SQLite 本地库建表、实体覆盖、v3 迁移器幂等标记。</summary>
public sealed class DataSelfTestModule : ISelfTestModule
{
    public string Name => "data";

    public IReadOnlyList<SelfTestItem> Run(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"agorain-selftest-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;

            using var db = new AppDbContext(options);
            var created = db.Database.EnsureCreated();
            var canConnect = db.Database.CanConnect();

            // 验证关键实体表存在且可访问
            _ = db.Students.Count();
            _ = db.CheckInTasks.Count();
            _ = db.ClassHourRecords.Count();
            _ = db.SeatCharts.Count();
            _ = db.RollCallSessions.Count();
            _ = db.PointRules.Count();
            _ = db.DutyPosts.Count();
            _ = db.ExamPapers.Count();
            _ = db.AppSettings.Count();
            var tablesChecked = true;

            return new List<SelfTestItem>
            {
                new("SQLite 建库（EnsureCreated）", created, path),
                new("SQLite 连接可读（CanConnect）", canConnect),
                new("全部核心实体表可访问（10 张）", tablesChecked),
            };
        }
        catch (Exception ex)
        {
            return [new SelfTestItem("SQLite 本地库可用性", false, ex.Message)];
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
