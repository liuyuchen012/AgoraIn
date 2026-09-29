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

    [HttpGet("{paperId}")]
    public async Task<IActionResult> RenderSheet(string paperId, [FromQuery] string? region)
    {
        await ApplyRegionAsync(region);
        var (paper, questions, options) = await LoadPaperAsync(paperId);
        if (paper == null) return NotFound(new { error = "试卷不存在" });
        return Content(AnswerSheetRenderer.Render(paper, questions, options), "text/html; charset=utf-8");
    }

    [HttpGet("{paperId}/student/{studentId}")]
    public async Task<IActionResult> RenderSheetForStudent(string paperId, string studentId, [FromQuery] string? region)
    {
        await ApplyRegionAsync(region);
        var (paper, questions, options) = await LoadPaperAsync(paperId);
        if (paper == null) return NotFound(new { error = "试卷不存在" });
        var student = await _db.Students.FindAsync(studentId);
        if (student == null) return NotFound(new { error = "学生不存在" });
        return Content(AnswerSheetRenderer.Render(paper, questions, options,
            studentName: student.Name, studentNo: student.StudentNo ?? student.Id), "text/html; charset=utf-8");
    }

    [HttpGet("{paperId}/batch")]
    public async Task<IActionResult> RenderBatch(string paperId, [FromQuery] string classId, [FromQuery] string? region)
    {
        await ApplyRegionAsync(region);
        var (paper, questions, options) = await LoadPaperAsync(paperId);
        if (paper == null) return NotFound(new { error = "试卷不存在" });
        var students = await _db.Students.Where(s => s.ClassId == classId).OrderBy(s => s.StudentNo).ToListAsync();
        if (students.Count == 0) return NotFound(new { error = "该班级没有学生" });

        var parts = new List<string>();
        foreach (var stu in students)
            parts.Add(AnswerSheetRenderer.Render(paper, questions, options,
                studentName: stu.Name, studentNo: stu.StudentNo ?? stu.Id));
        return Content(string.Join("\n<hr style='page-break-after:always'>\n", parts), "text/html; charset=utf-8");
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
