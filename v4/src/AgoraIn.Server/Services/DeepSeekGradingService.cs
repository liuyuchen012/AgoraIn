using System.Net.Http.Json;
using System.Text.Json;
using AgoraIn.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Services;

/// <summary>
/// AI 题卡识别、批改与出题服务（OpenAI 兼容 chat/completions，DeepSeek/GLM-4V/Qwen-VL 等均可）。
/// 配置优先级：数据库 AppSetting（ai.*，管理端在线可调）&gt; appsettings.json DeepSeek 节。
/// 每次调用写一条 AiCallLog（Token 消耗与失败原因可查）；答题卡图片识别受隐私开关控制。
/// </summary>
public sealed class DeepSeekGradingService
{
    private readonly HttpClient _http;
    private readonly AiSettingsService _settings;
    private readonly IDbContextFactory<ServerDbContext> _dbFactory;
    private readonly IConfiguration _config;

    public DeepSeekGradingService(
        HttpClient http,
        AiSettingsService settings,
        IDbContextFactory<ServerDbContext> dbFactory,
        IConfiguration config)
    {
        _http = http;
        _settings = settings;
        _dbFactory = dbFactory;
        _config = config;
    }

    private const string DefaultGradingTemplate = @"你是一个专业的试卷批改老师。请批改以下题目：

题型：{Type}
题目：{Question}
学生答案：{StudentAnswer}
标准答案：{StandardAnswer}
评分要点：{Rubric}
满分：{MaxScore}分

请按评分要点给分，并给出简短评语。返回 JSON：
{{""score"": 分数, ""comment"": ""评语"", ""confidence"": 0.0-1.0}}
只返回 JSON，不要其他文字。";

