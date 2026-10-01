using AgoraIn.Core.Entities;
using AgoraIn.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 答题卡渲染（匿名访问，用于 iframe 预览和打印）。
/// iframe 不携带 JWT，区域用户须在 URL 上带 <c>?region=区域Id</c> 指定区域（未知区域回落 manager）。
/// </summary>
[ApiController]
[Route("api/v4/sheet")]
public class AnswerSheetController : ControllerBase
{
    private readonly ServerDbContext _db;
    public AnswerSheetController(ServerDbContext db) => _db = db;

    /// <summary>匿名渲染的公共入口：按 query 参数切换区域上下文。</summary>
    private async Task<bool> ApplyRegionAsync(string? region)
    {
        if (string.IsNullOrWhiteSpace(region) || region == AgoraIn.Server.Security.RegionContext.ManagerRegion)
        {
            AgoraIn.Server.Security.RegionContext.Set(AgoraIn.Server.Security.RegionContext.ManagerRegion);
            return true;
        }
        var exists = await _db.Regions.AnyAsync(r => r.RegionId == region);
        AgoraIn.Server.Security.RegionContext.Set(exists ? region : null);
        return exists;
    }

    /// <summary>把 query 参数（纸张/考号区类型/注意事项）解析为渲染选项。</summary>
    private static AnswerSheetOptions ParseOptions(
        string? paper, string? idArea, bool? notes)
        => new()
        {
            Paper = SheetPaper.FromName(paper),
            IdArea = (idArea ?? "").Trim().ToLowerInvariant() switch
            {
                "handwrite" => IdAreaKind.Handwrite,
                "none" => IdAreaKind.None,
                _ => IdAreaKind.Bubble,
            },
            ShowNotes = notes ?? true,
        };

    [HttpGet("{paperId}")]
    public async Task<IActionResult> RenderSheet(
        string paperId, [FromQuery] string? region,
        [FromQuery] string? paper = null, [FromQuery] string? idArea = null, [FromQuery] bool? notes = null)
    {
        await ApplyRegionAsync(region);
        var (paperEntity, questions, options) = await LoadPaperAsync(paperId);
        if (paperEntity == null) return NotFound(new { error = "试卷不存在" });
        var opt = ParseOptions(paper, idArea, notes);
        return Content(AnswerSheetRenderer.Render(paperEntity, questions, options, sheetOptions: opt),
            "text/html; charset=utf-8");
    }

    [HttpGet("{paperId}/student/{studentId}")]
    public async Task<IActionResult> RenderSheetForStudent(
        string paperId, string studentId, [FromQuery] string? region,
        [FromQuery] string? paper = null, [FromQuery] string? idArea = null, [FromQuery] bool? notes = null)
    {
        await ApplyRegionAsync(region);
        var (paperEntity, questions, options) = await LoadPaperAsync(paperId);
        if (paperEntity == null) return NotFound(new { error = "试卷不存在" });
        var student = await _db.Students.FindAsync(studentId);
        if (student == null) return NotFound(new { error = "学生不存在" });
        var opt = ParseOptions(paper, idArea, notes);
        return Content(AnswerSheetRenderer.Render(paperEntity, questions, options,
            studentName: student.Name, studentNo: student.StudentNo ?? student.Id, sheetOptions: opt),
            "text/html; charset=utf-8");
    }

    [HttpGet("{paperId}/batch")]
    public async Task<IActionResult> RenderBatch(
        string paperId, [FromQuery] string classId, [FromQuery] string? region,
        [FromQuery] string? paper = null, [FromQuery] string? idArea = null, [FromQuery] bool? notes = null)
    {
        await ApplyRegionAsync(region);
        var (paperEntity, questions, options) = await LoadPaperAsync(paperId);
        if (paperEntity == null) return NotFound(new { error = "试卷不存在" });
        var students = await _db.Students.Where(s => s.ClassId == classId).OrderBy(s => s.StudentNo).ToListAsync();
        if (students.Count == 0) return NotFound(new { error = "该班级没有学生" });
        var opt = ParseOptions(paper, idArea, notes);

        var parts = new List<string>();
        foreach (var stu in students)
            parts.Add(AnswerSheetRenderer.Render(paperEntity, questions, options,
                studentName: stu.Name, studentNo: stu.StudentNo ?? stu.Id, sheetOptions: opt));
        return Content(string.Join("\n<hr style='page-break-after:always'>\n", parts), "text/html; charset=utf-8");
    }

    /// <summary>
    /// 考号表（匿名）：把班级学生的考号打印张贴，学生据此在【通用答题卡】上填涂。
    /// 速印机流程：通用答题卡制版印全班 → 考号表张贴/下发 → 学生自己填考号。
    /// </summary>
    [HttpGet("roster/{classId}")]
    public async Task<IActionResult> RenderRoster(
        string classId, [FromQuery] string? region, [FromQuery] string? paper = null)
    {
        await ApplyRegionAsync(region);
        var cls = await _db.Classes.FindAsync(classId);
        if (cls == null) return NotFound(new { error = "班级不存在" });
        var students = await _db.Students.Where(s => s.ClassId == classId)
            .OrderBy(s => s.StudentNo).ThenBy(s => s.Name).ToListAsync();
        if (students.Count == 0) return NotFound(new { error = "该班级没有学生" });
        var missing = students.Count(s => string.IsNullOrWhiteSpace(s.StudentNo));
        if (missing > 0)
            return Content("<h3 style='font-family:sans-serif'>该班级还有 " + missing +
                           " 名学生没有考号：请先在【试卷管理 → 答题卡配置 → 生成考号】中生成考号。</h3>",
                "text/html; charset=utf-8");

        var entries = students.Select(s => (s.StudentNo!, s.Name)).ToList();
        var opt = ParseOptions(paper, null, null);
        return Content(AnswerSheetRenderer.RenderRoster($"{cls.Name}", entries, opt),
            "text/html; charset=utf-8");
    }


    private async Task<(ExamPaper?, List<Question>, Dictionary<string, List<string>>)> LoadPaperAsync(string paperId)
    {
        var paper = await _db.ExamPapers.FindAsync(paperId);
        if (paper == null) return (null, [], new());
        var questions = await _db.Questions.Where(q => q.PaperId == paperId).OrderBy(q => q.Index).ToListAsync();
        var options = new Dictionary<string, List<string>>();
        foreach (var q in questions)
        {
            if (string.IsNullOrEmpty(q.OptionsJson)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(q.OptionsJson);
                var keys = doc.RootElement.EnumerateArray()
                    .Select(o => o.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "")
                    .Where(s => s.Length > 0).ToList();
                if (keys.Count > 0) options[q.Id] = keys;
            }
            catch { }
        }
        return (paper, questions, options);
    }
}
