using System.Text.Json;
using AgoraIn.Server.Models;
using Microsoft.EntityFrameworkCore;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgoraIn.Server.Services;

/// <summary>
/// ClassIsland 档案（CSES）导入 / 导出 / 推送。
/// 推送语义：把当前课表序列化为 ClassIsland 可识别的档案 JSON 存入 AppSetting 并递增版本号，
/// 插件通过 <c>POST api/profile_pull</c>（连接密码鉴权，见 ClassIslandCompatController）拉取后
/// 覆盖 ClassIsland 本地档案文件。
/// </summary>
public sealed class ClassIslandProfileService
{
    private const string ProfileKeyPrefix = "classisland.profile.";
    private const string VersionKeyPrefix = "classisland.profileVersion.";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ServerDbContext _db;

    public ClassIslandProfileService(ServerDbContext db) => _db = db;

    // ══════════════ 导入 ══════════════

    /// <summary>
    /// 导入 ClassIsland 档案（自动识别 JSON / YAML），写入当前区域的科目/时间布局/班级课表。
    /// 返回导入统计。
    /// </summary>
    public async Task<(int Subjects, int Slots, string LayoutName)> ImportAsync(string text, string? classId, CancellationToken ct = default)
    {
        text = text.TrimStart();
        CsesArchive archive = text.StartsWith("{") || text.StartsWith("[")
            ? JsonSerializer.Deserialize<CsesArchive>(text, JsonOpts) ?? new()
            : ParseYaml(text);

        if ((archive.Subjects?.Count ?? 0) == 0 && (archive.Schedules?.Count ?? 0) == 0 && (archive.TimeLayouts?.Count ?? 0) == 0)
            throw new InvalidDataException("档案中没有可导入的课表数据（支持 ClassIsland JSON / YAML 档案）");

        // ── 科目（按名称去重合并，保留颜色/教师） ──
        var existingSubjects = await _db.Subjects.ToListAsync(ct);
        var subjectIdByName = existingSubjects.ToDictionary(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);

        int addedSubjects = 0;
        void EnsureSubject(string? name, string? teacher, string? color)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (subjectIdByName.ContainsKey(name)) return;
            var id = Guid.NewGuid().ToString("N")[..8];
            _db.Subjects.Add(new Core.Entities.Subject
            {
                Id = id,
                Name = name,
                TeacherName = teacher,
                Color = color,
            });
            subjectIdByName[name] = id;
            addedSubjects++;
        }

        foreach (var s in archive.Subjects ?? [])
            EnsureSubject(s.Name, s.Teacher, s.Color);

        // ── 时间布局 + 课表 ──
        var layout = new Core.Entities.TimeLayout { Name = "ClassIsland 导入", ClassId = classId };
        var plan = new Core.Entities.ClassPlan { Name = "ClassIsland 课表", ClassId = classId ?? "", IsActive = false };
        var entries = new List<Core.Entities.TimeLayoutEntry>();
        var planEntries = new List<Core.Entities.ClassPlanEntry>();
        var slotIndex = 0;
        var processedSlots = new HashSet<int>();
        var slotCount = 0;

