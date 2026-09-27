using AgoraIn.Core.Entities;
using AgoraIn.Server.Models;
using AgoraIn.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 答题卡与 AI 阅卷 API。前缀 /api/v4/exams
/// </summary>
[ApiController]
[Route("api/v4/exams")]
[Authorize]
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
}
