using System.Text;
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
/// 批改状态机：未批 → AI已批 →（低置信度→）待人工 → 已确认；只有已确认才计入成绩。
/// </summary>
[ApiController]
[Route("api/v4/exams")]
[Authorize]
[RequirePermission(Permissions.ExamsManage)]
public class ExamsController : ControllerBase
{
    private readonly ServerDbContext _db;
    private readonly DeepSeekGradingService _ai;
    private readonly AiSettingsService _aiSettings;

    public ExamsController(ServerDbContext db, DeepSeekGradingService ai, AiSettingsService aiSettings)
    {
        _db = db;
        _ai = ai;
        _aiSettings = aiSettings;
    }

    /// <summary>试卷列表。</summary>
    [HttpGet("papers")]
    public async Task<IActionResult> ListPapers(CancellationToken ct)
    {
        var papers = await _db.ExamPapers
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id, p.Title, p.Subject, p.ClassId, p.TotalScore, p.CreatedBy, p.CreatedAt, p.IsTemplate,
                questionCount = _db.Questions.Count(q => q.PaperId == p.Id),
                submissionCount = _db.AnswerSheetSubmissions.Count(s => s.PaperId == p.Id),
            })
            .ToListAsync(ct);
        return Ok(papers);
    }

    /// <summary>创建试卷。</summary>
    [HttpPost("papers")]
    public async Task<IActionResult> CreatePaper([FromBody] ExamPaper paper)
    {
        _db.ExamPapers.Add(paper);
        await _db.SaveChangesAsync();
        return Created("", paper);
    }

    /// <summary>更新试卷。</summary>
    [HttpPut("papers/{paperId}")]
    public async Task<IActionResult> UpdatePaper(string paperId, [FromBody] ExamPaper update)
    {
        var paper = await _db.ExamPapers.FindAsync(paperId);
        if (paper == null) return NotFound();
        paper.Title = update.Title ?? paper.Title;
        paper.Subject = update.Subject ?? paper.Subject;
        paper.ClassId = update.ClassId;
        paper.IsTemplate = update.IsTemplate;
        await _db.SaveChangesAsync();
        return Ok(paper);
    }

    /// <summary>删除试卷（级联删除题目）。</summary>
    [HttpDelete("papers/{paperId}")]
    public async Task<IActionResult> DeletePaper(string paperId)
    {
        var paper = await _db.ExamPapers.FindAsync(paperId);
        if (paper == null) return NotFound();
        var questions = await _db.Questions.Where(q => q.PaperId == paperId).ToListAsync();
        _db.Questions.RemoveRange(questions);
        _db.ExamPapers.Remove(paper);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>试卷下的题目列表。</summary>
    [HttpGet("papers/{paperId}/questions")]
    public async Task<IActionResult> ListQuestions(string paperId)
    {
        var questions = await _db.Questions
            .Where(q => q.PaperId == paperId)
            .OrderBy(q => q.Index)
            .ToListAsync();
        return Ok(questions);
    }

    /// <summary>添加题目。</summary>
    [HttpPost("papers/{paperId}/questions")]
    public async Task<IActionResult> CreateQuestion(string paperId, [FromBody] Question question)
    {
        question.PaperId = paperId;
        _db.Questions.Add(question);
        await _db.SaveChangesAsync();
        await RefreshPaperTotalAsync(paperId);
        return Created("", question);
    }

    /// <summary>更新题目。</summary>
    [HttpPut("papers/{paperId}/questions/{questionId}")]
    public async Task<IActionResult> UpdateQuestion(string paperId, string questionId, [FromBody] Question update)
    {
        var q = await _db.Questions.FindAsync(questionId);
        if (q == null || q.PaperId != paperId) return NotFound();
        q.Index = update.Index;
        q.Type = update.Type;
        q.Score = update.Score;
        q.Content = update.Content;
        q.StandardAnswer = update.StandardAnswer;
        q.OptionsJson = update.OptionsJson;
        q.Rubric = update.Rubric;
        q.KnowledgeTagsJson = update.KnowledgeTagsJson;
        q.AiGradingEnabled = update.AiGradingEnabled;
        await _db.SaveChangesAsync();
        await RefreshPaperTotalAsync(paperId);
        return Ok(q);
    }

    /// <summary>从题库模板（IsTemplate=true 的试卷）复制题目到目标试卷。</summary>
    [HttpPost("papers/{paperId}/reuse/{templatePaperId}")]
    public async Task<IActionResult> ReuseQuestions(string paperId, string templatePaperId)
    {
        var paper = await _db.ExamPapers.FindAsync(paperId);
        if (paper == null) return NotFound("目标试卷不存在");
        var template = await _db.ExamPapers.FindAsync(templatePaperId);
        if (template == null) return NotFound("题库模板不存在");

        var questions = await _db.Questions
            .Where(q => q.PaperId == templatePaperId)
            .OrderBy(q => q.Index)
            .ToListAsync();
        var nextIndex = await _db.Questions
            .Where(q => q.PaperId == paperId)
            .Select(q => (int?)q.Index)
            .MaxAsync() ?? -1;

        foreach (var q in questions)
        {
            _db.Questions.Add(new Question
            {
                PaperId = paperId,
                Index = ++nextIndex,
                Type = q.Type,
                Content = q.Content,
                Score = q.Score,
                StandardAnswer = q.StandardAnswer,
                OptionsJson = q.OptionsJson,
                Rubric = q.Rubric,
                KnowledgeTagsJson = q.KnowledgeTagsJson,
                AiGradingEnabled = q.AiGradingEnabled,
            });
        }
        await _db.SaveChangesAsync();
        await RefreshPaperTotalAsync(paperId);
        return Ok(new { copied = questions.Count });
    }

    // ── 提交与批改流水线 ──

    /// <summary>某试卷的提交记录列表。</summary>
    [HttpGet("submissions")]
    public async Task<IActionResult> ListSubmissions([FromQuery] string paperId, CancellationToken ct)
    {
        var list = await _db.AnswerSheetSubmissions
            .Where(s => s.PaperId == paperId)
            .OrderByDescending(s => s.SubmittedAt)
            .ToListAsync(ct);
        var studentIds = list.Where(s => s.StudentId != null).Select(s => s.StudentId!).Distinct().ToList();
        var names = await _db.Students.Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return Ok(list.Select(s => new
        {
            s.Id, s.PaperId, s.StudentId, s.StudentRef, s.Status, s.TotalScore,
            s.SubmittedAt, s.AiGradedAt, s.ConfirmedAt, s.ImagePathsJson,
            studentName = s.StudentId != null && names.TryGetValue(s.StudentId, out var n) ? n : null,
        }));
    }

    /// <summary>
    /// 上传答题卡图片并触发识别（考号涂卡 + 客观题 OMR）。
    /// 客观题识别后立即与标准答案比对自动判分（Source=Ai）；识别到考号时按考号回填学生（学号匹配）。
    /// </summary>
    [HttpPost("submissions")]
    public async Task<IActionResult> UploadSubmission([FromForm] IFormFile file, [FromQuery] string paperId)
    {
        if (file == null || file.Length == 0) return BadRequest("请上传答题卡图片");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var imageData = ms.ToArray();

        var submission = new AnswerSheetSubmission
        {
            PaperId = paperId,
            ImagePathsJson = $"[\"{file.FileName}\"]",
            Status = SubmissionStatus.NotGraded,
        };

        // AI 识别考号 + 客观题（配置了 Key 且开启图像外发时可用；否则提交保持"未批"，走手判）
        var omrResult = await _ai.RecognizeAnswerSheetAsync(imageData);
        var questions = await _db.Questions
            .Where(q => q.PaperId == paperId)
            .OrderBy(q => q.Index)
            .ToListAsync();
        var byIndex = questions.ToDictionary(q => q.Index);
        var settings = await _aiSettings.LoadAsync();
        var lowestConfidence = 1.0;
        var autoScored = 0;

        if (omrResult != null)
        {
            submission.StudentRef = omrResult.StudentRef;
            // 考号 → 学号辅助匹配（结果须教师在批改工作台确认绑定）
            if (!string.IsNullOrEmpty(omrResult.StudentRef))
            {
                var stu = await _db.Students.FirstOrDefaultAsync(s => s.StudentNo == omrResult.StudentRef);
                if (stu != null) submission.StudentId = stu.Id;
            }

            foreach (var ans in omrResult.Answers)
            {
                if (!byIndex.TryGetValue(ans.Index, out var q)) continue;
                lowestConfidence = Math.Min(lowestConfidence, ans.Confidence);

                if (IsObjective(q.Type) && !string.IsNullOrEmpty(q.StandardAnswer))
                {
                    var correct = NormalizeAnswer(ans.Answer) == NormalizeAnswer(q.StandardAnswer);
                    _db.QuestionResults.Add(new QuestionResult
                    {
                        SubmissionId = submission.Id,
                        QuestionId = q.Id,
                        RecognizedAnswer = ans.Answer,
                        Score = correct ? q.Score : 0,
                        Confidence = ans.Confidence,
                        Source = GradingSource.Ai,
                        GradedAt = DateTime.Now,
                    });
                    autoScored++;
                }
                else
                {
                    // 主观题：先存识别内容，等待 AI 批改或人工
                    _db.QuestionResults.Add(new QuestionResult
                    {
                        SubmissionId = submission.Id,
                        QuestionId = q.Id,
                        RecognizedAnswer = ans.Answer,
                        Score = null,
                        Confidence = ans.Confidence,
                        Source = GradingSource.None,
                    });
                }
            }
        }

        if (omrResult != null && autoScored > 0)
        {
            // 有客观题自动判分即视为 AI 已批；识别置信度低或存在未定分主观题 → 待人工
            var hasUndecided = await _db.QuestionResults.CountAsync(r =>
                r.SubmissionId == submission.Id && r.Score == null) > 0;
            submission.Status = (hasUndecided || lowestConfidence < settings.HumanReviewThreshold)
                ? SubmissionStatus.NeedsHuman
                : SubmissionStatus.AiGraded;
            submission.AiGradedAt = DateTime.Now;
        }

        _db.AnswerSheetSubmissions.Add(submission);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            submissionId = submission.Id,
            status = submission.Status.ToString(),
            recognizedStudent = omrResult?.StudentRef,
            matchedStudentId = submission.StudentId,
            autoScored,
        });
    }

    /// <summary>整卷 AI 批改：对主观题（填空/简答/作文）逐题调用大模型按评分要点给分。</summary>
    [HttpPost("submissions/{submissionId}/grade-all")]
    public async Task<IActionResult> GradeAll(string submissionId)
    {
        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission == null) return NotFound();

        var questions = await _db.Questions
            .Where(q => q.PaperId == submission.PaperId)
            .OrderBy(q => q.Index)
            .ToListAsync();
        var results = await _db.QuestionResults
            .Where(r => r.SubmissionId == submissionId)
            .ToListAsync();
        var settings = await _aiSettings.LoadAsync();
        var lowestConfidence = 1.0;
        var graded = 0;

        foreach (var q in questions)
        {
            // 教师配置：false = 分配教师手判，跳过 AI
            if (q.AiGradingEnabled == false || IsObjective(q.Type)) continue;
            if (string.IsNullOrWhiteSpace(q.Content)) continue;

            var existing = results.FirstOrDefault(r => r.QuestionId == q.Id);
            var recognized = existing?.RecognizedAnswer ?? "";

            var response = await _ai.GradeSubjectiveAsync(new AiGradingRequest
            {
                QuestionText = q.Content,
                StudentAnswer = recognized,
                StandardAnswer = q.StandardAnswer,
                Rubric = q.Rubric,
                Type = q.Type.ToString(),
                MaxScore = q.Score,
            });
            if (response == null) continue;

            lowestConfidence = Math.Min(lowestConfidence, response.Confidence);
            AppendHistory(existing, response.Score, response.Comment, GradingSource.Ai);
            if (existing != null)
            {
                existing.Score = response.Score;
                existing.Comment = response.Comment;
                existing.Confidence = response.Confidence;
                existing.Source = GradingSource.Ai;
                existing.GradedAt = DateTime.Now;
            }
            else
            {
                _db.QuestionResults.Add(new QuestionResult
                {
                    SubmissionId = submissionId,
                    QuestionId = q.Id,
                    RecognizedAnswer = recognized,
                    Score = response.Score,
                    Comment = response.Comment,
                    Confidence = response.Confidence,
                    Source = GradingSource.Ai,
                    GradedAt = DateTime.Now,
                });
            }
            graded++;
        }

        if (graded > 0)
        {
            var hasUndecided = await _db.QuestionResults
                .AnyAsync(r => r.SubmissionId == submissionId && r.Score == null);
            submission.Status = (hasUndecided || lowestConfidence < settings.HumanReviewThreshold)
                ? SubmissionStatus.NeedsHuman
                : SubmissionStatus.AiGraded;
            submission.AiGradedAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }

        return Ok(new { graded, status = submission.Status.ToString() });
    }

    /// <summary>单题 AI 批改（保留旧端点语义，供逐题重批）。</summary>
    [HttpPost("submissions/{submissionId}/grade/{questionId}")]
    public async Task<IActionResult> AiGrade(string submissionId, string questionId, [FromBody] AiGradingRequest request)
    {
        var response = await _ai.GradeSubjectiveAsync(request);
        if (response == null) return BadRequest("AI 批改失败，请检查 AI 服务配置");

        var existing = await _db.QuestionResults
            .FirstOrDefaultAsync(r => r.SubmissionId == submissionId && r.QuestionId == questionId);
        AppendHistory(existing, response.Score, response.Comment, GradingSource.Ai);
        if (existing != null)
        {
            existing.Score = response.Score;
            existing.Comment = response.Comment;
            existing.Confidence = response.Confidence;
            existing.Source = GradingSource.Ai;
            existing.GradedAt = DateTime.Now;
        }
        else
        {
            _db.QuestionResults.Add(new QuestionResult
            {
                SubmissionId = submissionId,
                QuestionId = questionId,
                RecognizedAnswer = request.StudentAnswer,
                Score = response.Score,
                Comment = response.Comment,
                Confidence = response.Confidence,
                Source = GradingSource.Ai,
                GradedAt = DateTime.Now,
            });
        }

        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission != null)
        {
            var settings = await _aiSettings.LoadAsync();
            submission.Status = response.Confidence < settings.HumanReviewThreshold
                ? SubmissionStatus.NeedsHuman
                : SubmissionStatus.AiGraded;
            submission.AiGradedAt = DateTime.Now;
        }
        await _db.SaveChangesAsync();
        return Ok(new { score = response.Score, comment = response.Comment, confidence = response.Confidence });
    }

    /// <summary>逐题结果（含题干/标准答案/满分，批改工作台展示用）。</summary>
    [HttpGet("submissions/{submissionId}/results")]
    public async Task<IActionResult> Results(string submissionId)
    {
        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission == null) return NotFound();
        var questions = await _db.Questions
            .Where(q => q.PaperId == submission.PaperId)
            .OrderBy(q => q.Index)
            .ToListAsync();
        var results = await _db.QuestionResults
            .Where(r => r.SubmissionId == submissionId)
            .ToDictionaryAsync(r => r.QuestionId);

        return Ok(questions.Select(q =>
        {
            results.TryGetValue(q.Id, out var r);
            return new
            {
                questionId = q.Id, q.Index, q.Type, q.Content, q.StandardAnswer,
                fullScore = q.Score, q.Rubric, q.AiGradingEnabled,
                resultId = r?.Id,
                recognizedAnswer = r?.RecognizedAnswer,
                score = r?.Score,
                comment = r?.Comment,
                confidence = r?.Confidence,
                source = r?.Source.ToString(),
                gradedAt = r?.GradedAt,
            };
        }));
    }

    /// <summary>教师复判/改分（Source=Teacher，原结果进 HistoryJson 留痕）。</summary>
    [HttpPut("submissions/{submissionId}/results/{questionId}")]
    public async Task<IActionResult> OverrideResult(
        string submissionId, string questionId, [FromBody] OverrideResultRequest req)
    {
        var existing = await _db.QuestionResults
            .FirstOrDefaultAsync(r => r.SubmissionId == submissionId && r.QuestionId == questionId);
        if (existing == null) return NotFound();

        AppendHistory(existing, existing.Score, existing.Comment, existing.Source);
        existing.Score = req.Score;
        existing.Comment = req.Comment;
        existing.Confidence = null;
        existing.Source = GradingSource.Teacher;
        existing.GradedAt = DateTime.Now;

        // 教师介入后进入待人工队列，仍需显式"确认"才计入成绩
        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission != null && submission.Status != SubmissionStatus.Confirmed)
            submission.Status = SubmissionStatus.NeedsHuman;

        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    /// <summary>标记整份提交为待人工复判。</summary>
    [HttpPost("submissions/{submissionId}/review")]
    public async Task<IActionResult> MarkNeedsHuman(string submissionId)
    {
        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission == null) return NotFound();
        submission.Status = SubmissionStatus.NeedsHuman;
        await _db.SaveChangesAsync();
        return Ok(new { status = submission.Status.ToString() });
    }

    /// <summary>确认绑定学生（考号识别结果须教师确认后才生效）。</summary>
    [HttpPost("submissions/{submissionId}/bind-student")]
    public async Task<IActionResult> BindStudent(string submissionId, [FromBody] BindStudentRequest req)
    {
        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission == null) return NotFound();
        var stu = await _db.Students.FindAsync(req.StudentId);
        if (stu == null) return NotFound("学生不存在");
        submission.StudentId = stu.Id;
        await _db.SaveChangesAsync();
        return Ok(new { submission.StudentId, studentName = stu.Name });
    }

    /// <summary>教师确认分数（状态机推进到已确认，汇总总分；只有已确认计入成绩）。</summary>
    [HttpPost("submissions/{submissionId}/confirm")]
    public async Task<IActionResult> Confirm(string submissionId)
    {
        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission == null) return NotFound();

        submission.Status = SubmissionStatus.Confirmed;
        submission.ConfirmedAt = DateTime.Now;

        var results = _db.QuestionResults.Where(r => r.SubmissionId == submissionId);
        submission.TotalScore = await results.SumAsync(r => r.Score ?? 0);

        await _db.SaveChangesAsync();
        return Ok(new { submission.TotalScore, status = submission.Status.ToString() });
    }

    // ── 成绩统计 ──

    /// <summary>成绩统计：按学生总分 / 按题得分率（仅统计已确认提交）。</summary>
    [HttpGet("papers/{paperId}/statistics")]
    public async Task<IActionResult> Statistics(string paperId)
    {
        var paper = await _db.ExamPapers.FindAsync(paperId);
        if (paper == null) return NotFound();

        var questions = await _db.Questions
            .Where(q => q.PaperId == paperId)
            .OrderBy(q => q.Index)
            .ToListAsync();
        var submissions = await _db.AnswerSheetSubmissions
            .Where(s => s.PaperId == paperId && s.Status == SubmissionStatus.Confirmed)
            .ToListAsync();
        var results = await _db.QuestionResults
            .Where(r => _db.AnswerSheetSubmissions.Any(s => s.Id == r.SubmissionId && s.PaperId == paperId && s.Status == SubmissionStatus.Confirmed))
            .ToListAsync();

        var studentIds = submissions.Where(s => s.StudentId != null).Select(s => s.StudentId!).Distinct().ToList();
        var students = await _db.Students.Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        var perStudent = submissions
            .Where(s => s.StudentId != null)
            .GroupBy(s => s.StudentId!)
            .Select(g => new
            {
                studentId = g.Key,
                studentName = students.TryGetValue(g.Key, out var n) ? n : "",
                totalScore = g.Max(s => s.TotalScore ?? 0),
                attempts = g.Count(),
            })
            .OrderByDescending(x => x.totalScore)
            .ToList();

        var perQuestion = questions.Select(q =>
        {
            var qResults = results.Where(r => r.QuestionId == q.Id && r.Score != null).ToList();
            var full = qResults.Count(r => r.Score >= q.Score);
            return new
            {
                questionId = q.Id, q.Index, q.Type, fullScore = q.Score,
                answerCount = qResults.Count,
                avgScore = qResults.Count == 0 ? 0 : Math.Round(qResults.Average(r => r.Score!.Value), 2),
                scoreRate = qResults.Count == 0 ? 0 : Math.Round(qResults.Sum(r => r.Score!.Value) / (q.Score * qResults.Count) * 100, 1),
                fullScoreCount = full,
            };
        }).ToList();

        var totals = perStudent.Select(x => x.totalScore).ToList();
        return Ok(new
        {
            paperId, paperTitle = paper.Title, totalPaperScore = paper.TotalScore,
            confirmedCount = submissions.Count,
            pendingCount = await _db.AnswerSheetSubmissions.CountAsync(s => s.PaperId == paperId && s.Status != SubmissionStatus.Confirmed),
            avgScore = totals.Count == 0 ? 0 : Math.Round(totals.Average(), 2),
            maxScore = totals.Count == 0 ? 0 : totals.Max(),
            minScore = totals.Count == 0 ? 0 : totals.Min(),
            passRate = totals.Count == 0 ? 0 : Math.Round(totals.Count(t => t >= paper.TotalScore * 0.6) * 100.0 / totals.Count, 1),
            perStudent,
            perQuestion,
        });
    }

    /// <summary>班级成绩单导出（CSV，UTF-8 BOM，Excel 可直接打开）。</summary>
    [HttpGet("papers/{paperId}/export")]
    public async Task<IActionResult> ExportCsv(string paperId)
    {
        var statistics = await Statistics(paperId) as OkObjectResult;
        if (statistics?.Value == null) return NotFound();
        var json = System.Text.Json.JsonSerializer.Serialize(statistics.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        var questions = root.GetProperty("perQuestion");
        var sb = new StringBuilder();
        sb.Append("学生,总分");
        foreach (var q in questions.EnumerateArray()) sb.Append($",第{q.GetProperty("index").GetInt32() + 1}题");
        sb.AppendLine();

        // 逐生逐题从数据库直接取，保证列对齐
        var submissions = await _db.AnswerSheetSubmissions
            .Where(s => s.PaperId == paperId && s.Status == SubmissionStatus.Confirmed && s.StudentId != null)
            .ToListAsync();
        var results = await _db.QuestionResults
            .Where(r => _db.AnswerSheetSubmissions.Any(s => s.Id == r.SubmissionId && s.PaperId == paperId && s.Status == SubmissionStatus.Confirmed))
            .ToListAsync();
        var qIds = questions.EnumerateArray().Select(q => q.GetProperty("questionId").GetString()!).ToList();
        var studentIds = submissions.Select(s => s.StudentId!).Distinct().ToList();
        var students = await _db.Students.Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => $"{s.Name}({s.StudentNo})");

        foreach (var s in submissions.GroupBy(x => x.StudentId))
        {
            var sid = s.Key!;
            sb.Append(EscapeCsv(students.TryGetValue(sid, out var name) ? name : sid));
            sb.Append(',');
            sb.Append(s.Max(x => x.TotalScore ?? 0).ToString("0.##"));
            foreach (var qId in qIds)
            {
                var score = results.Where(r => r.SubmissionId != null && s.Any(x => x.Id == r.SubmissionId) && r.QuestionId == qId)
                    .Max(r => r.Score);
                sb.Append(',').Append(score?.ToString("0.##") ?? "");
            }
            sb.AppendLine();
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv; charset=utf-8",
            $"成绩单_{paperId[..Math.Min(8, paperId.Length)]}_{DateTime.Now:yyyyMMddHHmm}.csv");
    }

    // ── helpers ──

    private async Task RefreshPaperTotalAsync(string paperId)
    {
        var total = await _db.Questions.Where(q => q.PaperId == paperId).SumAsync(q => q.Score);
        var paper = await _db.ExamPapers.FindAsync(paperId);
        if (paper != null)
        {
            paper.TotalScore = total;
            await _db.SaveChangesAsync();
        }
    }

    private static bool IsObjective(QuestionType type)
        => type is QuestionType.SingleChoice or QuestionType.MultipleChoice or QuestionType.Judge;

    /// <summary>客观题答案归一化：去空格、大写、多选题按字母排序后比较。</summary>
    private static string NormalizeAnswer(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return "";
        var cleaned = new string(answer.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return string.Concat(cleaned.OrderBy(c => c));
    }

    private static void AppendHistory(QuestionResult? existing, double? score, string? comment, GradingSource source)
    {
        if (existing == null) return;
        var entry = System.Text.Json.JsonSerializer.Serialize(new
        {
            score, comment, source = source.ToString(), gradedAt = DateTime.Now,
        });
        existing.HistoryJson = string.IsNullOrEmpty(existing.HistoryJson)
            ? $"[{entry}]"
            : existing.HistoryJson.Insert(existing.HistoryJson.Length - 1, $",{entry}");
    }

    private static string EscapeCsv(string? value)
    {
        value ??= "";
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}

public record OverrideResultRequest(double Score, string? Comment);
public record BindStudentRequest(string StudentId);