        // YAML：schedules 按 enable_day 分组；JSON：ClassPlans[].Table[weekDay][slot]
        if (archive.Schedules is { Count: > 0 })
        {
            foreach (var dayGroup in (archive.Schedules ?? [])
                         .Where(s => s.EnableDay.HasValue)
                         .GroupBy(s => s.EnableDay!.Value))
            {
                var weekDay = dayGroup.Key - 1; // enable_day 1=周一 → 0
                if (weekDay is < 0 or > 6) continue;

                foreach (var sched in dayGroup)
                {
                    slotIndex = 0; // 每天的节次序列独立
                    foreach (var cls in sched.Classes ?? [])
                    {
                        if (string.IsNullOrEmpty(cls.Subject) && string.IsNullOrEmpty(cls.StartTime)) continue;
                        var sl = slotIndex++;

                        if (!processedSlots.Contains(sl))
                        {
                            processedSlots.Add(sl);
                            entries.Add(new Core.Entities.TimeLayoutEntry
                            {
                                TimeLayoutId = layout.Id,
                                Index = sl,
                                StartTime = ParseTimeOnly(cls.StartTime, new TimeOnly(8 + sl % 12, 0)),
                                EndTime = ParseTimeOnly(cls.EndTime, new TimeOnly(8 + sl % 12, 45)),
                                Kind = Core.Entities.TimeEntryKind.Class,
                                Name = $"第{sl + 1}节",
                            });
                            slotCount = Math.Max(slotCount, sl + 1);
                        }

                        if (!string.IsNullOrEmpty(cls.Subject))
                        {
                            EnsureSubject(cls.Subject, null, null);
                            if (subjectIdByName.TryGetValue(cls.Subject, out var subId))
                                planEntries.Add(new Core.Entities.ClassPlanEntry
                                {
                                    ClassPlanId = plan.Id,
                                    WeekDay = weekDay,
                                    SlotIndex = sl,
                                    SubjectId = subId,
                                });
                        }
                    }
                }
            }
        }
        else
        {
            // JSON 档案：TimeLayouts + ClassPlans（取第一个布局与第一个课表）
            var tl = archive.TimeLayouts?.FirstOrDefault();
            if (tl?.Entries != null)
            {
                foreach (var e in tl.Entries)
                {
                    entries.Add(new Core.Entities.TimeLayoutEntry
                    {
                        TimeLayoutId = layout.Id,
                        Index = e.Index,
                        StartTime = ParseTimeOnly(e.StartTime, new TimeOnly(8, 0)),
                        EndTime = ParseTimeOnly(e.EndTime, new TimeOnly(8, 45)),
                        Kind = e.IsBreak ? Core.Entities.TimeEntryKind.Break : Core.Entities.TimeEntryKind.Class,
                        Name = e.Name,
                    });
                }
            }
            var cp = archive.ClassPlans?.FirstOrDefault();
            if (cp?.Table != null)
            {
                for (var wd = 0; wd < cp.Table.Length && wd < 7; wd++)
                {
                    var row = cp.Table[wd];
                    if (row == null) continue;
                    for (var sl = 0; sl < row.Length; sl++)
                    {
                        var subId = row[sl];
                        if (string.IsNullOrEmpty(subId)) continue;
                        planEntries.Add(new Core.Entities.ClassPlanEntry
                        {
                            ClassPlanId = plan.Id,
                            WeekDay = wd,
                            SlotIndex = sl,
                            SubjectId = subId,
                        });
                    }
                }
            }
        }

        _db.TimeLayouts.Add(layout);
        _db.TimeLayoutEntries.AddRange(entries);
        _db.ClassPlans.Add(plan);
        _db.ClassPlanEntries.AddRange(planEntries);
        await _db.SaveChangesAsync(ct);

