using System.Text.Json;
using AgoraIn.Core.Entities;

namespace AgoraIn.App.Services;

/// <summary>
/// 课表持久化服务：读写 data/timetable.json（CSES 格式子集）。
/// </summary>
public sealed class TimetableStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public TimetableStore(string basePath)
    {
        _path = Path.Combine(basePath, "data", "timetable.json");
    }

    public TimetableData Load()
    {
        if (!File.Exists(_path)) return new TimetableData();
        try
        {
            return JsonSerializer.Deserialize<TimetableData>(File.ReadAllText(_path), JsonOpts) ?? new TimetableData();
        }
        catch { return new TimetableData(); }
    }

    public void Save(TimetableData data)
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(data, JsonOpts));
    }
}

/// <summary>课表数据（持久化到 data/timetable.json）。</summary>
public sealed class TimetableData
{
    public List<TtSubject> Subjects { get; set; } = new();
    public List<TtTimeLayout> TimeLayouts { get; set; } = new();
    public List<TtClassPlan> ClassPlans { get; set; } = new();
}

public sealed class TtSubject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string? Color { get; set; }
    public string? TeacherName { get; set; }
}

public sealed class TtTimeLayout
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "默认作息";
    public List<TtTimeEntry> Entries { get; set; } = new();
}

public sealed class TtTimeEntry
{
    public int Index { get; set; }
    public string StartTime { get; set; } = "08:00"; // HH:mm
    public string EndTime { get; set; } = "08:45";
    public bool IsBreak { get; set; }
    public string? Name { get; set; } // 节次名称：早读/第一节/课间...
}

/// <summary>班级课表：周几 × 节次 → 科目。</summary>
public sealed class TtClassPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "课表";
    public string TimeLayoutId { get; set; } = "";
    public bool IsActive { get; set; } = true;
    /// <summary>entries[weekDay * maxSlot + slotIndex] = subjectId；空=无课。</summary>
    public Dictionary<int, string> Entries { get; set; } = new();
}

/// <summary>
/// ClassIsland 课表导入/导出（CSES 格式兼容子集）。
/// </summary>
public static class ClassIslandCompat
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>从 ClassIsland 档案 JSON 导入课表。</summary>
    public static TimetableData ImportFromJson(string json)
    {
        var data = JsonSerializer.Deserialize<ClassIslandArchive>(json, JsonOpts);
        if (data == null) return new TimetableData();

        var result = new TimetableData();

        // 科目
        foreach (var s in data.Subjects ?? [])
        {
            result.Subjects.Add(new TtSubject
            {
                Id = s.Id ?? Guid.NewGuid().ToString("N")[..8],
                Name = s.Name ?? "",
                Color = s.Color,
            });
        }

        // 时间布局
        foreach (var tl in data.TimeLayouts ?? [])
        {
            var layout = new TtTimeLayout
            {
                Id = tl.Id ?? Guid.NewGuid().ToString("N")[..8],
                Name = tl.Name ?? "导入作息",
            };
            foreach (var e in tl.Entries ?? [])
            {
                layout.Entries.Add(new TtTimeEntry
                {
                    Index = e.Index,
                    StartTime = e.StartTime ?? "08:00",
                    EndTime = e.EndTime ?? "08:45",
                    IsBreak = e.IsBreak,
                    Name = e.Name,
                });
            }
            result.TimeLayouts.Add(layout);
        }

        // 班级课表
        foreach (var cp in data.ClassPlans ?? [])
        {
            var plan = new TtClassPlan
            {
                Id = cp.Id ?? Guid.NewGuid().ToString("N")[..8],
                Name = cp.Name ?? "导入课表",
                TimeLayoutId = cp.TimeLayoutId ?? "",
            };
            // ClassIsland 用二维数组 [weekDay][slot] = subjectId
            if (cp.Table != null)
            {
                for (var wd = 0; wd < cp.Table.Length; wd++)
                {
                    if (cp.Table[wd] == null) continue;
                    for (var sl = 0; sl < cp.Table[wd].Length; sl++)
                    {
                        var subId = cp.Table[wd][sl];
                        if (!string.IsNullOrEmpty(subId))
                            plan.Entries[wd * 100 + sl] = subId;
                    }
                }
            }
            result.ClassPlans.Add(plan);
        }

        return result;
    }

    /// <summary>导出为 ClassIsland 档案 JSON。</summary>
    public static string ExportToJson(TimetableData data)
    {
        var archive = new ClassIslandArchive
        {
            Subjects = data.Subjects.Select(s => new CiSubject { Id = s.Id, Name = s.Name, Color = s.Color }).ToList(),
            TimeLayouts = data.TimeLayouts.Select(tl => new CiTimeLayout
            {
                Id = tl.Id,
                Name = tl.Name,
                Entries = tl.Entries.Select(e => new CiTimeEntry
                {
                    Index = e.Index, StartTime = e.StartTime, EndTime = e.EndTime,
                    IsBreak = e.IsBreak, Name = e.Name,
                }).ToList(),
            }).ToList(),
            ClassPlans = data.ClassPlans.Select(cp => new CiClassPlan
            {
                Id = cp.Id, Name = cp.Name, TimeLayoutId = cp.TimeLayoutId,
                Table = BuildTable(cp),
            }).ToList(),
        };
        return JsonSerializer.Serialize(archive, JsonOpts);
    }

    private static string[][]? BuildTable(TtClassPlan plan)
    {
        if (plan.Entries.Count == 0) return null;
        var maxSlot = plan.Entries.Keys.Max() % 100 + 1;
        var table = new string[7][];
        for (var wd = 0; wd < 7; wd++)
        {
            table[wd] = new string[maxSlot];
            for (var sl = 0; sl < maxSlot; sl++)
            {
                var key = wd * 100 + sl;
                table[wd][sl] = plan.Entries.TryGetValue(key, out var subId) ? subId : "";
            }
        }
        return table;
    }
}

// ClassIsland 档案反序列化模型
internal sealed class ClassIslandArchive
{
    public List<CiSubject>? Subjects { get; set; }
    public List<CiTimeLayout>? TimeLayouts { get; set; }
    public List<CiClassPlan>? ClassPlans { get; set; }
}
internal sealed class CiSubject { public string? Id { get; set; } public string? Name { get; set; } public string? Color { get; set; } }
internal sealed class CiTimeLayout { public string? Id { get; set; } public string? Name { get; set; } public List<CiTimeEntry>? Entries { get; set; } }
internal sealed class CiTimeEntry { public int Index { get; set; } public string? StartTime { get; set; } public string? EndTime { get; set; } public bool IsBreak { get; set; } public string? Name { get; set; } }
internal sealed class CiClassPlan { public string? Id { get; set; } public string? Name { get; set; } public string? TimeLayoutId { get; set; } public string[][]? Table { get; set; } }
