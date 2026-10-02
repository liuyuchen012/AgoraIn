using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Services;

/// <summary>
/// 可视化编辑器里拖动/缩放后的题目位置覆盖（mm，相对纸面左上角）。
/// 有覆盖的题按这份坐标**绝对定位**，其余题仍按自动流排版——老师的调整不该被自动分页冲掉。
/// 按题目 Id（稳定）而不是题号索引：重新编号后覆盖不会串到别的题上。
/// </summary>
public sealed record QuestionPlacement(
    string QuestionId,
    int PageNo,
    double Xmm,
    double Ymm,
    double Wmm,
    double Hmm);

/// <summary>
/// 答题卡版面覆盖的存取。存 AppSettings（region 隔离自动生效），不动表结构：
///   sheet.layout.{paperId} = QuestionPlacement[]
/// 渲染、版面模型、识别、切图四处都读这一份，拖完照样扫得出来。
/// </summary>
public sealed class SheetLayoutService(ServerDbContext db)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private const string Prefix = "sheet.layout.";

    public async Task<List<QuestionPlacement>> LoadAsync(string paperId, CancellationToken ct = default)
    {
        var row = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Key == Prefix + paperId, ct);
        if (row == null || string.IsNullOrWhiteSpace(row.Value)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<QuestionPlacement>>(row.Value, JsonOpts) ?? [];
        }
        catch { return []; }
    }

    /// <summary>整份覆盖表替换保存（编辑器每次保存都发全量）。坐标夹在纸面内并吸附到 0.5mm。</summary>
    public async Task SaveAsync(string paperId, IEnumerable<QuestionPlacement> items, CancellationToken ct = default)
    {
        var cleaned = items
            .Where(i => !string.IsNullOrWhiteSpace(i.QuestionId) && i.PageNo >= 1)
            .GroupBy(i => i.QuestionId)
            .Select(g => g.Last())
            .Select(i => i with
            {
                Xmm = Snap(Math.Max(0, i.Xmm)),
                Ymm = Snap(Math.Max(0, i.Ymm)),
                Wmm = Snap(Math.Max(20, i.Wmm)),
                Hmm = Snap(Math.Max(8, i.Hmm)),
            })
            .ToList();

        var key = Prefix + paperId;
        var json = JsonSerializer.Serialize(cleaned);
        var row = await db.AppSettings.FirstOrDefaultAsync(a => a.Key == key, ct);
        if (row == null) db.AppSettings.Add(new Core.Entities.AppSetting { Key = key, Value = json });
        else row.Value = json;
        await db.SaveChangesAsync(ct);
    }

    private static double Snap(double v) => Math.Round(v * 2) / 2;   // 吸附 0.5mm
}
