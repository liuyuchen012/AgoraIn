using System.Text.Json;
using AgoraIn.Core.Domain;
using AgoraIn.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Data.V3;

/// <summary>迁移输入：v3.2 客户端数据目录（含 config.json / workspace.json / classhours.json / data/tabs）。</summary>
public sealed record V3MigrationOptions(string BaseDir);

/// <summary>迁移计数汇总。</summary>
public sealed record V3MigrationCounts(
    int Classes,
    int Students,
    int Tasks,
    int RosterEntries,
    int CheckInRecords,
    int HourAccounts,
    int HourRecords,
    int ScheduleEntries,
    int OffDays);

/// <summary>迁移结果。</summary>
public sealed record V3MigrationResult(bool Success, string? Error, V3MigrationCounts Counts)
{
    public static V3MigrationResult Fail(string error) => new(false, error, new V3MigrationCounts(0, 0, 0, 0, 0, 0, 0, 0, 0));
}

/// <summary>
/// v3.2 → v4 数据迁移器：把 config.json / workspace.json（含 data/tabs 任务目录）/
/// classhours.json 的字段语义等价迁入 v4 SQLite 库。
/// 迁移幂等：完成后在 AppSettings 写入 v3.migratedAt 标记，重复调用直接返回。
/// </summary>
public static class V3Migrator
{
    public static V3MigrationResult Migrate(AppDbContext db, V3MigrationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var baseDir = Path.GetFullPath(options.BaseDir);

        // 幂等保护：已迁移过则跳过
        var migratedFlag = db.AppSettings.FirstOrDefault(s => s.Key == "v3.migratedAt");
        if (migratedFlag != null)
        {
            return new V3MigrationResult(true, "已迁移过（v3.migratedAt 标记存在），本次跳过。", CountAll(db));
        }

        try
        {
            var configJson = V3DataFiles.ReadTextOrNull(Path.Combine(baseDir, "config.json"));
            var config = V3DataFiles.DeserializeOrNull<AppConfigV3>(configJson) ?? new AppConfigV3();

            var workspace = V3DataFiles.DeserializeOrNull<WorkspaceConfigV3>(
                V3DataFiles.ReadTextOrNull(Path.Combine(baseDir, "workspace.json"))) ?? new WorkspaceConfigV3();

            var classHours = V3DataFiles.DeserializeOrNull<ClassHourDataV3>(
                V3DataFiles.ReadTextOrNull(Path.Combine(baseDir, "classhours.json"))) ?? new ClassHourDataV3();

            // SQLite 事务由 SaveChanges 自动管理，不显式 BeginTransaction
            // 以避免 SQLite 连接池下的事务隔离问题

            // 1) 班级：沿用 v3 的 学校+年级+班级 命名
            var className = BuildClassName(config);
            var classInfo = new ClassInfo
            {
                Name = className,
                Grade = config.Nj,
                Remark = "v3 迁移",
            };
            db.Classes.Add(classInfo);

            // 2) 全局设置原样留档 + 课时设置结构化保存
            if (configJson != null)
            {
                db.AppSettings.Add(new AppSetting { Key = "v3.config", Value = configJson });
            }

            db.AppSettings.Add(new AppSetting
            {
                Key = "classhours.settings",
                Value = JsonSerializer.Serialize(new { classHours.HoursPerHour, classHours.AutoDeduct }),
            });

            // 3) 学生身份合并表：班级内按姓名唯一（v3 任务名单与划课学生两套身份合并）
            var studentsByName = new Dictionary<string, Student>(StringComparer.Ordinal);
            int studentsCreated = 0;

            Student GetOrCreateStudent(string name, string? remark = null)
            {
                if (studentsByName.TryGetValue(name, out var existing))
                {
                    return existing;
                }

                var student = new Student
                {
                    ClassId = classInfo.Id,
                    Name = name,
                    Remark = remark ?? "",
                };
                db.Students.Add(student);
                studentsByName[name] = student;
                studentsCreated++;
                return student;
            }

            // 4) 任务树 + 名单 + 打卡记录
            int taskOrder = 0, rosterCount = 0, checkInCount = 0;
            foreach (var tab in workspace.Tabs.Where(t => !string.IsNullOrWhiteSpace(t.Id)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tabDir = Path.Combine(baseDir, "data", "tabs", tab.Id);
                var tabConfig = V3DataFiles.DeserializeOrNull<TabConfigV3>(
                    V3DataFiles.ReadTextOrNull(Path.Combine(tabDir, "config.json")));

                var task = new CheckInTask
                {
                    Name = tabConfig?.Name is { Length: > 0 } n ? n : tab.Name,
                    Subject = tabConfig?.Km ?? config.Km ?? "",
                    ButtonRows = tabConfig?.ButtonRows ?? config.ButtonRows,
                    ButtonCols = tabConfig?.ButtonCols ?? config.ButtonCols,
                    IsSignInTask = tabConfig?.IsSignInTask ?? false,
                    SignInTaskId = tabConfig?.SignInTaskId,
                    SortOrder = taskOrder++,
                };
                db.CheckInTasks.Add(task);

                // 名单（name.txt，每行一个姓名）
                var names = ReadStudentNames(Path.Combine(tabDir, "name.txt"));
                for (var i = 0; i < names.Count; i++)
                {
                    var student = GetOrCreateStudent(names[i]);
                    db.TaskRosterEntries.Add(new TaskRosterEntry
                    {
                        TaskId = task.Id,
                        StudentId = student.Id,
                        SortOrder = i,
                    });
                    rosterCount++;
                }

                // 打卡记录（attendance.dat：姓名:次数:首次时间:历史1|历史2）
                var attPath = Path.Combine(tabDir, "attendance.dat");
                checkInCount += ImportAttendance(db, attPath, task.Id, studentsByName, GetOrCreateStudent);
            }

            // 5) 课时账户 / 流水 / 排课 / 不排课日
            var accounts = new Dictionary<string, ClassHourAccount>();
            int accountCount = 0, hourRecordCount = 0, scheduleCount = 0;
            var seenSlotKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var ch in classHours.Students)
            {
                if (string.IsNullOrWhiteSpace(ch.Name))
                {
                    continue;
                }

                var student = GetOrCreateStudent(ch.Name, ch.Remark);
                var account = new ClassHourAccount
                {
                    StudentId = student.Id,
                    TotalHours = ch.TotalHours,
                    UsedHours = ch.UsedHours,
                    Remark = ch.Remark,
                };
                db.ClassHourAccounts.Add(account);
                accounts[ch.Id] = account;
                accountCount++;
            }

            foreach (var rec in classHours.Records)
            {
                if (!accounts.TryGetValue(rec.StudentId, out var account))
                {
                    continue; // 孤儿流水（学生已删），与 v3 一样忽略
                }

                if (!DateOnly.TryParse(rec.Date, out var date))
                {
                    date = DateOnly.FromDateTime(DateTime.Now);
                }

                // v3 数据自身防重：同 SlotKey 只迁一条
                if (!string.IsNullOrEmpty(rec.SlotKey) && !seenSlotKeys.Add(rec.SlotKey))
                {
                    continue;
                }

                _ = DateTime.TryParse(rec.CreatedAt, out var createdAt);

                db.ClassHourRecords.Add(new ClassHourRecord
                {
                    StudentId = account.StudentId,
                    Date = date,
                    Delta = rec.Hours,
                    Note = rec.Note,
                    SlotKey = rec.SlotKey,
                    Source = string.IsNullOrEmpty(rec.SlotKey) ? ClassHourSource.Manual : ClassHourSource.AutoSchedule,
                    CreatedAt = createdAt == default ? DateTime.Now : createdAt,
                });
                hourRecordCount++;
            }

            foreach (var (dateText, entries) in classHours.Schedule)
            {
                if (!DateOnly.TryParse(dateText, out var date))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (!accounts.TryGetValue(entry.StudentId, out var account))
                    {
                        continue;
                    }

                    if (!TimeOnly.TryParse(entry.StartTime, out var start) || !TimeOnly.TryParse(entry.EndTime, out var end))
                    {
                        continue; // 起止时间无效的排课（v3 视为无效，不参与自动划消）
                    }

                    db.CourseScheduleEntries.Add(ClassHourService.CreateScheduleEntry(date, account.StudentId, start, end));
                    scheduleCount++;
                }
            }

            int offDayCount = 0;
            foreach (var off in classHours.OffDays)
            {
                if (DateOnly.TryParse(off, out var date))
                {
                    db.ScheduleOffDays.Add(new ScheduleOffDay { Date = date });
                    offDayCount++;
                }
            }

            db.AppSettings.Add(new AppSetting
            {
                Key = "v3.migratedAt",
                Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            });

            _ = db.SaveChanges();

            var counts = new V3MigrationCounts(
                1, studentsCreated, taskOrder, rosterCount, checkInCount, accountCount, hourRecordCount, scheduleCount, offDayCount);
            return new V3MigrationResult(true, null, counts);
        }
        catch (Exception ex)
        {
            return V3MigrationResult.Fail(ex.Message + "\n" + ex.StackTrace);
        }
    }

    private static string BuildClassName(AppConfigV3 config)
    {
        var school = config.School?.Trim() ?? "";
        var nj = config.Nj?.Trim() ?? "";
        var classId = config.ClassId?.Trim() ?? "";
        if (school.Length == 0 && nj.Length == 0 && classId.Length == 0)
        {
            return "默认班级";
        }

        return $"{school}{nj}年{classId}班";
    }

    private static List<string> ReadStudentNames(string path)
    {
        var text = V3DataFiles.ReadTextOrNull(path);
        if (text == null)
        {
            return [];
        }

        return text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToList();
    }

    private static int ImportAttendance(
        AppDbContext db,
        string path,
        string taskId,
        Dictionary<string, Student> studentsByName,
        Func<string, string?, Student> getOrCreateStudent)
    {
        var text = V3DataFiles.ReadTextOrNull(path);
        if (text == null)
        {
            return 0;
        }

        // v3 格式：姓名:次数:首次时间:历史1|历史2
        // v3 的时间戳格式不统一（有冒号/无冒号），迁移到 v4 只需姓名+打卡时间。
        // 解析策略：竖线分段后，每段尝试两种格式解析（yyyy-MM-dd HH:mm:ss 和 yyyy-MM-dd HHmmss）；
        // 第一段含姓名+计数前缀，跳过"姓名:计数:"后解析。
        var count = 0;
        var v3Fmts = new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HHmmss" };
        var dtStyle = System.Globalization.DateTimeStyles.None;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Length == 0) continue;

            var segments = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) continue;

            var firstColon = segments[0].IndexOf(':');
            if (firstColon < 0) continue;
            var name = segments[0][..firstColon];
            if (name.Length == 0) continue;

            if (!studentsByName.TryGetValue(name, out var student))
                student = getOrCreateStudent(name, null);

            // 第一段：跳过 "姓名:次数:" 后剩余部分尝试解析
            var firstSeg = segments[0];
            var afterName = firstSeg[(firstColon + 1)..];
            var countEndColon = afterName.IndexOf(':');
            var skipTo = countEndColon >= 0 ? firstColon + 1 + countEndColon + 1 : firstColon + 1;
            var firstPart = firstSeg[skipTo..];
            // 第一段可能含多个时间戳（用 ':' 连接），只取第一个完整时间戳
            // 第二个时间戳以 ":2026-" 开头（冒号+年份），以此截断
            var dashIdx = firstPart.IndexOf(":2026-", StringComparison.Ordinal);
            if (dashIdx > 0 && dashIdx < firstPart.Length - 1) firstPart = firstPart[..dashIdx];
            if (DateTime.TryParseExact(firstPart.Trim(), v3Fmts, culture, dtStyle, out var firstAt))
            {
                db.CheckInRecords.Add(new CheckInRecord
                {
                    TaskId = taskId, StudentId = student.Id,
                    CheckedAt = firstAt, Source = CheckInSource.Import, Note = "v3 迁移",
                });
                count++;
            }

            for (var i = 1; i < segments.Length; i++)
            {
                if (DateTime.TryParseExact(segments[i].Trim(), v3Fmts, culture, dtStyle, out var at))
                {
                    db.CheckInRecords.Add(new CheckInRecord
                    {
                        TaskId = taskId, StudentId = student.Id,
                        CheckedAt = at, Source = CheckInSource.Import, Note = "v3 迁移",
                    });
                    count++;
                }
            }
        }

        return count;
    }

    private static V3MigrationCounts CountAll(AppDbContext db) => new(
        db.Classes.Count(),
        db.Students.Count(),
        db.CheckInTasks.Count(),
        db.TaskRosterEntries.Count(),
        db.CheckInRecords.Count(),
        db.ClassHourAccounts.Count(),
        db.ClassHourRecords.Count(),
        db.CourseScheduleEntries.Count(),
        db.ScheduleOffDays.Count());
}