    /// <summary>识别答题卡图片：考号涂卡 + 客观题 OMR（走视觉模型；自动容忍旋转/倒置拍摄）。</summary>
    public async Task<OmrResult?> RecognizeAnswerSheetAsync(byte[] imageData, CancellationToken ct = default)
    {
        var settings = await _settings.LoadAsync(ct);
        if (string.IsNullOrEmpty(settings.ApiKey)) return null;

        // 隐私合规开关（规格 6.9）：关闭后不把学生作答图像发给第三方模型
        if (!settings.AllowImageToCloud) return null;

        var prompt = @"你是一个答题卡识别专家。请识别这张答题卡图片：
1. 如果图片被旋转（90°/180°/270°）或上下颠倒，请先在脑中转正后再识别，禁止因方向问题返回空结果
2. 识别顶部的考号涂卡区（OMR气泡），读取考号数字
3. 识别所有客观题（选择题/判断题）的涂卡答案
4. 返回 JSON 格式：
{
  ""student_ref"": ""考号"",
  ""answers"": [{""index"": 1, ""answer"": ""A"", ""confidence"": 0.95}],
  ""confidence"": 0.9
}
只返回 JSON，不要其他文字。";

        var model = string.IsNullOrEmpty(settings.VisionModel) ? settings.Model : settings.VisionModel;
        var result = await CallAsync(prompt, model, settings, imageData == null ? null : [imageData], "recognize", ct);
        if (result == null) return null;

        try
        {
            return JsonSerializer.Deserialize<OmrResult>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    /// <summary>AI 批改主观题（填空/简答/作文）：按可配置提示词模板 + 评分要点给分。</summary>
    public async Task<AiGradingResponse?> GradeSubjectiveAsync(AiGradingRequest request, CancellationToken ct = default)
    {
        var settings = await _settings.LoadAsync(ct);
        if (string.IsNullOrEmpty(settings.ApiKey)) return null;

        var template = string.IsNullOrWhiteSpace(settings.GradingPromptTemplate)
            ? DefaultGradingTemplate
            : settings.GradingPromptTemplate!;
        var prompt = template
            .Replace("{Type}", request.Type)
            .Replace("{Question}", request.QuestionText)
            .Replace("{StudentAnswer}", request.StudentAnswer)
            .Replace("{StandardAnswer}", request.StandardAnswer ?? "无")
            .Replace("{Rubric}", request.Rubric ?? "无")
            .Replace("{MaxScore}", request.MaxScore.ToString("0.#"));

        var result = await CallAsync(prompt, settings.Model, settings, null, "grade", ct);
        if (result == null) return null;

        try
        {
            return JsonSerializer.Deserialize<AiGradingResponse>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    /// <summary>
    /// AI 出题：从试卷文本（docx/pdf/txt 提取，题目与答案可分开两个文件）解析题目清单。
    /// 返回 JSON 数组：[{index,type,content,options,standardAnswer,score,rubric,knowledgeTags}]
    /// type ∈ single/multiple/judge/blank/short/essay。
    /// </summary>
    public async Task<List<ExtractedQuestion>?> ExtractQuestionsAsync(string questionText, string? answerText, CancellationToken ct = default)
    {
        var settings = await _settings.LoadAsync(ct);
        if (string.IsNullOrEmpty(settings.ApiKey)) return null;

        var answerSection = string.IsNullOrWhiteSpace(answerText)
            ? "未提供答案文件：请根据题目内容自行推断标准答案（客观题给选项字母，主观题给要点式参考答案）。"
            : $@"答案文件内容如下（可能含答案与评分细则/评分标准）：
---
{answerText}
---";

        var prompt = $@"你是专业的试卷结构化解析助手。下面是一份试卷的文本内容，请把它解析为结构化题目清单。

{answerSection}

试卷文本：
---
{questionText}
---

要求：
1. 逐题解析，不要遗漏；题目文本乱码时按上下文修复
2. type 取值：single（单选）/ multiple（多选）/ judge（判断）/ blank（填空）/ short（简答）/ essay（作文）
3. 选择题给出 options（[{{
""key"": ""A"", ""text"": ""选项内容""}}…]）；判断题 standardAnswer 用 ""对"" 或 ""错""
4. 分值 score 取卷面标注，未标注时按题型估默认（选择/判断 2 分，填空 3 分，简答 8 分，作文 40 分）
5. 主观题（short/essay）必须提炼评分要点 rubric（分步给分点，格式如 ""要点1(2分)；要点2(3分)""）
6. 若答案文件含评分细则，务必完整写入 rubric
7. 返回 JSON 数组，只返回 JSON：
[{{""index"":1,""type"":""single"",""content"":""题干"",""options"":[{{""key"":""A"",""text"":""""}}],""standardAnswer"":""A"",""score"":2,""rubric"":null,""knowledgeTags"":[""知识点""]}}]";

        var result = await CallAsync(prompt, settings.Model, settings, null, "extract", ct, 8192);
        if (result == null) return null;

        try
        {
            return JsonSerializer.Deserialize<List<ExtractedQuestion>>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    /// <summary>
    /// 视觉出题：把试卷页面图片（前端 pdf.js 渲染）直接交给视觉模型识别全部题目。
    /// 数学公式在 PDF 文字层是乱码，渲染成图片后视觉模型读取的是完整版面。
    /// </summary>
    public async Task<List<ExtractedQuestion>?> ExtractQuestionsFromImagesAsync(
        IReadOnlyList<byte[]> pageImages, string? answerHint, CancellationToken ct = default)
    {
        var settings = await _settings.LoadAsync(ct);
        if (string.IsNullOrEmpty(settings.ApiKey)) return null;

        var answerSection = string.IsNullOrWhiteSpace(answerHint)
            ? "未提供答案信息：客观题请依据题干自行推断最合理的选项，主观题给出要点式参考答案。"
            : answerHint;

        var prompt = $@"你是专业的试卷结构化解析助手。下面若干张图片是同一份试卷的连续页面（按顺序），请逐页识别并解析出全部题目。

{answerSection}

要求：
1. 逐题解析，跨页的题目合并为完整题干，不要遗漏、不要重复
2. type 取值：single（单选）/ multiple（多选）/ judge（判断）/ blank（填空）/ short（简答）/ essay（作文）
3. 选择题给出 options（[{{""key"": ""A"", ""text"": ""选项内容""}}…]）；判断题 standardAnswer 用 ""对"" 或 ""错""
4. 数学公式用 LaTeX 表示（如 y=x^{{-1}} 写作 ""y=x^-1"" 或 LaTeX）
5. 分值 score 取卷面标注；未标注按题型估默认（选择/判断 2 分，填空 3 分，简答 8 分，作文 40 分）
6. 主观题（short/essay）必须提炼评分要点 rubric（分步给分点，格式 ""要点1(2分)；要点2(3分)""，总分等于该题满分）
7. knowledgeTags 给 1-3 个知识点标签
8. 只返回 JSON 数组：
[{{""index"":1,""type"":""single"",""content"":""题干"",""options"":[{{""key"":""A"",""text"":""""}}],""standardAnswer"":""A"",""score"":2,""rubric"":null,""knowledgeTags"":[""知识点""]}}]";

        var model = string.IsNullOrEmpty(settings.VisionModel) ? settings.Model : settings.VisionModel;
        var result = await CallAsync(prompt, model, settings, pageImages, "extract_images", ct, 8192);
        if (result == null) return null;

        try
        {
            return JsonSerializer.Deserialize<List<ExtractedQuestion>>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    /// <summary>答案图片回填：把答案页的答案与评分细则对应填入已解析的题目。</summary>
    public async Task<List<ExtractedQuestion>?> FillAnswersFromImagesAsync(
        List<ExtractedQuestion> questions, IReadOnlyList<byte[]> answerImages, CancellationToken ct = default)
    {
        var settings = await _settings.LoadAsync(ct);
        if (string.IsNullOrEmpty(settings.ApiKey)) return null;

        var questionSummary = JsonSerializer.Serialize(questions.Select(q => new
        {
            index = q.Index, type = q.Type, content = (q.Content ?? "")[..Math.Min(80, q.Content?.Length ?? 0)],
            q.StandardAnswer, q.Rubric,
        }), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        var prompt = $@"你是批改准备助手。第一组图片是一份试卷的**参考答案页**，请把答案页中的逐题答案与评分细则，
对应填入下面的题目清单（按题号对应；答案页含评分细则/分步得分时完整写入 rubric）。

题目清单（JSON）：
{questionSummary}

要求：
1. 只补全/修正 standardAnswer 与 rubric 字段，content/options/score 等保持原样
2. 答案页没有的题目保持原值
3. 数学公式用 LaTeX 或纯文本
4. 只返回完整的题目清单 JSON 数组（字段与输入一致）";

        var model = string.IsNullOrEmpty(settings.VisionModel) ? settings.Model : settings.VisionModel;
        var result = await CallAsync(prompt, model, settings, answerImages, "fill_answers", ct, 8192);
        if (result == null) return null;

        try
        {
            var filled = JsonSerializer.Deserialize<List<ExtractedQuestion>>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (filled == null || filled.Count == 0) return null;
            // 按题号回填到原清单，保留原 content/options
            foreach (var f in filled)
            {
                var orig = questions.FirstOrDefault(q => q.Index == f.Index);
                if (orig == null) continue;
                if (!string.IsNullOrWhiteSpace(f.StandardAnswer)) orig.StandardAnswer = f.StandardAnswer;
                if (!string.IsNullOrWhiteSpace(f.Rubric)) orig.Rubric = f.Rubric;
            }
            return questions;
        }
        catch { return null; }
    }

    /// <summary>AI 生成标准答案与评分要点（用于缺答案/缺 rubric 的题目）。</summary>
    public async Task<(string? StandardAnswer, string? Rubric)?> GenerateAnswerAsync(
        string type, string? content, double maxScore, string? existingRubric, CancellationToken ct = default)
    {
        var settings = await _settings.LoadAsync(ct);
        if (string.IsNullOrEmpty(settings.ApiKey)) return null;

        var prompt = $@"你是命题专家。请为下面这道题生成标准答案与评分要点。

题型：{type}
题目：{content}
满分：{maxScore} 分
已有评分要点（可完善）：{existingRubric ?? "无"}

要求：
1. 标准答案：客观题给选项字母/对错；填空给精确答案；简答/作文给要点式参考答案
2. 评分要点：分步给分点，格式 ""要点1(2分)；要点2(3分)""，总分等于满分
3. 返回 JSON，只返回 JSON：
{{""standardAnswer"": ""…"", ""rubric"": ""…""}}";

        var result = await CallAsync(prompt, settings.Model, settings, null, "generate_answer", ct);
        if (result == null) return null;

        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(result);
            var answer = doc.TryGetProperty("standardAnswer", out var a) ? a.GetString() : null;
            var rubric = doc.TryGetProperty("rubric", out var r) ? r.GetString() : null;
            return (answer, rubric);
        }
        catch { return null; }
    }

    /// <summary>调用 OpenAI 兼容 chat/completions；支持多图输入与 max_tokens 覆盖；含重试与调用日志。</summary>
    private async Task<string?> CallAsync(
        string prompt, string model, AiRuntimeSettings settings,
        IReadOnlyList<byte[]>? images, string endpoint, CancellationToken ct, int? maxTokensOverride = null)
    {
        if (string.IsNullOrEmpty(model)) model = "deepseek-chat";
        if (string.IsNullOrEmpty(settings.ApiKey)) return null;

        object BuildRequest()
        {
            object content = images is { Count: > 0 }
                ? Enumerable.Range(0, images.Count + 1)
                    .Select(i => i == 0
                        ? (object)new { type = "text", text = prompt }
                        : new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{Convert.ToBase64String(images[i - 1])}" } })
                    .ToArray()
                : prompt;
            return new
            {
                model,
                messages = new object[] { new { role = "user", content } },
                temperature = settings.Temperature,
                max_tokens = maxTokensOverride ?? settings.MaxTokens,
            };
        }

        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);

        // API 地址智能拼接：地址已带版本段（/v1、/v3、/v4…）时直接接 /chat/completions
        //（兼容智谱 open.bigmodel.cn/api/paas/v4、通义 …/compatible-mode/v1），否则补 /v1（DeepSeek 官方风格）
        var baseTrim = (settings.BaseUrl ?? "https://api.deepseek.com").Trim().TrimEnd('/');
        var chatUrl = System.Text.RegularExpressions.Regex.IsMatch(baseTrim, @"/v[0-9]+$")
            ? $"{baseTrim}/chat/completions"
            : $"{baseTrim}/v1/chat/completions";

        var retries = Math.Clamp(settings.Retries, 0, 5);
        var started = Environment.TickCount64;
        string? error = null;

        for (var attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                var response = await _http.PostAsJsonAsync(chatUrl, BuildRequest(), ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                var content = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

                var usage = json.TryGetProperty("usage", out var u) ? u : default;
                await WriteLogAsync(endpoint, model, usage, Environment.TickCount64 - started,
                    success: true, error: null, ct);
                return content;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && attempt < retries)
            {
                error = ex.Message;
                await Task.Delay(500 * (attempt + 1), ct);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                break;
            }
        }

        await WriteLogAsync(endpoint, model, default, Environment.TickCount64 - started, success: false, error, ct);
        return null;
    }

    private async Task WriteLogAsync(string endpoint, string model, JsonElement usage, long durationMs, bool success, string? error, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.AiCallLogs.Add(new AiCallLogEntity
            {
                Endpoint = endpoint,
                Model = model,
                PromptTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pi) ? pi : null,
                CompletionTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("completion_tokens", out var cp) && cp.TryGetInt32(out var ci) ? ci : null,
                TotalTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("total_tokens", out var tp) && tp.TryGetInt32(out var ti) ? ti : null,
                DurationMs = (int)Math.Clamp(durationMs, 0, int.MaxValue),
                Success = success,
                Error = error,
            });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}

/// <summary>AI 出题解析结果（一份试卷 → 题目清单）。</summary>
public sealed class ExtractedQuestion
{
    public int Index { get; set; }
    public string Type { get; set; } = "single";
    public string? Content { get; set; }
    public List<ExtractedOption>? Options { get; set; }
    public string? StandardAnswer { get; set; }
    public double Score { get; set; } = 2;
    public string? Rubric { get; set; }
    public List<string>? KnowledgeTags { get; set; }
}

public sealed class ExtractedOption
{
    public string Key { get; set; } = "";
    public string? Text { get; set; }
}