        return (addedSubjects, planEntries.Count, layout.Name);
    }

    // ══════════════ 导出 ══════════════

    /// <summary>导出当前区域的课表为 ClassIsland 档案 JSON（ClassIsland 可直接导入）。</summary>
    public async Task<string> ExportAsync(string? classId, CancellationToken ct = default)
    {
        var subjects = await _db.Subjects
            .Where(s => classId == null || s.ClassId == classId || s.ClassId == null)
            .ToListAsync(ct);
        var layouts = await _db.TimeLayouts
            .Where(t => classId == null || t.ClassId == classId || t.ClassId == null)
            .ToListAsync(ct);
        var layoutIds = layouts.Select(l => l.Id).ToList();
        var entries = await _db.TimeLayoutEntries
            .Where(e => layoutIds.Contains(e.TimeLayoutId))
            .OrderBy(e => e.Index)
            .ToListAsync(ct);
        var plans = await _db.ClassPlans
            .Where(p => classId == null || p.ClassId == classId)
            .ToListAsync(ct);
        var planIds = plans.Select(p => p.Id).ToList();
        var planEntries = await _db.ClassPlanEntries
            .Where(e => planIds.Contains(e.ClassPlanId))
            .ToListAsync(ct);

        var archive = new CsesArchive
        {
            Subjects = subjects.Select(s => new CsesSubject { Id = s.Id, Name = s.Name, Teacher = s.TeacherName, Color = s.Color }).ToList(),
            TimeLayouts = layouts.Select(t => new CsesTimeLayout
            {
                Id = t.Id,
                Name = t.Name,
                Entries = entries.Where(e => e.TimeLayoutId == t.Id).Select(e => new CsesTimeEntry
                {
                    Index = e.Index,
                    StartTime = e.StartTime.ToString("HH:mm"),
                    EndTime = e.EndTime.ToString("HH:mm"),
                    IsBreak = e.Kind == Core.Entities.TimeEntryKind.Break,
                    Name = e.Name,
                }).ToList(),
            }).ToList(),
            ClassPlans = plans.Select(p => new CsesClassPlan
            {
                Id = p.Id,
                Name = p.Name,
                TimeLayoutId = p.TimeLayoutId,
                Table = BuildTable(p.Id, planEntries),
            }).ToList(),
        };
        return JsonSerializer.Serialize(archive, JsonOpts);
    }

    // ══════════════ 推送（供插件拉取覆盖） ══════════════

    /// <summary>把当前区域课表导出并写入推送存储，版本号 +1（幂等：内容未变时版本不变）。</summary>
    public async Task<int> PushAsync(string? classId, CancellationToken ct = default)
    {
        var region = Security.RegionContext.Current;
        var json = await ExportAsync(classId, ct);

        var versionKey = VersionKeyPrefix + region;
        var profileKey = ProfileKeyPrefix + region;

        var versionRow = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == versionKey, ct);
        var profileRow = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == profileKey, ct);

        var currentVersion = versionRow != null && int.TryParse(versionRow.Value, out var v) ? v : 0;
        var contentChanged = profileRow?.Value != json;
        var newVersion = contentChanged ? currentVersion + 1 : Math.Max(currentVersion, 1);

        if (profileRow != null) profileRow.Value = json;
        else _db.AppSettings.Add(new Core.Entities.AppSetting { Key = profileKey, Value = json });

        if (versionRow != null) versionRow.Value = newVersion.ToString();
        else _db.AppSettings.Add(new Core.Entities.AppSetting { Key = versionKey, Value = newVersion.ToString() });

        await _db.SaveChangesAsync(ct);
        return newVersion;
    }

    /// <summary>插件拉取：返回当前区域已推送的档案与版本（未推送返回 null）。</summary>
    public async Task<(string? Profile, int Version)> PullAsync(string regionId, CancellationToken ct = default)
    {
        var profileKey = ProfileKeyPrefix + regionId;
        var versionKey = VersionKeyPrefix + regionId;
        var profile = await _db.AppSettings.Where(s => s.Key == profileKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        var versionText = await _db.AppSettings.Where(s => s.Key == versionKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return (profile, int.TryParse(versionText, out var v) ? v : 0);
    }

    // ══════════════ helpers ══════════════

    private static string?[][]? BuildTable(string planId, List<Core.Entities.ClassPlanEntry> entries)
    {
        var mine = entries.Where(e => e.ClassPlanId == planId).ToList();
        if (mine.Count == 0) return null;
        var maxSlot = mine.Max(e => e.SlotIndex) + 1;
        var table = new string[7][];
        for (var wd = 0; wd < 7; wd++)
        {
            table[wd] = new string[maxSlot];
            for (var sl = 0; sl < maxSlot; sl++)
            {
                table[wd][sl] = mine.FirstOrDefault(e => e.WeekDay == wd && e.SlotIndex == sl)?.SubjectId ?? "";
            }
        }
        return table;
    }

    private static TimeOnly ParseTimeOnly(string? raw, TimeOnly fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        raw = raw.Trim();
        if (TimeOnly.TryParseExact(raw, "HH:mm", out var t)) return t;
        if (TimeOnly.TryParse(raw, out var t2)) return t2;
        if (TimeSpan.TryParse(raw, out var ts) && ts < TimeSpan.FromHours(24))
            return new TimeOnly(ts.Hours, ts.Minutes);
        return fallback;
    }

    private static CsesArchive ParseYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<CsesArchive>(yaml) ?? new();
    }
}

// ── CSES / ClassIsland 档案模型（JSON 与 YAML 共用，忽略未匹配字段） ──

public sealed class CsesArchive
{
    public int Version { get; set; } = 1;
    public List<CsesSubject>? Subjects { get; set; }
    public List<CsesTimeLayout>? TimeLayouts { get; set; }
    public List<CsesClassPlan>? ClassPlans { get; set; }

    // YAML 档案（v3 导出格式）：schedules 按天分组
    public List<CsesSchedule>? Schedules { get; set; }
}

public sealed class CsesSubject
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? SimplifiedName { get; set; }
    public string? Teacher { get; set; }
    public string? Room { get; set; }
    public string? Color { get; set; }
}

public sealed class CsesTimeLayout
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public List<CsesTimeEntry>? Entries { get; set; }
}

public sealed class CsesTimeEntry
{
    public int Index { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public bool IsBreak { get; set; }
    public string? Name { get; set; }
}

public sealed class CsesClassPlan
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? TimeLayoutId { get; set; }
    /// <summary>ClassIsland 二维课表 [weekDay][slot] = subjectId。</summary>
    public string[][]? Table { get; set; }
}

public sealed class CsesSchedule
{
    public string? Name { get; set; }
    public List<CsesScheduleClass>? Classes { get; set; }
    public int? EnableDay { get; set; }
    public string? Weeks { get; set; }
}

public sealed class CsesScheduleClass
{
    public string? Subject { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
}
