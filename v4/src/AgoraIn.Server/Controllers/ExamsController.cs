using AgoraIn.Core.Entities;
using AgoraIn.Core.Security;
using AgoraIn.Server.Models;
using AgoraIn.Server.Security;
using AgoraIn.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 答题卡与 AI 阅卷 API。前缀 /api/v4/exams
/// </summary>
[ApiController]
[Route("api/v4/exams")]
[Authorize]
[RequirePermission(Permissions.ExamsManage)]
public class ExamsController : ControllerBase
{
    private readonly ServerDbContext _db;
    private readonly DeepSeekGradingService _ai;

    public ExamsController(ServerDbContext db, DeepSeekGradingService ai)
    {
        _db = db;
        _ai = ai;
    }

    /// <summary>创建试卷。</summary>
    [HttpPost("papers")]
    public async Task<IActionResult> CreatePaper([FromBody] ExamPaper paper)
    {
        _db.ExamPapers.Add(paper);
        await _db.SaveChangesAsync();
        return Created("", paper);
    }

    /// <summary>上传答题卡图片并触发识别（考号涂卡 + 客观题 OMR）。</summary>
    [HttpPost("submissions")]
    public async Task<IActionResult> UploadSubmission([FromForm] IFormFile file, [FromQuery] string paperId)
    {
        if (file == null || file.Length == 0) return BadRequest("请上传答题卡图片");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var imageData = ms.ToArray();

        // AI 识别考号 + 客观题
        var omrResult = await _ai.RecognizeAnswerSheetAsync(imageData);

        var submission = new AnswerSheetSubmission
        {
            PaperId = paperId,
            StudentRef = omrResult?.StudentRef,
            ImagePathsJson = $"[\"{file.FileName}\"]",
            Status = SubmissionStatus.NotGraded,
        };

        _db.AnswerSheetSubmissions.Add(submission);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            submissionId = submission.Id,
            recognizedStudent = omrResult?.StudentRef,
            answers = omrResult?.Answers,
            confidence = omrResult?.OverallConfidence,
        });
    }

    /// <summary>AI 批改指定题目的学生答案（填空/简答/作文）。</summary>
    [HttpPost("submissions/{submissionId}/grade/{questionId}")]
    public async Task<IActionResult> AiGrade(string submissionId, string questionId, [FromBody] AiGradingRequest request)
    {
        var response = await _ai.GradeSubjectiveAsync(request);
        if (response == null) return BadRequest("AI 批改失败，请检查 DeepSeek API 配置");

        var result = new QuestionResult
        {
            SubmissionId = submissionId,
            QuestionId = questionId,
            Score = response.Score,
            Comment = response.Comment,
            Confidence = response.Confidence,
            Source = GradingSource.Ai,
            GradedAt = DateTime.Now,
        };

        _db.QuestionResults.Add(result);
        await _db.SaveChangesAsync();
        return Ok(result);
    }

    /// <summary>教师确认分数（状态机推进到已确认）。</summary>
    [HttpPost("submissions/{submissionId}/confirm")]
    public async Task<IActionResult> Confirm(string submissionId)
    {
        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission == null) return NotFound();

        submission.Status = SubmissionStatus.Confirmed;
        submission.ConfirmedAt = DateTime.Now;

        // 汇总总分
        var results = _db.QuestionResults.Where(r => r.SubmissionId == submissionId);
        submission.TotalScore = results.Sum(r => r.Score ?? 0);

        await _db.SaveChangesAsync();
        return Ok(new { submission.TotalScore, status = submission.Status.ToString() });
    }

    /// <summary>生成空白通用答题卡（浏览器打开后 Ctrl+P 直接打印 A4）。</summary>
    [HttpGet("papers/{paperId}/sheet")]
    public async Task<IActionResult> RenderSheet(string paperId)
    {
        var (paper, questions, options) = await LoadPaperAsync(paperId);
        if (paper == null) return NotFound(new { error = "试卷不存在" });

        var html = AnswerSheetRenderer.Render(paper, questions, options);
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>生成指定学生的专属答题卡（含姓名 + 学号条码）。</summary>
    [HttpGet("papers/{paperId}/sheet/{studentId}")]
    public async Task<IActionResult> RenderSheetForStudent(string paperId, string studentId)
    {
        var (paper, questions, options) = await LoadPaperAsync(paperId);
        if (paper == null) return NotFound(new { error = "试卷不存在" });

        var student = await _db.Students.FindAsync(studentId);
        if (student == null) return NotFound(new { error = "学生不存在" });

        var html = AnswerSheetRenderer.Render(
            paper, questions, options,
            studentName: student.Name,
            studentNo: student.StudentNo ?? student.Id);
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>批量生成全班答题卡（按学号排序，一人一页，浏览器打印为一册）。</summary>
    [HttpGet("papers/{paperId}/sheets/batch")]
    public async Task<IActionResult> RenderBatch(string paperId, [FromQuery] string classId)
    {
        var (paper, questions, options) = await LoadPaperAsync(paperId);
        if (paper == null) return NotFound(new { error = "试卷不存在" });

        var students = await _db.Students
            .Where(s => s.ClassId == classId)
            .OrderBy(s => s.StudentNo)
            .ToListAsync();
        if (students.Count == 0) return NotFound(new { error = "该班级没有学生" });

        var parts = new List<string>();
        foreach (var stu in students)
        {
            parts.Add(AnswerSheetRenderer.Render(
                paper, questions, options,
                studentName: stu.Name,
                studentNo: stu.StudentNo ?? stu.Id));
        }
        return Content(string.Join("\n<hr style=\"page-break-after:always\">\n", parts), "text/html; charset=utf-8");
    }

    /// <summary>加载试卷、题目与选项定义。</summary>
    private async Task<(ExamPaper? Paper, List<Question> Questions, Dictionary<string, List<string>> Options)> LoadPaperAsync(string paperId)
    {
        var paper = await _db.ExamPapers.FindAsync(paperId);
        if (paper == null) return (null, [], new Dictionary<string, List<string>>());

        var questions = await _db.Questions
            .Where(q => q.PaperId == paperId)
            .OrderBy(q => q.Index)
            .ToListAsync();

        // 从 OptionsJson 解析选项键（[{key:"A",text:"…"},…]）
        var options = new Dictionary<string, List<string>>();
        foreach (var q in questions)
        {
            if (string.IsNullOrEmpty(q.OptionsJson)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(q.OptionsJson);
                var keys = doc.RootElement.EnumerateArray()
                    .Select(o => o.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "")
                    .Where(s => s.Length > 0)
                    .ToList();
                if (keys.Count > 0) options[q.Id] = keys;
            }
            catch
            {
                // 选项格式异常时忽略，渲染器回落默认 A-D
            }
        }

        return (paper, questions, options);
    }
}
