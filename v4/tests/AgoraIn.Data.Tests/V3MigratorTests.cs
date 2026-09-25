using AgoraIn.Core.Entities;
using AgoraIn.Data.V3;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgoraIn.Data.Tests;

/// <summary>v3 数据迁移集成测试。</summary>
public class V3MigratorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _v3Dir;

    public V3MigratorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"agorain-migrate-{Guid.NewGuid():N}.db");
        _v3Dir = Path.Combine(Path.GetTempPath(), $"agorain-v3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_v3Dir);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        try { if (Directory.Exists(_v3Dir)) Directory.Delete(_v3Dir, true); } catch { }
        try { File.Delete(Path.Combine(Path.GetTempPath(), "agorain-mig-diag.txt")); } catch { }
    }

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new AppDbContext(options);
    }

    private void WriteV3Files(string school, string nj, string classId, string km,
        string[] tabNames, string[][] tabStudents, string[][] tabAttendance)
    {
        File.WriteAllText(Path.Combine(_v3Dir, "config.json"),
            $$"""{"School":"{{school}}","Nj":"{{nj}}","ClassId":"{{classId}}","Km":"{{km}}","ButtonRows":6,"ButtonCols":6}""");

        var tabs = new List<string>();
        for (var i = 0; i < tabNames.Length; i++)
        {
            var id = $"tab{i + 1}";
            tabs.Add($"{{\"Id\":\"{id}\",\"Name\":\"{tabNames[i]}\"}}");
            var tabDir = Path.Combine(_v3Dir, "data", "tabs", id);
            Directory.CreateDirectory(tabDir);
            File.WriteAllText(Path.Combine(tabDir, "name.txt"), string.Join("\n", tabStudents[i]));
            if (i < tabAttendance.Length && tabAttendance[i].Length > 0)
                File.WriteAllLines(Path.Combine(tabDir, "attendance.dat"), tabAttendance[i]);
        }

        File.WriteAllText(Path.Combine(_v3Dir, "workspace.json"),
            $$"""{"Tabs":[{{string.Join(",", tabs)}}],"ActiveTabId":"tab1"}""");

        File.WriteAllText(Path.Combine(_v3Dir, "classhours.json"),
            """{"Version":3,"Students":[{"Id":"ch1","Name":"张三","TotalHours":20,"UsedHours":5},{"Id":"ch2","Name":"李四","TotalHours":30,"UsedHours":0}],"Records":[{"Id":"r1","StudentId":"ch1","Date":"2026-09-01","Hours":-2,"Note":"划消","CreatedAt":"2026-09-01 08:00:00"},{"Id":"r2","StudentId":"ch1","Date":"2026-09-02","Hours":5,"Note":"赠送","CreatedAt":"2026-09-02 10:00:00"}],"Schedule":{"2026-09-03":[{"StudentId":"ch1","StartTime":"08:00","EndTime":"09:30"},{"StudentId":"ch2","StartTime":"10:00","EndTime":"11:00"}]},"OffDays":["2026-09-10","2026-09-11"],"HoursPerHour":1.5,"AutoDeduct":true}""");
    }

    [Fact]
    public void Migrate_完整数据_各类数据正确迁入()
    {
        WriteV3Files("阳光小学", "三", "1", "数学",
            new[] { "三1班数学", "口算比赛" },
            new[] { new[] { "张三", "李四", "王五" }, new[] { "张三", "王五" } },
            new[] {
                new[] { "张三:3:2026-09-01 080000", "李四:1:2026-09-01 080005" },
                new[] { "王五:2:2026-09-03 100000" },
            });

        // 用独立上下文做迁移（诊断计数）
        V3MigrationResult result;
        using (var db = CreateDb())
        {
            db.Database.EnsureCreated();
            result = V3Migrator.Migrate(db, new V3MigrationOptions(_v3Dir));
        }

        // 立即写诊断（不等任何断言）
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "agorain-mig-diag.txt"), $"OK\nSuccess={result.Success}\nError={result.Error}\nCounts={result.Counts}\n"); } catch { }

        Assert.True(result.Success, result.Error);
        Assert.Null(result.Error);
        Assert.Equal(1, result.Counts.Classes);
        Assert.Equal(3, result.Counts.Students);
        Assert.Equal(2, result.Counts.Tasks);
        Assert.Equal(5, result.Counts.RosterEntries);
        Assert.Equal(3, result.Counts.CheckInRecords);
        Assert.Equal(2, result.Counts.HourAccounts);
        Assert.Equal(2, result.Counts.HourRecords);
        Assert.Equal(2, result.Counts.ScheduleEntries);
        Assert.Equal(2, result.Counts.OffDays);

        // 用全新上下文读库验证数据持久化
        using var v = CreateDb();
        Assert.Equal(1, v.Classes.Count());
        Assert.Equal(3, v.Students.Count());
        Assert.Equal(2, v.CheckInTasks.Count());
        Assert.Equal(5, v.TaskRosterEntries.Count());
        Assert.Equal(3, v.CheckInRecords.Count());
        Assert.Equal(2, v.ClassHourAccounts.Count());
        Assert.Equal(2, v.ClassHourRecords.Count());
        Assert.Equal(2, v.CourseScheduleEntries.Count());
        Assert.Equal(2, v.ScheduleOffDays.Count());

        Assert.Equal("阳光小学三年1班", v.Classes.Single().Name);
        var zhangSan = v.Students.Single(s => s.Name == "张三");
        Assert.Equal(20, v.ClassHourAccounts.Single(a => a.StudentId == zhangSan.Id).TotalHours);

        var tasks = v.CheckInTasks.OrderBy(t => t.SortOrder).ToList();
        Assert.Equal("三1班数学", tasks[0].Name);
        Assert.Equal("数学", tasks[0].Subject);
    }

    [Fact]
    public void Migrate_幂等_二次调用跳过()
    {
        var myDb = Path.Combine(Path.GetTempPath(), $"agorain-idem-{Guid.NewGuid():N}.db");
        var myV3 = Path.Combine(Path.GetTempPath(), $"agorain-v3-idem-{Guid.NewGuid():N}");
        Directory.CreateDirectory(myV3);
        try
        {
            File.WriteAllText(Path.Combine(myV3, "config.json"),
                """{"School":"学校","Nj":"一","ClassId":"1","Km":"语文"}""");
            File.WriteAllText(Path.Combine(myV3, "workspace.json"),
                """{"Tabs":[{"Id":"t1","Name":"默认"}],"ActiveTabId":"t1"}""");
            var tabDir = Path.Combine(myV3, "data", "tabs", "t1");
            Directory.CreateDirectory(tabDir);
            File.WriteAllText(Path.Combine(tabDir, "name.txt"), "小明");
            File.WriteAllText(Path.Combine(myV3, "classhours.json"),
                """{"Version":3,"Students":[],"Records":[],"Schedule":{},"OffDays":[]}""");

            var opts = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={myDb}").Options;
            using (var db = new AppDbContext(opts))
            {
                db.Database.EnsureCreated();
                var r1 = V3Migrator.Migrate(db, new V3MigrationOptions(myV3));
                Assert.True(r1.Success, r1.Error);
                Assert.Equal(1, db.Students.Count());

                var r2 = V3Migrator.Migrate(db, new V3MigrationOptions(myV3));
                Assert.True(r2.Success, r2.Error);
                Assert.Contains("已迁移过", r2.Error);
                Assert.Equal(1, db.Students.Count());
            }
        }
        finally
        {
            try { File.Delete(myDb); } catch { }
            try { Directory.Delete(myV3, true); } catch { }
        }
    }

    [Fact]
    public void Migrate_空目录_不报错生成默认班级()
    {
        using var db = CreateDb();
        db.Database.EnsureCreated();

        var result = V3Migrator.Migrate(db, new V3MigrationOptions(_v3Dir));
        Assert.True(result.Success, result.Error);
        Assert.Equal("默认班级", db.Classes.Single().Name);
        Assert.Equal(0, result.Counts.Students);
    }
}
