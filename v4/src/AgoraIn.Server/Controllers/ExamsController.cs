using System.Text;
using System.Text.Json;
using AgoraIn.Core.Domain;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServerPaths _paths;
    private readonly GradingPolicyService _gradingPolicy;
    private readonly SheetLayoutService _sheetLayout;

    public ExamsController(ServerDbContext db, DeepSeekGradingService ai, AiSettingsService aiSettings,
        IServiceScopeFactory scopeFactory, ServerPaths paths, GradingPolicyService gradingPolicy,
        SheetLayoutService sheetLayout)
    {
        _db = db;
        _ai = ai;
        _aiSettings = aiSettings;
        _scopeFactory = scopeFactory;
        _paths = paths;
        _gradingPolicy = gradingPolicy;
        _sheetLayout = sheetLayout;
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

    /// <summary>
    /// 可视化编辑器的版面数据：每道题在自动排版下的落位（mm，纸面坐标系），
    /// 供编辑页画拖拽框；已被拖动过的题带 pinned=true，坐标为覆盖值。
    /// </summary>
    [HttpGet("papers/{paperId}/sheet-layout")]
    public async Task<IActionResult> GetSheetLayout(string paperId, [FromQuery] string? paper, [FromQuery] string? idArea, [FromQuery] bool? notes)
    {
        var (questions, optionKeys) = await LoadPaperQuestionsAsync(paperId);
        if (questions.Count == 0) return NotFound(new { error = "试卷没有题目" });
        var opt = ParseSheetOptions(paper, idArea, notes);
        var placements = (await _sheetLayout.LoadAsync(paperId)).ToDictionary(p => p.QuestionId);
        var pages = AnswerSheetLayout.Compute(questions, optionKeys, opt, placements);
        // 只有"缩放"（SizeOnly）的题留在自动流里，它们的位置由重排决定；"拖动"的题才是绝对定位
        var sized = placements.Where(kv => kv.Value.SizeOnly).Select(kv => kv.Key).ToHashSet();

        var byQid = questions.ToDictionary(q => q.Id);
        return Ok(new
        {
            paperId,
            columns = opt.Columns,
            paperWidthMm = opt.Paper.WidthMm,
            paperHeightMm = opt.Paper.HeightMm,
            pageCount = pages.Count,
            // 注意：模型里已经包含覆盖项（它们在 .pinned 位置），所以这里要跳过被覆盖的题，
            // 否则同一道题会出现两个条目（编辑器画出两个框、拖动targeting 也乱）
            items = pages.SelectMany(pg => (pg.Rows ?? [])
                    .Select(r => new { Key = r.QuestionIndex, B = r.Box })
                    .Concat(pg.Frames.Select(f => new { Key = f.QuestionIndex, B = f.Box }))
                    .Where(x => !placements.ContainsKey(questions.First(q => q.Index == x.Key).Id))
                    .Select(x => new
                    {
                        questionId = questions.First(q => q.Index == x.Key).Id,
                        questionNo = x.Key + 1,
                        type = questions.First(q => q.Index == x.Key).Type.ToString(),
                        page = pg.PageNo,
                        // 客观题：气泡行整体外框（含左侧题号）；其余：作答框
                        kind = IsObjective(questions.First(q => q.Index == x.Key).Type) ? "objective" : "frame",
                        x = x.B.Xmm,
                        y = x.B.Ymm,
                        w = x.B.Wmm,
                        h = x.B.Hmm,
                        pinned = false,
                        sizeOnly = sized.Contains(questions.First(q => q.Index == x.Key).Id),
                    }))
                .Concat(placements.Values.Where(pl => byQid.ContainsKey(pl.QuestionId)).Select(pl => new
                {
                    questionId = pl.QuestionId,
                    questionNo = byQid[pl.QuestionId].Index + 1,
                    type = byQid[pl.QuestionId].Type.ToString(),
                    page = pl.PageNo,
                    kind = IsObjective(byQid[pl.QuestionId].Type) ? "objective" : "frame",
                    x = pl.Xmm,
                    y = pl.Ymm,
                    w = pl.Wmm,
                    h = pl.Hmm,
                    pinned = !pl.SizeOnly,
                    sizeOnly = pl.SizeOnly,
                })),
        });
    }

    /// <summary>保存可视化编辑的版面覆盖（整份替换）；传空数组即恢复全自动排版。</summary>
    [HttpPut("papers/{paperId}/sheet-layout")]
    public async Task<IActionResult> SaveSheetLayout(string paperId, [FromBody] List<QuestionPlacement> items)
    {
        var (questions, _) = await LoadPaperQuestionsAsync(paperId);
        if (questions.Count == 0) return NotFound(new { error = "试卷没有题目" });
        var valid = questions.Select(q => q.Id).ToHashSet();
        // 只接受本试卷的题（防止把别的卷的覆盖塞进来）
        await _sheetLayout.SaveAsync(paperId, items.Where(i => valid.Contains(i.QuestionId)));
        var saved = await _sheetLayout.LoadAsync(paperId);
        return Ok(new { saved = saved.Count });
    }

    /// <summary>试卷题目 + 选项键（渲染/版面/编辑三处共用）。</summary>
    private async Task<(List<Question> Questions, Dictionary<string, List<string>> OptionKeys)> LoadPaperQuestionsAsync(string paperId)
    {
        var questions = await _db.Questions.Where(q => q.PaperId == paperId).OrderBy(q => q.Index).ToListAsync();
        return (questions, BuildOptionKeys(questions));
    }

    /// <summary>答题卡纸张/考号区/注意事项参数（渲染端点与编辑器共用同一套解析）。</summary>
    internal static AnswerSheetOptions ParseSheetOptions(string? paper, string? idArea, bool? notes)
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

    /// <summary>删除一份扫卡答卷：连同逐题结果与已落盘的原图一起清掉。
    /// 用于扫错卷（拍了别班/别科的卡）、照片拍坏、重复扫描留下的垃圾记录；
    /// 已确认出分的答卷不允许删（成绩已计入统计），要删先退回。
    /// </summary>
    [HttpDelete("submissions/{submissionId}")]
    public async Task<IActionResult> DeleteSubmission(string submissionId)
    {
        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission == null) return NotFound(new { error = "提交记录不存在" });
        if (submission.Status == SubmissionStatus.Confirmed)
            return BadRequest(new { error = "该答卷已确认出分并计入统计，不能删除；如需重扫请先撤销确认" });

        var results = await _db.QuestionResults.Where(r => r.SubmissionId == submissionId).ToListAsync();
        _db.QuestionResults.RemoveRange(results);

        var deletedFiles = 0;
        foreach (var name in StoredImages(submission))
        {
            try
            {
                var full = Path.Combine(_paths.SheetDirectory, name);
                if (System.IO.File.Exists(full)) { System.IO.File.Delete(full); deletedFiles++; }
            }
            catch { /* 原图删不掉不阻断记录删除：记录才是老师要清掉的东西 */ }
        }

        _db.AnswerSheetSubmissions.Remove(submission);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true, questionResults = results.Count, images = deletedFiles });
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

    /// <summary>
    /// 上传试卷 AI 自动识别题目并生成答题卡题目。三种输入（便于教师）：
    ///  · questionImages：试卷页面图片（前端 pdf.js 把 PDF 渲染成页图后上传；数学公式文字层乱码的问题由此绕过）
    ///  · questionFile：docx/txt 文本文件（PDF 文本层不可靠，不建议）
    ///  · 答案：answerImages（答案页图片）或 answerFile（文本），可选——答案与题目同文件时不用传
    /// </summary>
    [HttpPost("papers/{paperId}/import-file")]
    [RequestSizeLimit(60 * 1024 * 1024)]
    public async Task<IActionResult> ImportFile(
        string paperId,
        [FromForm] List<IFormFile>? questionImages,
        [FromForm] List<IFormFile>? answerImages,
        [FromForm] IFormFile? questionFile,
        [FromForm] IFormFile? answerFile,
        CancellationToken ct)
    {
        var qImgs = (questionImages ?? []).Where(f => f.Length > 0).ToList();
        var aImgs = (answerImages ?? []).Where(f => f.Length > 0).ToList();
        if (qImgs.Count == 0 && (questionFile == null || questionFile.Length == 0))
            return BadRequest(new { error = "请上传试卷文件或试卷页面图片" });
        if (qImgs.Count > 12 || aImgs.Count > 12)
            return BadRequest(new { error = "页面图片最多 12 张，请拆分后分次导入" });

        // ── 图片路径：立即回执，后台处理 ──
        // 推理模型分批识别全流程可能耗时 5-10 分钟，挂在 HTTP 请求上会被
        // 浏览器/axios 超时断开（499），且断开还会取消 CancellationToken 中止处理。
        // 改为接收图片后立刻返回 accepted，后台任务独立作用域处理，前端轮询题目出现。
        if (qImgs.Count > 0)
        {
            var qBytes = new List<byte[]>();
            foreach (var f in qImgs)
            {
                using var ms = new MemoryStream();
                await f.CopyToAsync(ms, ct);
                qBytes.Add(ms.ToArray());
            }
            var aBytes = new List<byte[]>();
            foreach (var f in aImgs)
            {
                using var ms = new MemoryStream();
                await f.CopyToAsync(ms, ct);
                aBytes.Add(ms.ToArray());
            }

            _ = Task.Run(() => ProcessImportInBackgroundAsync(paperId, qBytes, aBytes));
            return Accepted(new { accepted = true, paperId, mode = "background" });
        }

        // ── 文本路径（docx/txt）：同步处理（AI 调用快，无超时风险） ──
        var questionText = await ExtractTextAsync(questionFile!, ct);
        if (string.IsNullOrWhiteSpace(questionText))
            return BadRequest(new { error = "无法从文件中提取文本。若为 PDF（尤其是含公式的数学卷），请使用页面图片方式：前端会自动把 PDF 渲染为图片上传" });

        var answerText = answerFile is { Length: > 0 } ? await ExtractTextAsync(answerFile, ct) : null;
        var extracted = await _ai.ExtractQuestionsAsync(questionText, answerText, ct);
        if (extracted == null)
            return BadRequest(new { error = "AI 解析失败，请检查 AI 服务配置（需在「AI 批改设置」填写 API 密钥）" });

        if (extracted.Count == 0)
            return BadRequest(new { error = "AI 识别到试卷但未解析出题目。常见原因：①页面图片为空白（PDF 渲染失败，请按 Ctrl+F5 强制刷新浏览器后重试）②识别模型不支持图像输入 ③试卷为扫描件图片（非文字版）。原始 AI 响应已存服务器 data/ai-raw/ 供排查" });

        var startIndex = await _db.Questions
            .Where(q => q.PaperId == paperId)
            .Select(q => (int?)q.Index)
            .MaxAsync(ct) ?? -1;

        var created = new List<Question>();
        foreach (var eq in extracted)
        {
            var q = new Question
            {
                PaperId = paperId,
                Index = ++startIndex,
                Type = ParseQuestionType(eq.Type),
                Content = eq.Content,
                Score = eq.Score > 0 ? eq.Score : 2,
                StandardAnswer = eq.StandardAnswer,
                OptionsJson = eq.Options is { Count: > 0 }
                    ? JsonSerializer.Serialize(eq.Options.Select(o => new { key = o.Key, text = o.Text ?? "" }).ToList())
                    : null,
                Rubric = eq.Rubric,
                KnowledgeTagsJson = eq.KnowledgeTags is { Count: > 0 } ? JsonSerializer.Serialize(eq.KnowledgeTags) : null,
                AiGradingEnabled = null,
            };
            _db.Questions.Add(q);
            created.Add(q);
        }
        await _db.SaveChangesAsync(ct);
        await RefreshPaperTotalAsync(paperId);

        return Ok(new
        {
            imported = created.Count,
            questions = created.OrderBy(q => q.Index).Select(q => new
            {
                q.Id, q.Index, type = q.Type.ToString(), q.Content, q.Score,
                q.StandardAnswer, q.Rubric,
            }),
        });
    }

    /// <summary>
    /// 分批视觉出题（前端逐批上传页面图片，实时展示）：每批 ≤3 张，
    /// 立即解析入库并返回本批 AI 原文（前端实时展示），题号按 startNumber 连续。
    /// </summary>
    [HttpPost("papers/{paperId}/extract-batch")]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> ExtractBatch(
        string paperId, [FromForm] List<IFormFile>? images,
        [FromQuery] int startNumber = 1, CancellationToken ct = default)
    {
        if (images == null || images.Count == 0 || images.Count > 4)
            return BadRequest(new { error = "每批请上传 1-4 张页面图片" });

        var bytes = new List<byte[]>();
        foreach (var f in images)
        {
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            bytes.Add(ms.ToArray());
        }

        var (items, raw) = await _ai.ExtractOneBatchAsync(bytes, Math.Max(1, startNumber), ct);
        if (items == null || items.Count == 0)
            return BadRequest(new
            {
                error = "AI 未识别出题目（可能页面空白或模型不支持图像，详见调用日志）",
                raw = raw != null ? raw[..Math.Min(400, raw.Length)] : null,
            });

        var startIndex = await _db.Questions
            .Where(q => q.PaperId == paperId)
            .Select(q => (int?)q.Index)
            .MaxAsync(ct) ?? -1;

        var created = new List<Question>();
        foreach (var eq in items)
        {
            var q = new Question
            {
                PaperId = paperId,
                Index = ++startIndex,
                Type = ParseQuestionType(eq.Type),
                Content = eq.Content,
                Score = eq.Score > 0 ? eq.Score : 2,
                StandardAnswer = eq.StandardAnswer,
                OptionsJson = eq.Options is { Count: > 0 }
                    ? JsonSerializer.Serialize(eq.Options.Select(o => new { key = o.Key, text = o.Text ?? "" }).ToList())
                    : null,
                Rubric = eq.Rubric,
                KnowledgeTagsJson = eq.KnowledgeTags is { Count: > 0 } ? JsonSerializer.Serialize(eq.KnowledgeTags) : null,
                AiGradingEnabled = null,
            };
            _db.Questions.Add(q);
            created.Add(q);
        }
        await _db.SaveChangesAsync(ct);
        await RefreshPaperTotalAsync(paperId);

        return Ok(new
        {
            imported = created.Count,
            raw = raw ?? "",
            questions = created.OrderBy(q => q.Index).Select(q => new
            {
                q.Id, q.Index, type = q.Type.ToString(), q.Content, q.Score, q.StandardAnswer, q.Rubric,
            }),
        });
    }

    /// <summary>分批答案回填：上传答案页图片（≤5 张），把答案与评分细则按题号回填到已导入的题目。</summary>
    [HttpPost("papers/{paperId}/fill-batch")]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> FillBatch(string paperId, [FromForm] List<IFormFile>? images, CancellationToken ct = default)
    {
        if (images == null || images.Count == 0 || images.Count > 6)
            return BadRequest(new { error = "每批请上传 1-6 张答案页图片" });

        var questions = await _db.Questions
            .Where(q => q.PaperId == paperId)
            .OrderBy(q => q.Index)
            .ToListAsync(ct);
        if (questions.Count == 0) return BadRequest(new { error = "请先完成题目导入" });

        var eqList = questions.Select(q => new ExtractedQuestion
        {
            Index = q.Index + 1,
            Content = (q.Content ?? "")[..Math.Min(60, q.Content?.Length ?? 0)],
            StandardAnswer = q.StandardAnswer,
            Rubric = q.Rubric,
        }).ToList();

        var bytes = new List<byte[]>();
        foreach (var f in images)
        {
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            bytes.Add(ms.ToArray());
        }

        var filled = await _ai.FillAnswersFromImagesAsync(eqList, bytes, ct);
        var updated = 0;
        foreach (var f in filled ?? [])
        {
            var q = questions.FirstOrDefault(x => x.Index == f.Index - 1);
            if (q == null) continue;
            if (!string.IsNullOrWhiteSpace(f.StandardAnswer)) { q.StandardAnswer = f.StandardAnswer; updated++; }
            if (!string.IsNullOrWhiteSpace(f.Rubric)) q.Rubric = f.Rubric;
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { updated });
    }

    /// <summary>AI 生成标准答案与评分要点（只处理缺失项；开启 AI 后主观题阅卷提示词随 rubric 自动生效）。</summary>
    [HttpPost("papers/{paperId}/ai-generate-answers")]
    public async Task<IActionResult> AiGenerateAnswers(string paperId, CancellationToken ct)
    {
        var questions = await _db.Questions
            .Where(q => q.PaperId == paperId)
            .OrderBy(q => q.Index)
            .ToListAsync(ct);
        if (questions.Count == 0) return NotFound(new { error = "试卷暂无题目" });

        var updated = 0;
        foreach (var q in questions)
        {
            var needAnswer = string.IsNullOrWhiteSpace(q.StandardAnswer);
            var needRubric = q.Type >= QuestionType.Blank && string.IsNullOrWhiteSpace(q.Rubric);
            if (!needAnswer && !needRubric) continue;

            var result = await _ai.GenerateAnswerAsync(q.Type.ToString(), q.Content, q.Score, q.Rubric, ct);
            if (result == null) continue;

            if (needAnswer && !string.IsNullOrWhiteSpace(result.Value.StandardAnswer))
                q.StandardAnswer = result.Value.StandardAnswer;
            if (needRubric && !string.IsNullOrWhiteSpace(result.Value.Rubric))
                q.Rubric = result.Value.Rubric;
            updated++;
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { updated, total = questions.Count });
    }

    /// <summary>
    /// 后台出题任务：独立 DI 作用域 + CancellationToken.None（客户端断开不中断）。
    /// 失败时把原因写入 AiCallLogs（Endpoint=import_background）供排查。
    /// </summary>
    private async Task ProcessImportInBackgroundAsync(string paperId, List<byte[]> qBytes, List<byte[]> aBytes)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
            var ai = scope.ServiceProvider.GetRequiredService<DeepSeekGradingService>();
            var ct = CancellationToken.None;

            var extracted = await ai.ExtractQuestionsFromImagesAsync(qBytes, null, ct);
            if (extracted != null && aBytes.Count > 0)
                extracted = await ai.FillAnswersFromImagesAsync(extracted, aBytes, ct) ?? extracted;

            if (extracted == null || extracted.Count == 0)
            {
                await LogBackgroundAsync(db, "import_background", false,
                    "AI 未识别出题目（可能原因：页面图片空白/模型不支持图像/扫描件），详见 data/ai-raw/");
                return;
            }

            var startIndex = await db.Questions
                .Where(q => q.PaperId == paperId)
                .Select(q => (int?)q.Index)
                .MaxAsync(ct) ?? -1;

            foreach (var eq in extracted)
            {
                db.Questions.Add(new Question
                {
                    PaperId = paperId,
                    Index = ++startIndex,
                    Type = ParseQuestionType(eq.Type),
                    Content = eq.Content,
                    Score = eq.Score > 0 ? eq.Score : 2,
                    StandardAnswer = eq.StandardAnswer,
                    OptionsJson = eq.Options is { Count: > 0 }
                        ? JsonSerializer.Serialize(eq.Options.Select(o => new { key = o.Key, text = o.Text ?? "" }).ToList())
                        : null,
                    Rubric = eq.Rubric,
                    KnowledgeTagsJson = eq.KnowledgeTags is { Count: > 0 } ? JsonSerializer.Serialize(eq.KnowledgeTags) : null,
                    AiGradingEnabled = null,
                });
            }
            await db.SaveChangesAsync(ct);

            var total = await db.Questions.Where(q => q.PaperId == paperId).SumAsync(q => q.Score);
            var paper = await db.ExamPapers.FindAsync([paperId], ct);
            if (paper != null)
            {
                paper.TotalScore = total;
                await db.SaveChangesAsync(ct);
            }

            await LogBackgroundAsync(db, "import_background", true, $"后台导入 {extracted.Count} 题");
        }
        catch (Exception ex)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
                await LogBackgroundAsync(db, "import_background", false,
                    ex.Message.Length > 300 ? ex.Message[..300] : ex.Message);
            }
            catch { }
        }
    }

    private static async Task LogBackgroundAsync(ServerDbContext db, string endpoint, bool success, string? error)
    {
        try
        {
            db.AiCallLogs.Add(new AiCallLogEntity
            {
                Endpoint = endpoint,
                Model = "background",
                Success = success,
                Error = error,
                DurationMs = 0,
            });
            await db.SaveChangesAsync();
        }
        catch { }
    }

    /// <summary>从上传文件提取文本（docx / pdf / txt）。</summary>
    private static async Task<string> ExtractTextAsync(IFormFile file, CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        if (ext == ".txt" || ext == ".md")
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());

        if (ext == ".docx")
        {
            // docx = zip：读 word/document.xml 并剥除 XML 标签
            using var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            var entry = archive.GetEntry("word/document.xml");
            if (entry == null) return "";
            await using var es = entry.Open();
            using var reader = new StreamReader(es);
            var xml = await reader.ReadToEndAsync(ct);
            var text = System.Text.RegularExpressions.Regex.Replace(xml, @"<w:p[ >]", "\n<w:p ")
                + "\n";
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");
            return System.Net.WebUtility.HtmlDecode(text)
                .Replace("\r", "").Trim();
        }

        if (ext == ".pdf")
        {
            var text = ExtractPdfText(ms.ToArray());
            return text;
        }

        return "";
    }

    /// <summary>
    /// 内置轻量 PDF 文本提取：解压 FlateDecode 内容流，解析 Tj/TJ 文本算子。
    /// 覆盖常见"文字层 PDF"；扫描件/图片型 PDF 无文字层时返回空，由调用方提示改用 docx/txt。
    /// </summary>
    private static string ExtractPdfText(byte[] bytes)
    {
        var raw = System.Text.Encoding.Latin1.GetString(bytes);
        var sb = new System.Text.StringBuilder();

        // 逐个 stream ... endstream 块尝试解压并提取文本
        var offset = 0;
        while (true)
        {
            var streamStart = raw.IndexOf("stream", offset, StringComparison.Ordinal);
            if (streamStart < 0) break;
            var dataStart = streamStart + "stream".Length;
            if (raw[dataStart] == '\r') dataStart++;
            if (raw[dataStart] == '\n') dataStart++;
            var dataEnd = raw.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (dataEnd < 0) break;
            offset = dataEnd + "endstream".Length;

            byte[] chunk;
            try
            {
                var src = System.Text.Encoding.Latin1.GetBytes(raw[dataStart..dataEnd]);
                using var input = new MemoryStream(src);
                using var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                chunk = output.ToArray();
            }
            catch
            {
                continue; // 未压缩或其他滤镜的流跳过
            }

            var content = System.Text.Encoding.Latin1.GetString(chunk);
            // 提取 (text) Tj / [(…) …] TJ / ' 与 " 算子中的可读文本
            foreach (var m in System.Text.RegularExpressions.Regex.Matches(content, @"\((?:\\.|[^\\()])*\)"))
            {
                var t = m.ToString()![1..^1]
                    .Replace("\\(", "(").Replace("\\)", ")").Replace("\\\\", "\\");
                sb.Append(t);
            }
            if (sb.Length > 0) sb.AppendLine();
        }

        var result = sb.ToString().Trim();
        // 启发式：乱码或无有效中文/字母时视为无可提取文字层
        if (result.Length < 10) return "";
        return result;
    }

    private static QuestionType ParseQuestionType(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "single" or "单选" or "singlechoice" => QuestionType.SingleChoice,
        "multiple" or "多选" or "multiplechoice" => QuestionType.MultipleChoice,
        "judge" or "判断" or "truefalse" => QuestionType.Judge,
        "blank" or "填空" or "fillintheblank" => QuestionType.Blank,
        "short" or "简答" or "shortanswer" => QuestionType.ShortAnswer,
        "essay" or "作文" => QuestionType.Essay,
        _ => QuestionType.SingleChoice,
    };

    /// <summary>从题库模板（IsTemplate=true 的试卷）复制题目到目标试卷。</summary>
    [HttpPost("papers/{paperId}/reuse/{templatePaperId}")]
    public async Task<IActionResult> ReuseQuestions(string paperId, string templatePaperId)
    {        var paper = await _db.ExamPapers.FindAsync(paperId);
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
            /** 是否有扫描原图（人工复盘要对照原卷改分） */
            hasImage = StoredImages(s).Count > 0,
            /** 已存页数（多页答题卡按页归并到同一份提交） */
            imageCount = StoredImages(s).Count,
        }));
    }

    /// <summary>提交概要（嵌入式批改页的标题栏用）。</summary>
    [HttpGet("submissions/{submissionId}/header")]
    public async Task<IActionResult> SubmissionHeader(string submissionId)
    {
        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission == null) return NotFound(new { error = "提交记录不存在" });
        var paper = await _db.ExamPapers.FindAsync(submission.PaperId);
        string? studentName = null;
        if (submission.StudentId != null)
        {
            var stu = await _db.Students.FindAsync(submission.StudentId);
            studentName = stu?.Name;
        }
        return Ok(new
        {
            submissionId = submission.Id,
            paperId = submission.PaperId,
            paperTitle = paper?.Title ?? "",
            studentName,
            studentRef = submission.StudentRef,
            status = submission.Status.ToString(),
        });
    }

    /// <summary>取某次提交的扫描原图（人工复盘时对照原卷；多页答卷用 ?page=N 选页）。</summary>
    [HttpGet("submissions/{submissionId}/image")]
    public async Task<IActionResult> GetSubmissionImage(string submissionId, [FromQuery] int page = 1)
    {
        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission == null) return NotFound(new { error = "提交记录不存在" });
        var images = StoredImages(submission);
        if (images.Count == 0) return NotFound(new { error = "这条记录没有存原图（早期上传或存图失败）" });
        var idx = Math.Clamp(page - 1, 0, images.Count - 1);
        var stored = images[idx];

        var full = Path.Combine(_paths.SheetDirectory, stored);
        if (!System.IO.File.Exists(full)) return NotFound(new { error = "原图文件已丢失" });

        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(full, "image/jpeg", enableRangeProcessing: true);
    }

    /// <summary>
    /// 人工复盘"本题切图"：把某道题的作答区域从扫描原图里裁出来（题号为卡面题号，1 起）。
    /// 依次尝试各页扫描图，找到含该题且定位成功的页做透视归正后裁剪；
    /// 全部失败（照片拍不全等）返回 404，前端回退显示整页原图。
    /// </summary>
    [HttpGet("submissions/{submissionId}/image/crop/{questionNo}")]
    public async Task<IActionResult> CropQuestionImage(string submissionId, int questionNo)
    {
        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission == null) return NotFound(new { error = "提交记录不存在" });
        var images = StoredImages(submission);
        if (images.Count == 0) return NotFound(new { error = "没有存原图" });

        var questions = await _db.Questions
            .Where(q => q.PaperId == submission.PaperId)
            .OrderBy(q => q.Index)
            .ToListAsync();
        var optionKeys = BuildOptionKeys(questions);
        // 老师拖过版面的卷：切图必须按覆盖后的坐标，否则切出来的位置会错
        var placements = (await _sheetLayout.LoadAsync(submission.PaperId)).ToDictionary(p => p.QuestionId);

        foreach (var name in images)
        {
            var full = Path.Combine(_paths.SheetDirectory, name);
            if (!System.IO.File.Exists(full)) continue;
            var bytes = await System.IO.File.ReadAllBytesAsync(full);
            var cropped = OmrRecognizer.CropQuestion(bytes, questions, optionKeys, questionNo - 1, placements);
            if (cropped != null)
            {
                Response.Headers.CacheControl = "private, max-age=86400";
                return File(cropped, "image/jpeg");
            }
        }
        return NotFound(new { error = "该题无法自动切图（照片定位失败），请对照整页原图" });
    }

    /// <summary>提交里「已落盘」的原图文件名（按页号升序）。只认本服务生成的 {提交Id}-p{页}.jpg 命名，
    /// 早期记录里存的原始文件名（如 answersheet.jpg）没有对应文件，不算。</summary>
    private static List<string> StoredImages(AnswerSheetSubmission submission)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(submission.ImagePathsJson)) return result;
        try
        {
            var names = System.Text.Json.JsonSerializer.Deserialize<List<string>>(submission.ImagePathsJson);
            if (names == null) return result;
            foreach (var n in names)
            {
                if (string.IsNullOrWhiteSpace(n) || n != Path.GetFileName(n)) continue;
                if (!System.Text.RegularExpressions.Regex.IsMatch(n,
                        @"^[0-9a-fA-F-]{36}-p\d+\.(jpg|jpeg|png)$")) continue;
                result.Add(n);
            }
            result.Sort((a, b) =>
            {
                var pa = int.Parse(System.Text.RegularExpressions.Regex.Match(a, @"-p(\d+)\.").Groups[1].Value);
                var pb = int.Parse(System.Text.RegularExpressions.Regex.Match(b, @"-p(\d+)\.").Groups[1].Value);
                return pa.CompareTo(pb);
            });
        }
        catch { /* JSON 损坏按无图处理 */ }
        return result;
    }

    /// <summary>
    /// 上传答题卡图片并触发识别（考号涂卡 + 客观题 OMR）。
    ///
    /// **多页答题卡**：一份答卷的各页应归并到同一条提交记录，而不是每页各建一条——
    ///   1. App 扫完第 1 页后带上 ?submissionId= 继续扫后续页（主流程，归属明确）；
    ///   2. 第 1 页重复扫/换设备再扫：同试卷、同考号、未确认的已有答卷会被复用（按考号去重）；
    ///   3. 不带 submissionId 的续页（网页单张上传）：当该试卷只有一份"缺这一页"的未完成答卷时
    ///      自动并入；有多份无法确定归属时不落库，提示按顺序扫描。
    /// 客观题识别后立即与标准答案比对自动判分；识别到考号时按考号回填学生（学号匹配）。
    /// </summary>
    [HttpPost("submissions")]
    public async Task<IActionResult> UploadSubmission(
        [FromForm] IFormFile file, [FromQuery] string paperId, [FromQuery] string? submissionId,
        [FromQuery] bool allowMissingId = false)
    {
        if (file == null || file.Length == 0) return BadRequest("请上传答题卡图片");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var imageData = ms.ToArray();

        var questions = await _db.Questions
            .Where(q => q.PaperId == paperId)
            .OrderBy(q => q.Index)
            .ToListAsync();
        var optionKeys = BuildOptionKeys(questions);
        // 识别同样按覆盖后的版面（拖过的题在它被拖到的地方采样）
        var placements = (await _sheetLayout.LoadAsync(paperId)).ToDictionary(p => p.QuestionId);

        // 先本地识别（切卡 + 客观题 + 考号 + 页码，不调大模型、不外发图像）；
        // 找不到四角定位标记（没拍全/太模糊）才回退到视觉模型。
        string recognizeSource = "ai";
        var local = OmrRecognizer.Recognize(imageData, questions, optionKeys, placements);
        OmrResult? omrResult;
        int pageNo = 1, totalPages = 1;
        if (local != null)
        {
            recognizeSource = "local";
            omrResult = new OmrResult
            {
                StudentRef = local.StudentRef,
                OverallConfidence = local.Confidence,
                Answers = local.Answers,
            };
            pageNo = local.PageNo;
            totalPages = local.TotalPages;
        }
        else
        {
            omrResult = await _ai.RecognizeAnswerSheetAsync(imageData);
        }

        // ── 扫卡准入闸门：认不出答题卡的页当场退回，不落库 ──
        // 以前一律建提交（还落盘原图），失败的一页会变成"考号空、答案空"的垃圾答卷混进批次，
        // 事后只能在批改页看到切图失败，分不清是照片拍坏了还是学生真没涂。
        // local != null 说明四个定位标记与版面都对上了；否则看视觉模型有没有读出答案。
        var identified = local != null || (omrResult?.Answers.Count ?? 0) > 0;
        if (!identified)
        {
            // recognizeDebug 带出本地识别看到的四角标记/匹配情况，便于现场排查（App 与网页都只显示 error）
            return BadRequest(new
            {
                code = "sheet_not_recognized",
                error = "未识别到答题卡：请拍全四个角的定位标记、避免反光与阴影后重扫。本次未录入",
                recognizeDebug = local?.Debug ?? OmrRecognizer.Diagnose(imageData, questions, optionKeys, placements),
            });
        }

        // 考号缺失只在"要靠考号认领归属的第 1 页"上拦截：续页带 submissionId 时归属已定，
        // 学生忘涂考号而教师仍要收卷的，由 App 二次确认后带 allowMissingId=true 重传。
        if (pageNo <= 1 && !allowMissingId && string.IsNullOrWhiteSpace(submissionId)
            && string.IsNullOrWhiteSpace(omrResult?.StudentRef))
        {
            return BadRequest(new
            {
                code = "student_ref_missing",
                error = "考号未识别/未填涂：无法归属到学生。请涂好考号后重扫，或在 App 上选择「仍然录入」并稍后手动绑定",
            });
        }

        // ── 定位这份答卷该归并到哪条提交（多页答题卡的核心）──
        var (target, isNew, resolveWarning) = await ResolveTargetSubmissionAsync(paperId, submissionId, pageNo, omrResult?.StudentRef);
        if (target == null)
        {
            // 无法确定归属（多份未完成答卷抢同一续页）：不落库，避免产生垃圾提交
            return Ok(new
            {
                submissionId = (string?)null,
                status = SubmissionStatus.NotGraded.ToString(),
                isNew = false,
                pageNo,
                totalPages,
                storedPages = 0,
                recognizedStudent = omrResult?.StudentRef,
                matchedStudentId = (string?)null,
                answers = new List<string>(),
                confidence = omrResult?.OverallConfidence,
                warning = resolveWarning,
                recognizeSource,
                recognizeDebug = local?.Debug,
                autoScored = 0,
            });
        }

        // ── 原图落盘：{提交Id}-p{页}.jpg，多页按页归并到同一份提交 ──
        var images = StoredImages(target);
        var page = local != null ? pageNo : images.Count + 1;   // 无页码信息时追加到末尾
        var storedName = $"{target.Id}-p{page}.jpg";
        try
        {
            await System.IO.File.WriteAllBytesAsync(Path.Combine(_paths.SheetDirectory, storedName), imageData);
            images.RemoveAll(n => n.Contains($"-p{page}.", StringComparison.OrdinalIgnoreCase));
            images.Add(storedName);
            images.Sort(ComparePageName);
            target.ImagePathsJson = System.Text.Json.JsonSerializer.Serialize(images);
        }
        catch
        {
            // 存图失败不阻断识别链路，只是复盘时看不到原卷
        }

        if (isNew) _db.AnswerSheetSubmissions.Add(target);   // 新答卷（续页归并的目标已在上下文中跟踪）

        // 考号 → 学号辅助匹配（结果须教师在批改工作台确认绑定）
        if (!string.IsNullOrEmpty(omrResult?.StudentRef))
        {
            if (string.IsNullOrEmpty(target.StudentRef)) target.StudentRef = omrResult.StudentRef;
            if (target.StudentId == null)
            {
                var stu = await _db.Students.FirstOrDefaultAsync(s => s.StudentNo == omrResult.StudentRef);
                if (stu != null) target.StudentId = stu.Id;
            }
        }

        var byIndex = questions.ToDictionary(q => q.Index);
        // 卡面印刷的是「题号」= Index + 1；识别结果按卡面题号回传，故按题号索引（旧代码用 Index 会整体错位一题）
        var byNo = questions.ToDictionary(q => q.Index + 1);
        var settings = await _aiSettings.LoadAsync();
        var lowestConfidence = 1.0;
        var autoScored = 0;

        if (omrResult != null)
        {
            foreach (var ans in omrResult.Answers)
            {
                // 兼容模型偶发返回 0 起编号：先按卡面题号取，取不到再按 0 起 Index 兜底
                if (!byNo.TryGetValue(ans.Index, out var q)
                    && !byIndex.TryGetValue(ans.Index, out q)) continue;

                // 同题已有结果（重复上传同一页 / 教师已改）：不重复插入
                var existing = await _db.QuestionResults.FirstOrDefaultAsync(r =>
                    r.SubmissionId == target.Id && r.QuestionId == q.Id);
                if (existing != null)
                {
                    if (existing.Source != GradingSource.Teacher)
                    {
                        existing.RecognizedAnswer = ans.Answer;
                        existing.Confidence = ans.Confidence;
                    }
                    continue;
                }

                lowestConfidence = Math.Min(lowestConfidence, ans.Confidence);

                if (IsAutoScored(q.Type) && q.AiGradingEnabled != false && !string.IsNullOrEmpty(q.StandardAnswer))
                {
                    // 客观题/填空题且教师未关闭自动判分：识别后与标准答案比对自动判分
                    var correct = NormalizeAnswer(ans.Answer) == NormalizeAnswer(q.StandardAnswer);
                    _db.QuestionResults.Add(new QuestionResult
                    {
                        SubmissionId = target.Id,
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
                    // 主观题，或教师指定人工判分（AiGradingEnabled=false）的客观题：
                    // 先存识别内容，等待教师阅卷（"待人工"）
                    _db.QuestionResults.Add(new QuestionResult
                    {
                        SubmissionId = target.Id,
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
                r.SubmissionId == target.Id && r.Score == null) > 0;
            target.Status = (hasUndecided || lowestConfidence < settings.HumanReviewThreshold)
                ? SubmissionStatus.NeedsHuman
                : SubmissionStatus.AiGraded;
            target.AiGradedAt = DateTime.Now;
        }
        else if (omrResult != null && omrResult.Answers.Count == 0 && isNew)
        {
            // 一道题都没识别出来（如卡面反光/未涂卡）：不要误判为「AI 已批 0 分」
            target.Status = SubmissionStatus.NeedsHuman;
        }

        await _db.SaveChangesAsync();

        // 识别没返回结果时，把最近的 AI 调用错误带回给 App：
        // 否则手机上只看到「没有识别结果」，分不清是没涂卡、没配密钥还是上游欠费。
        string? warning = resolveWarning;
        if (omrResult == null && warning == null)
        {
            var lastError = await _db.AiCallLogs
                .Where(l => !l.Success && l.Endpoint == "recognize")
                .OrderByDescending(l => l.Id)
                .Select(l => l.Error)
                .FirstOrDefaultAsync();
            warning = string.IsNullOrWhiteSpace(lastError)
                ? "AI 识别未返回结果：请检查 AI 设置（密钥/地址/模型）后重试"
                : $"AI 识别失败：{lastError}";
        }

        return Ok(new
        {
            submissionId = target.Id,
            status = target.Status.ToString(),
            isNew,
            pageNo,
            totalPages,
            storedPages = images.Count,
            recognizedStudent = target.StudentRef ?? omrResult?.StudentRef,
            matchedStudentId = target.StudentId,
            // 移动端扫卡结果回显：题号:答案（卡面题号，1 起）
            answers = omrResult?.Answers
                .OrderBy(a => a.Index)
                .Select(a => $"{a.Index}:{a.Answer}")
                .ToList() ?? [],
            confidence = omrResult?.OverallConfidence,
            warning,
            /** 识别来源：local=本地切卡识别（不花 token），ai=视觉模型 */
            recognizeSource,
            recognizeDebug = local?.Debug,

            autoScored,
        });
    }

    /// <summary>
    /// 多页归并的目标提交解析。
    /// 返回 (目标提交, 警告)；目标为 null 表示无法确定归属（调用方不落库）。
    /// 新建的提交只 new 了对象、未入库（Id 由调用方 Add + SaveChanges 落库）。
    /// </summary>
    private async Task<(AnswerSheetSubmission Target, bool IsNew, string? Warning)> ResolveTargetSubmissionAsync(
        string paperId, string? submissionId, int pageNo, string? studentRef)
    {
        // 1) App 链式上传：显式指定续传目标
        if (!string.IsNullOrWhiteSpace(submissionId))
        {
            var target = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
            if (target != null && target.PaperId == paperId && target.Status != SubmissionStatus.Confirmed)
            {
                // 该页带考号且与已有答卷的考号不同 → 这是下一份答卷的第 1 页，开新份而不是并进去
                var differentStudent = !string.IsNullOrEmpty(target.StudentRef)
                                       && !string.IsNullOrEmpty(studentRef)
                                       && target.StudentRef != studentRef;
                if (!differentStudent) return (target, false, null);
            }
            // 找不到 / 已确认 / 跨试卷 / 换了考生：静默开新份
        }

        // 2) 首页（带考号）：同试卷同考号已有未确认答卷 → 并入，避免重复扫描生成两条
        if (pageNo <= 1 && !string.IsNullOrEmpty(studentRef))
        {
            var cutoff = DateTime.Now.AddHours(-24);
            var dup = await _db.AnswerSheetSubmissions
                .Where(s => s.PaperId == paperId && s.Status != SubmissionStatus.Confirmed
                            && s.SubmittedAt >= cutoff && s.StudentRef == studentRef)
                .OrderByDescending(s => s.SubmittedAt)
                .FirstOrDefaultAsync();
            if (dup != null) return (dup, false, "该考号已有未完成的答卷，本页已并入（重复扫描不会产生重复记录）");
        }

        // 3) 无显式目标的续页（网页单张上传）：仅当该试卷恰好只有一份缺这一页的未完成答卷时自动并入
        if (pageNo > 1)
        {
            var cutoff = DateTime.Now.AddHours(-24);
            var open = await _db.AnswerSheetSubmissions
                .Where(s => s.PaperId == paperId && s.Status != SubmissionStatus.Confirmed
                            && s.SubmittedAt >= cutoff)
                .OrderByDescending(s => s.SubmittedAt)
                .ToListAsync();
            var missing = open.Where(s => StoredImages(s).Count < pageNo).ToList();
            if (missing.Count == 1) return (missing[0], false, null);
            if (missing.Count > 1)
                return (null!, false, $"扫到第 {pageNo} 页，但有 {missing.Count} 份未完成答卷，无法确定归属；" +
                                      "请在 App 中按 1→2→3 顺序连续扫描同一份，或先扫该生的第 1 页");
            return (null!, false, $"扫到第 {pageNo} 页，但没有找到未完成的答卷；请先扫该生的第 1 页");
        }

        return (new AnswerSheetSubmission { PaperId = paperId, Status = SubmissionStatus.NotGraded }, true, null);
    }

    /// <summary>题目选项字母表（本地 OMR 需要知道每题有几个气泡）。</summary>
    private static Dictionary<string, List<string>> BuildOptionKeys(List<Question> questions)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var q in questions)
        {
            if (string.IsNullOrEmpty(q.OptionsJson)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(q.OptionsJson);
                var keys = doc.RootElement.EnumerateArray()
                    .Select(o => o.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "")
                    .Where(x => x.Length > 0).ToList();
                if (keys.Count > 0) map[q.Id] = keys;
            }
            catch { /* 选项 JSON 损坏则按默认 ABCD 处理 */ }
        }
        return map;
    }

    /// <summary>按文件名里的 -p{N} 页号排序。</summary>
    private static int ComparePageName(string a, string b)
    {
        var pa = int.Parse(System.Text.RegularExpressions.Regex.Match(a, @"-p(\d+)\.").Groups[1].Value);
        var pb = int.Parse(System.Text.RegularExpressions.Regex.Match(b, @"-p(\d+)\.").Groups[1].Value);
        return pa.CompareTo(pb);
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
            // 客观题/填空题按标准答案自动判分，也不该再走 AI 评分要点
            if (q.AiGradingEnabled == false || IsAutoScored(q.Type)) continue;
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

        // 分题/双判：非特权教师只看到分给自己的题（批改分配 + 待仲裁的题）
        var (role, username) = CurrentRoleAndName();
        var isPrivileged = role is AppRoles.Admin or AppRoles.Owner;
        // 仲裁状态按「卡面题号」建。题目被删后会有孤儿结果——旧写法把它们全映射到 0，
        // 两条孤儿就撞键 500（现场抓到）；这里显式跳过孤儿结果。
        var arbitrationState = new Dictionary<int, bool>();
        foreach (var r in results)
        {
            var q = questions.FirstOrDefault(x => x.Id == r.Key);
            if (q != null) arbitrationState[q.Index + 1] = r.Value.NeedArbitration;
        }
        var assigned = await _gradingPolicy.AssignedQuestionNosAsync(
            submission.PaperId, submissionId, username, isPrivileged, arbitrationState);
        var visible = assigned == null
            ? questions
            : questions.Where(q => assigned.Contains(q.Index + 1)).ToList();

        return Ok(visible.Select(q =>
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
                // 双判 / 仲裁
                grader = r?.Grader,
                score2 = r?.Score2,
                grader2 = r?.Grader2,
                needArbitration = r?.NeedArbitration ?? false,
                arbiter = r?.Arbiter,
            };
        }));
    }

    /// <summary>当前请求的角色与用户名。</summary>
    private (string Role, string Username) CurrentRoleAndName()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        var name = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "";
        return (role, name);
    }

    /// <summary>
    /// 教师复判/改分（Source=Teacher，原结果进 HistoryJson 留痕）。
    /// 开启双判时：第一次提交记一判，第二位教师提交记二判；两判齐全后
    /// 取平均并**进位到 0.5**（GradingMath.RoundToHalfCeil：1.23→1.5，0.98→1）；
    /// 分差超过阈值 → 标记待仲裁，分数清空等待第三位资深教师裁定。
    /// </summary>
    [HttpPut("submissions/{submissionId}/results/{questionId}")]
    public async Task<IActionResult> OverrideResult(
        string submissionId, string questionId, [FromBody] OverrideResultRequest req)
    {
        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission == null) return NotFound(new { error = "答卷不存在（可能已被删除）" });
        var question = await _db.Questions.FindAsync(questionId);
        if (question == null)
            return NotFound(new { error = "该题已不在试卷中（试卷可能被重新导入/删改），无法保存此题的分数" });

        // 没有结果记录的题（试卷后加的题、或该题未参与识别）直接新建一条教师结果，而不是 404
        var existing = await _db.QuestionResults
            .FirstOrDefaultAsync(r => r.SubmissionId == submissionId && r.QuestionId == questionId);
        if (existing == null)
        {
            existing = new QuestionResult
            {
                SubmissionId = submissionId,
                QuestionId = questionId,
                RecognizedAnswer = null,
                Score = null,
                Source = GradingSource.Teacher,
            };
            _db.QuestionResults.Add(existing);
        }

        var (role, username) = CurrentRoleAndName();
        var isPrivileged = role is AppRoles.Admin or AppRoles.Owner;
        var policy = await _gradingPolicy.LoadPolicyAsync(submission.PaperId);

        AppendHistory(existing, existing.Score, existing.Comment, existing.Source);

        if (policy.DoubleGrading)
        {
            var questionNo = question.Index + 1;

            // 仲裁教师：该题待仲裁且本用户持有该题的仲裁分配（特权用户亦可仲裁）
            var arbiterScope = existing.NeedArbitration
                ? await _gradingPolicy.AssignedQuestionNosAsync(
                    submission.PaperId, submissionId, username, isPrivileged,
                    new Dictionary<int, bool> { [questionNo] = true })
                : null;
            if (existing.NeedArbitration && (isPrivileged || (arbiterScope?.Contains(questionNo) ?? false)))
            {
                existing.Score = Math.Clamp(req.Score, 0, question.Score);
                existing.Comment = req.Comment ?? existing.Comment;
                existing.NeedArbitration = false;
                existing.Arbiter = username;
                existing.Source = GradingSource.Teacher;
                existing.GradedAt = DateTime.Now;
            }
            else
            {
                // 分配校验：非特权教师只能判分给自己的题
                var assigned = await _gradingPolicy.AssignedQuestionNosAsync(
                    submission.PaperId, submissionId, username, isPrivileged);
                if (assigned != null && !assigned.Contains(questionNo))
                    return StatusCode(403, new { error = $"第 {questionNo} 题不在分配给你的批改范围内" });

                // 一判 / 二判：按改分人区分（同一教师重复提交覆盖自己那一判）
                if (existing.Score == null || existing.Grader == username)
                {
                    existing.Score = Math.Clamp(req.Score, 0, question.Score);
                    existing.Grader = username;
                }
                else if (existing.Score2 == null || existing.Grader2 == username)
                {
                    existing.Score2 = Math.Clamp(req.Score, 0, question.Score);
                    existing.Grader2 = username;
                }
                else
                {
                    return BadRequest(new { error = "两位教师均已提交评分；如有异议请联系仲裁教师" });
                }

                existing.Comment = req.Comment ?? existing.Comment;
                existing.Confidence = null;
                existing.Source = GradingSource.Teacher;
                existing.GradedAt = DateTime.Now;

                // 两判齐全 → 合分或仲裁
                if (existing.Score != null && existing.Score2 != null)
                {
                    var s1 = existing.Score.Value;
                    var s2 = existing.Score2.Value;
                    if (GradingMath.NeedsArbitration(s1, s2, policy.ArbitrationThreshold))
                    {
                        existing.NeedArbitration = true;   // 分差超阈值：清空得分等待仲裁
                        existing.Score = null;
                    }
                    else
                    {
                        existing.Score = GradingMath.RoundToHalfCeil((s1 + s2) / 2);
                        existing.NeedArbitration = false;
                    }
                }
            }
        }
        else
        {
            existing.Score = Math.Clamp(req.Score, 0, question.Score);
            existing.Comment = req.Comment;
            existing.Confidence = null;
            existing.Source = GradingSource.Teacher;
            existing.Grader = username;
            existing.GradedAt = DateTime.Now;
        }

        // 教师介入后进入待人工队列，仍需显式"确认"才计入成绩
        if (submission.Status != SubmissionStatus.Confirmed)
            submission.Status = SubmissionStatus.NeedsHuman;

        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    // ── 批改策略与分配（分题 / 双判 / 仲裁）──

    /// <summary>读取批改策略。</summary>
    [HttpGet("papers/{paperId}/grading-policy")]
    public async Task<IActionResult> GetGradingPolicy(string paperId)
        => Ok(await _gradingPolicy.LoadPolicyAsync(paperId));

    /// <summary>保存批改策略（开双判必须先开分题）。</summary>
    [HttpPut("papers/{paperId}/grading-policy")]
    public async Task<IActionResult> SetGradingPolicy(string paperId, [FromBody] GradingPolicy policy)
    {
        try
        {
            await _gradingPolicy.SavePolicyAsync(paperId, policy with
            {
                ArbitrationThreshold = Math.Clamp(policy.ArbitrationThreshold, 0.5, 100),
            });
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>读取批改分配。</summary>
    [HttpGet("papers/{paperId}/grading-assignments")]
    public async Task<IActionResult> GetGradingAssignments(string paperId)
        => Ok(await _gradingPolicy.LoadAssignmentsAsync(paperId));

    /// <summary>整体替换批改分配（含仲裁教师，Kind=1）。</summary>
    [HttpPut("papers/{paperId}/grading-assignments")]
    public async Task<IActionResult> SetGradingAssignments(string paperId,
        [FromBody] List<GradingAssignment> assignments)
    {
        try
        {
            if (assignments.Sum(a => a.Kind == GradingAssignment.KindGrading ? a.Percent : 0) > 100.5)
                return BadRequest(new { error = "批改分配的百分比之和不能超过 100%" });
            foreach (var a in assignments)
            {
                if (a.Percent is < 0 or > 100)
                    return BadRequest(new { error = "百分比须在 0-100 之间" });
                if (a.QuestionNos.Count == 0)
                    return BadRequest(new { error = "请为每条分配指定至少一个题号" });
            }
            await _gradingPolicy.SaveAssignmentsAsync(paperId, assignments);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
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

    /// <summary>
    /// 按标准答案比对自动判分的题型：客观题（涂卡）+ 填空题。
    /// 填空题虽由视觉模型读手写内容，但判分口径与客观题一致——对就是对、错就是错，
    /// 不走主观题那套"评分要点"流程（教师真想让 AI 按要点给分，把题型改成简答即可）。
    /// </summary>
    private static bool IsAutoScored(QuestionType type)
        => IsObjective(type) || type == QuestionType.Blank;

    /// <summary>客观题答案归一化：去空格、大写、多选题按字母排序后比较。</summary>
    /// <summary>
    /// 答案归一化：多选按字母排序（"ABD"），大小写不敏感。
    /// 判断题必须特判——卡面选项印的是 √（U+221A）/ ×（U+00D7），题库里标准答案存的是 对 / 错，
    /// 若按「只保留字母数字」处理，两者都会被清成空串而互相相等，导致所有判断题被判为答对。
    /// </summary>
    private static string NormalizeAnswer(string? answer) => AgoraIn.Core.Domain.AnswerNormalizer.Normalize(answer);

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
