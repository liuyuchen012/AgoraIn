using System.Text.Json;

namespace AgoraIn.Data.V3;

// ─────────────────────────────────────────────────────────────────────────────
// v3.2 本地数据文件模型（只读映射，字段名与 AgoraInPro 序列化契约逐字对齐）：
//   config.json     → AppConfigV3
//   workspace.json  → WorkspaceConfigV3
//   data/tabs/<id>/config.json → TabConfigV3
//   data/tabs/<id>/name.txt    → 每行一个学生姓名
//   data/tabs/<id>/attendance.dat → 每行"姓名:次数:首次时间:历史1|历史2"
//   classhours.json → ClassHourDataV3（Version=3）
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>v3 全局配置（config.json）。</summary>
public sealed class AppConfigV3
{
    public string School { get; set; } = "";
    public string Nj { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string Km { get; set; } = "";
    public int ButtonRows { get; set; } = 6;
    public int ButtonCols { get; set; } = 6;
    public bool OnlineMode { get; set; } = true;
    public string ServerIp { get; set; } = "";
    public int ServerPort { get; set; } = 5250;
    public string ServerPassword { get; set; } = "";
    public string AdminPasswordHash { get; set; } = "";
}

/// <summary>v3 工作区（workspace.json）：打开过的任务标签列表。</summary>
public sealed class WorkspaceConfigV3
{
    public List<TabInfoV3> Tabs { get; set; } = new();
    public string? ActiveTabId { get; set; }
}

public sealed class TabInfoV3
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>v3 标签页配置（data/tabs/&lt;id&gt;/config.json）。</summary>
public sealed class TabConfigV3
{
    public string Name { get; set; } = "";
    public string Km { get; set; } = "";
    public int ButtonRows { get; set; } = 6;
    public int ButtonCols { get; set; } = 6;
    public bool OnlineMode { get; set; } = true;
    public bool IsSignInTask { get; set; }
    public string? SignInTaskId { get; set; }
}

/// <summary>v3 课时划消数据（classhours.json，Version=3）。</summary>
public sealed class ClassHourDataV3
{
    public int Version { get; set; } = 3;
    public List<ChStudentV3> Students { get; set; } = new();
    public List<ChRecordV3> Records { get; set; } = new();

    /// <summary>排课：日期(yyyy-MM-dd) → 条目列表。</summary>
    public Dictionary<string, List<ScheduleEntryV3>> Schedule { get; set; } = new();

    /// <summary>不排课日（yyyy-MM-dd）。</summary>
    public List<string> OffDays { get; set; } = new();

    /// <summary>每小时上课消耗的课时数。</summary>
    public double HoursPerHour { get; set; } = 1;

    /// <summary>是否自动划消。</summary>
    public bool AutoDeduct { get; set; }
}

public sealed class ChStudentV3
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double TotalHours { get; set; }
    public double UsedHours { get; set; }
    public string Remark { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

/// <summary>v3 课时流水；实际语义：负数 = 划消（红），正数 = 增加（绿）。</summary>
public sealed class ChRecordV3
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Date { get; set; } = "";
    public double Hours { get; set; }
    public string Note { get; set; } = "";
    public string? SlotKey { get; set; }
    public string CreatedAt { get; set; } = "";
}

public sealed class ScheduleEntryV3
{
    public string StudentId { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
}

/// <summary>v3 数据文件读取与反序列化。</summary>
public static class V3DataFiles
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string ReadTextOrNull(string path)
        => File.Exists(path) ? File.ReadAllText(path) : null!;

    public static T? DeserializeOrNull<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
