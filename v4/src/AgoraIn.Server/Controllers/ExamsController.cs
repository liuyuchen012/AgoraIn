using System.Text;
using System.Text.Json;
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

    public ExamsController(ServerDbContext db, DeepSeekGradingService ai, AiSettingsService aiSettings,
        IServiceScopeFactory scopeFactory, ServerPaths paths)
    {
        _db = db;
        _ai = ai;
        _aiSettings = aiSettings;
        _scopeFactory = scopeFactory;
        _paths = paths;
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
            hasImage = FirstStoredImage(s) != null,
        }));
    }

    /// <summary>取某次提交的第一张扫描原图（人工复盘时对照原卷）。</summary>
    [HttpGet("submissions/{submissionId}/image")]
    public async Task<IActionResult> GetSubmissionImage(string submissionId)
    {
        var submission = await _db.AnswerSheetSubmissions.FindAsync(submissionId);
        if (submission == null) return NotFound(new { error = "提交记录不存在" });
        var stored = FirstStoredImage(submission);
        if (stored == null) return NotFound(new { error = "这条记录没有存原图（早期上传或存图失败）" });

        var full = Path.Combine(_paths.SheetDirectory, stored);
        if (!System.IO.File.Exists(full)) return NotFound(new { error = "原图文件已丢失" });

        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(full, "image/jpeg", enableRangeProcessing: true);
    }

    /// <summary>从 ImagePathsJson 取第一张「已存储」的文件名（只接受纯文件名，防目录穿越）。</summary>
    private static string? FirstStoredImage(AnswerSheetSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.ImagePathsJson)) return null;
        try
        {
            var names = System.Text.Json.JsonSerializer.Deserialize<List<string>>(submission.ImagePathsJson);
            var first = names?.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            if (first == null || first != Path.GetFileName(first)) return null;
            var ext = Path.GetExtension(first);
            if (!ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".png", StringComparison.OrdinalIgnoreCase)) return null;
            return first;
        }
        catch
        {
            return null;
        }
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
            Status = SubmissionStatus.NotGraded,
        };

        // 原图落盘：人工复盘要对着学生原卷改分，只存文件名等于没存
        var storedName = $"{submission.Id}.jpg";
        try
        {
            await System.IO.File.WriteAllBytesAsync(Path.Combine(_paths.SheetDirectory, storedName), imageData);
            submission.ImagePathsJson = System.Text.Json.JsonSerializer.Serialize(new[] { storedName });
        }
        catch
        {
            // 存图失败不阻断识别链路，只是复盘时看不到原卷
            submission.ImagePathsJson = System.Text.Json.JsonSerializer.Serialize(new[] { file.FileName });
        }

        var questions = await _db.Questions
            .Where(q => q.PaperId == paperId)
            .OrderBy(q => q.Index)
            .ToListAsync();
        var optionKeys = BuildOptionKeys(questions);

        // 先本地识别（切卡 + 客观题 + 考号，不调大模型、不外发图像）；
        // 找不到四角定位标记（没拍全/太模糊）才回退到视觉模型。
        string recognizeSource = "ai";
        var local = OmrRecognizer.Recognize(imageData, questions, optionKeys);
        OmrResult? omrResult;
        if (local != null)
        {
            recognizeSource = "local";
            omrResult = new OmrResult
            {
                StudentRef = local.StudentRef,
                OverallConfidence = local.Confidence,
                Answers = local.Answers,
            };
        }
        else
        {
            omrResult = await _ai.RecognizeAnswerSheetAsync(imageData);
        }
        var byIndex = questions.ToDictionary(q => q.Index);
        // 卡面印刷的是「题号」= Index + 1；识别结果按卡面题号回传，故按题号索引（旧代码用 Index 会整体错位一题）
        var byNo = questions.ToDictionary(q => q.Index + 1);
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
                // 兼容模型偶发返回 0 起编号：先按卡面题号取，取不到再按 0 起 Index 兜底
                if (!byNo.TryGetValue(ans.Index, out var q)
                    && !byIndex.TryGetValue(ans.Index, out q)) continue;
                lowestConfidence = Math.Min(lowestConfidence, ans.Confidence);

                if (IsObjective(q.Type) && q.AiGradingEnabled != false && !string.IsNullOrEmpty(q.StandardAnswer))
                {
                    // 客观题且教师未关闭 AI 判分：识别后与标准答案比对自动判分
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
                    // 主观题，或教师指定人工判分（AiGradingEnabled=false）的客观题：
                    // 先存识别内容，等待教师阅卷（"待人工"）
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
        else if (omrResult != null && omrResult.Answers.Count == 0)
        {
            // 一道题都没识别出来（如卡面反光/未涂卡）：不要误判为「AI 已批 0 分」
            submission.Status = SubmissionStatus.NeedsHuman;
        }

        _db.AnswerSheetSubmissions.Add(submission);
        await _db.SaveChangesAsync();

        // 识别没返回结果时，把最近的 AI 调用错误带回给 App：
        // 否则手机上只看到「没有识别结果」，分不清是没涂卡、没配密钥还是上游欠费。
        string? warning = null;
        if (omrResult == null)
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
            submissionId = submission.Id,
            status = submission.Status.ToString(),
            recognizedStudent = omrResult?.StudentRef,
            matchedStudentId = submission.StudentId,
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
                    .Where(s => s.Length > 0).ToList();
                if (keys.Count > 0) map[q.Id] = keys;
            }
            catch { /* 选项 JSON 损坏则按默认 ABCD 处理 */ }
        }
        return map;
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
