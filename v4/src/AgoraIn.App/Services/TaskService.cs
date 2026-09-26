using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using AgoraIn.App.Models;
using AgoraIn.Core.Entities;

namespace AgoraIn.App.Services;

/// <summary>
/// 桌面端核心服务：任务管理、学生名单、打卡/取消、排名、导入导出。
/// 直接操作文件系统（沿用 v3.2 data/tabs/{id}/ 布局），P5 阶段再接入同步引擎。
/// </summary>
public sealed class TaskService
{
    private readonly string _baseDir;

    public TaskService(string baseDir)
    {
        _baseDir = baseDir;
    }

    // ───── 任务树 ─────

    /// <summary>从 data/tabs/ 扫描所有任务目录，构建任务树。</summary>
    public List<TaskTreeNode> BuildTaskTree()
    {
        var tabsRoot = Path.Combine(_baseDir, "data", "tabs");
        if (!Directory.Exists(tabsRoot))
        {
            return [];
        }

        var nodes = new List<TaskTreeNode>();
        foreach (var dir in Directory.GetDirectories(tabsRoot).OrderBy(d => d))
        {
            var id = Path.GetFileName(dir);
            var configPath = Path.Combine(dir, "config.json");
            var config = ReadTabConfig(configPath);
            nodes.Add(new TaskTreeNode
            {
                Id = id,
                DisplayName = config.Name.Length > 0 ? config.Name : id,
                IsFolder = false,
                TabId = id,
            });
        }

        return nodes;
    }

    /// <summary>创建新任务（目录 + 默认配置 + 空名单）。</summary>
    public string CreateTab(string name, string km = "数学")
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var tabDir = Path.Combine(_baseDir, "data", "tabs", id);
        Directory.CreateDirectory(tabDir);

        var config = new TabConfig { Name = name, Km = km };
        WriteTabConfig(configPath: Path.Combine(tabDir, "config.json"), config);
        File.WriteAllText(Path.Combine(tabDir, "name.txt"), "");

        return id;
    }

    /// <summary>删除任务目录。</summary>
    public void DeleteTab(string tabId)
    {
        var tabDir = Path.Combine(_baseDir, "data", "tabs", tabId);
        if (Directory.Exists(tabDir))
        {
            Directory.Delete(tabDir, true);
        }
    }

    // ───── 学生名单 ─────

    /// <summary>读取任务的学生名单（name.txt 每行一个姓名）。</summary>
    public List<string> ReadStudentNames(string tabId)
    {
        var path = Path.Combine(_baseDir, "data", "tabs", tabId, "name.txt");
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadAllLines(path, Encoding.UTF8)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToList();
    }

    /// <summary>写入学生名单。</summary>
    public void WriteStudentNames(string tabId, IReadOnlyList<string> names)
    {
        var path = Path.Combine(_baseDir, "data", "tabs", tabId, "name.txt");
        File.WriteAllLines(path, names, Encoding.UTF8);
    }

    // ───── 打卡 ─────

    /// <summary>加载打卡数据（attendance.dat：姓名:次数:首次时间:历史1|历史2）。</summary>
    public Dictionary<string, StudentModel> LoadAttendance(string tabId, IReadOnlyList<string> names)
    {
        var students = new Dictionary<string, StudentModel>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            students[name] = new StudentModel { Name = name };
        }

        var path = Path.Combine(_baseDir, "data", "tabs", tabId, "attendance.dat");
        if (!File.Exists(path))
        {
            return students;
        }

        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(':');
            if (parts.Length < 3) continue;

            var name = parts[0];
            if (!students.TryGetValue(name, out var stu)) continue;

            stu.Count = int.TryParse(parts[1], out var c) ? c : 0;
            if (DateTime.TryParse(parts[2], out var ft))
            {
                stu.FirstTime = ft;
            }

            if (parts.Length > 3 && !string.IsNullOrEmpty(parts[3]))
            {
                stu.History = parts[3].Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
            }
        }

        return students;
    }

    /// <summary>保存打卡数据。</summary>
    public void SaveAttendance(string tabId, IReadOnlyDictionary<string, StudentModel> students)
    {
        var path = Path.Combine(_baseDir, "data", "tabs", tabId, "attendance.dat");
        var lines = students.Values.Select(s =>
            $"{s.Name}:{s.Count}:{s.FirstTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""}:{string.Join("|", s.History)}");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    /// <summary>打卡：设置首次时间、计数+1、追加历史。</summary>
    public static void CheckIn(StudentModel student)
    {
        if (student.IsCheckedIn) return;
        var now = DateTime.Now;
        student.FirstTime = now;
        student.Count++;
        student.History.Add(now.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    /// <summary>取消打卡：移除最后一条历史、重新计算。</summary>
    public static void CancelCheckIn(StudentModel student)
    {
        if (!student.IsCheckedIn) return;
        if (student.History.Count > 0)
        {
            student.History.RemoveAt(student.History.Count - 1);
        }

        student.Count = Math.Max(0, student.Count - 1);
        student.FirstTime = student.History.Count > 0 && DateTime.TryParse(student.History[0], out var ft)
            ? ft : null;
    }

    /// <summary>清空打卡记录。</summary>
    public static void ClearAllCheckIn(IReadOnlyDictionary<string, StudentModel> students)
    {
        foreach (var stu in students.Values)
        {
            stu.Count = 0;
            stu.FirstTime = null;
            stu.History.Clear();
        }
    }

    /// <summary>计算排名（最早打卡排序，前三金银铜）。</summary>
    public static List<RankingItem> ComputeRanking(IReadOnlyDictionary<string, StudentModel> students)
    {
        return students.Values
            .Where(s => s.IsCheckedIn)
            .OrderBy(s => s.FirstTime)
            .Select((s, i) => new RankingItem
            {
                Rank = i + 1,
                Name = s.Name,
                Time = s.FirstTime?.ToString("HH:mm:ss") ?? "",
            })
            .ToList();
    }

    // ───── 导入导出 ─────

    /// <summary>导出打卡数据为 CSV（姓名,打卡时间,打卡次数,历史记录）。</summary>
    public static string ExportCsv(IReadOnlyDictionary<string, StudentModel> students)
    {
        var sb = new StringBuilder();
        sb.AppendLine("姓名,打卡时间,打卡次数,历史记录");
        foreach (var s in students.Values.OrderBy(s => s.Name))
        {
            sb.AppendLine($"{EscapeCsv(s.Name)},{EscapeCsv(s.FirstTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")},{s.Count},{EscapeCsv(string.Join("|", s.History))}");
        }

        return sb.ToString();
    }

    /// <summary>导入 CSV 打卡数据。</summary>
    public static int ImportCsv(string csv, Dictionary<string, StudentModel> students)
    {
        var count = 0;
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines.Skip(1)) // 跳过表头
        {
            var parts = ParseCsvLine(line);
            if (parts.Length < 1) continue;
            var name = parts[0].Trim();
            if (!students.TryGetValue(name, out var stu)) continue;

            if (parts.Length > 1 && DateTime.TryParse(parts[1].Trim(), out var ft))
            {
                stu.FirstTime = ft;
            }

            if (parts.Length > 2 && int.TryParse(parts[2].Trim(), out var c))
            {
                stu.Count = c;
            }

            if (parts.Length > 3 && !string.IsNullOrEmpty(parts[3].Trim()))
            {
                stu.History = parts[3].Trim().Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            count++;
        }

        return count;
    }

    // ───── 工具方法 ─────

    private static string EscapeCsv(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuote = !inQuote;
            }
            else if (ch == ',' && !inQuote)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        result.Add(current.ToString());
        return result.ToArray();
    }

    private static TabConfig ReadTabConfig(string path)
    {
        if (!File.Exists(path)) return new TabConfig();
        try
        {
            return JsonSerializer.Deserialize<TabConfig>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new TabConfig();
        }
        catch
        {
            return new TabConfig();
        }
    }

    private static void WriteTabConfig(string configPath, TabConfig config)
    {
        File.WriteAllText(configPath, JsonSerializer.Serialize(config,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
