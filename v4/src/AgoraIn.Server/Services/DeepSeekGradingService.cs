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
    private readonly AiQuotaService _quota;

    public DeepSeekGradingService(
        HttpClient http,
        AiSettingsService settings,
        IDbContextFactory<ServerDbContext> dbFactory,
        IConfiguration config,
        AiQuotaService quota)
    {
        _http = http;
        _settings = settings;
        _dbFactory = dbFactory;
        _config = config;
        _quota = quota;
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
2. 考号填涂区：位于卡片右上角的黑框内。框内顶部是手写考号行（可作参考），其下是 8 列方格，
   每列顶部有列序号 1-8，每列自上而下为 0-9 共 10 个方格，每列只涂黑一个数字；
   按列序 1→8 读出 8 位考号（手写与填涂不一致时以填涂为准，未涂的列留空）
3. 客观题：位于「一、客观题」黑框内，按 3 列排布，每行左端是印在卡上的题号，右端是选项方格，
   读取被涂黑的选项字母（判断题的方格是 √ / ×）
4. 每个黑框的左右两侧各有一列等距黑色小矩形，那是定位用的 OMR 定时标记，不是答案，必须忽略
5. 填空题：位于「填空题」黑框内，一题一行，行内左侧印着「第 N 题（x 分）」、右侧一条横线。
   **横线上是学生手写的内容，必须逐题识别并按题号返回**：数字/字母/汉字/带单位的算式照抄原样，
   不要补全、不要换算、不要解释、不要判对错；学生没写的行不要返回
6. 除填空题外，简答/作文等主观题的作答框内容不需要识别，也不要返回
6. 返回 JSON 格式：
{
  ""student_ref"": ""考号（8 位数字；完全没涂则为空字符串）"",
  ""answers"": [{""index"": 1, ""answer"": ""A"", ""confidence"": 0.95}],
  ""confidence"": 0.9
}
其中 index 必须与卡面印刷的题号完全一致（从 1 开始，不要重新编号、不要按顺序递增）；
多选按字母顺序连写（如 ""ABD""）；判断题返回 √ 或 ×。
只返回 JSON，不要其他文字。";

        var model = string.IsNullOrEmpty(settings.VisionModel) ? settings.Model : settings.VisionModel;
        var result = await CallAsync(prompt, model, settings, imageData == null ? null : [imageData], "recognize", ct);
        if (result == null) return null;

        var json = ExtractJsonBlock(result, expectArray: false);
        if (json == null) return null;
        try
        {
            return JsonSerializer.Deserialize<OmrResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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

        var json = ExtractJsonBlock(result, expectArray: false);
        if (json == null) return null;
        try
        {
            return JsonSerializer.Deserialize<AiGradingResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
0. **直接输出 JSON 数组，不要输出思考过程、解释或 markdown 代码块**
1. 逐题解析，不要遗漏；题目文本乱码时按上下文修复
2. type 取值：single（单选）/ multiple（多选）/ judge（判断）/ blank（填空）/ short（简答）/ essay（作文）
3. 选择题给出 options（[{{
""key"": ""A"", ""text"": ""选项内容""}}…]）；判断题 standardAnswer 用 ""对"" 或 ""错""
4. 分值 score 取卷面标注，未标注时按题型估默认（选择/判断 2 分，填空 3 分，简答 8 分，作文 40 分）
5. 主观题（short/essay）必须提炼评分要点 rubric（分步给分点，格式如 ""要点1(2分)；要点2(3分)""）
6. 若答案文件含评分细则，务必完整写入 rubric
7. 返回 JSON 数组，只返回 JSON：
[{{""index"":1,""type"":""single"",""content"":""题干"",""options"":[{{""key"":""A"",""text"":""""}}],""standardAnswer"":""A"",""score"":2,""rubric"":null,""knowledgeTags"":[""知识点""]}}]";

        var result = await CallAsync(prompt, settings.Model, settings, null, "extract", ct, 16384);
        SaveRawResponse("extract", result);
        var parsed = ParseQuestions(result);
        if (parsed == null && result != null)
            await WriteLogAsync("extract", settings.Model, default, 0, false,
                $"JSON 解析失败，返回前 200 字：{result[..Math.Min(200, result.Length)]}", ct);
        return parsed;
    }

    /// <summary>
    /// 视觉出题（多批总入口）：把试卷页面图片（前端 pdf.js 渲染）分批交给视觉模型识别。
    /// 每批 3 页独立调用——推理型模型整卷思考极易顶满 max_tokens 截断，分批保证完整出题。
    /// </summary>
    public async Task<List<ExtractedQuestion>?> ExtractQuestionsFromImagesAsync(
        IReadOnlyList<byte[]> pageImages, string? answerHint, CancellationToken ct = default)
    {
        var all = new List<ExtractedQuestion>();
        var start = 1;
        for (var offset = 0; offset < pageImages.Count; offset += 3)
        {
            ct.ThrowIfCancellationRequested();
            var batch = pageImages.Skip(offset).Take(3).ToList();
            var (items, _) = await ExtractOneBatchAsync(batch, start, ct);
            if (items != null && items.Count > 0)
            {
                all.AddRange(items);
                start += items.Count;
            }
        }
        return all.Count > 0 ? all : null;
    }

    /// <summary>
    /// 单批视觉出题：一批页面图片（≤3 页）独立调用，返回解析后的题目与本批 AI 原文。
    /// startNumber 为本批第一题的起始题号（跨批连续编号）。
    /// </summary>
    public async Task<(List<ExtractedQuestion>? Items, string? Raw)> ExtractOneBatchAsync(
        IReadOnlyList<byte[]> batchImages, int startNumber, CancellationToken ct = default)
    {
        var settings = await _settings.LoadAsync(ct);
        if (string.IsNullOrEmpty(settings.ApiKey)) return (null, null);

        var prompt = $@"你是专业的试卷结构化解析助手。下面若干张图片是同一份试卷的连续页面片段。请识别其中全部题目，题号从 {startNumber} 开始连续编号。

排版注意：
- 若图片为**横向大页（A3 两栏排版）**：页面分为左右两栏，必须先读左栏（自上而下）、再读右栏（自上而下），题目按该顺序连续编号，不得漏栏
- 图片可能包含“选择题选项仅字母无题干”的排版：客观题无需转录题干原文

要求：
1. type 取值：single（单选）/ multiple（多选）/ judge（判断）/ blank（填空）/ short（简答）/ essay（作文）
2. **客观题（single/multiple/judge/blank）**：content 仅需 ≤15 字的简要提示（可留空），重点是 options 的选项个数与 standardAnswer；不要转录题干原文
3. 选择题给出 options（[{{""key"": ""A"", ""text"": """"}}…]，key 连续）；判断题 standardAnswer 用 ""对"" 或 ""错""
4. 数学公式用 LaTeX 表示
5. 分值 score 取卷面标注；未标注按题型估默认（选择/判断 2 分，填空 3 分，简答 8 分，作文 40 分）
6. 主观题（short/essay）必须提炼评分要点 rubric（分步给分点，格式 ""要点1(2分)；要点2(3分)""，总分等于该题满分）
7. knowledgeTags 给 1-3 个知识点标签
8. **直接输出 JSON 数组，不要输出思考过程、解释或 markdown 代码块**：
[{{""index"":{startNumber},""type"":""single"",""content"":""题干提示"",""options"":[{{""key"":""A"",""text"":""""}}],""standardAnswer"":""A"",""score"":2,""rubric"":null,""knowledgeTags"":[""知识点""]}}]";

        var model = string.IsNullOrEmpty(settings.VisionModel) ? settings.Model : settings.VisionModel;
        var result = await CallAsync(prompt, model, settings, batchImages, "extract_images", ct, 16384);
        SaveRawResponse($"extract_images_n{startNumber}", result); // 排障：原始响应落盘

        var items = ParseQuestions(result);
        if (items != null)
        {
            // 按起始题号重编，保证跨批连续
            for (var i = 0; i < items.Count; i++) items[i].Index = startNumber + i;
        }
        else if (result != null)
        {
            await WriteLogAsync("extract_images", model, default, 0, false,
                $"JSON 解析失败，返回前 200 字：{result[..Math.Min(200, result.Length)]}", ct);
        }
        return (items, result);
    }

    /// <summary>答案图片回填：把答案页的答案与评分细则对应填入已解析的题目。</summary>
    public async Task<List<ExtractedQuestion>?> FillAnswersFromImagesAsync(
        List<ExtractedQuestion> questions, IReadOnlyList<byte[]> answerImages, CancellationToken ct = default)
    {
        var settings = await _settings.LoadAsync(ct);
        if (string.IsNullOrEmpty(settings.ApiKey)) return null;

        var questionSummary = JsonSerializer.Serialize(questions.Select(q => new
        {
            index = q.Index,
            content = (q.Content ?? "")[..Math.Min(60, q.Content?.Length ?? 0)],
        }), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        var prompt = $@"你是批改准备助手。下面是试卷的参考答案页图片，请把答案与评分细则按题号对应填入。

题目清单（JSON）：
{questionSummary}

要求：
1. 只输出答案与评分细则，不要重复题干
2. 答案页没有的题号不要输出
3. **客观题（选择/判断/填空）只识别答案本身**（选项字母/对错/数值），不要转录或描述原题内容
4. 若答案页为**横向大页（A3 两栏排版）**：先读左栏（自上而下）、再读右栏，按题号对应
5. 数学公式用 LaTeX 或纯文本；评分细则含分步得分时完整写入 rubric
6. **只返回 JSON 数组，不要思考过程、解释或 markdown 代码块**：
[{{""index"":1,""standardAnswer"":""…"",""rubric"":""要点1(2分);要点2(3分)""}}]";

        // 分批（每批 5 页）+ 精简输出，避免与出题相同的 max_tokens 截断问题
        const int batchSize = 5;
        var model = string.IsNullOrEmpty(settings.VisionModel) ? settings.Model : settings.VisionModel;
        for (var offset = 0; offset < answerImages.Count; offset += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = answerImages.Skip(offset).Take(batchSize).ToList();
            var result = await CallAsync(prompt, model, settings, batch, "fill_answers", ct, 16384);
            var filled = ParseQuestions(result);
            if (filled == null || filled.Count == 0) continue;
            // 按题号回填到原清单
            foreach (var f in filled)
            {
                var orig = questions.FirstOrDefault(q => q.Index == f.Index);
                if (orig == null) continue;
                if (!string.IsNullOrWhiteSpace(f.StandardAnswer)) orig.StandardAnswer = f.StandardAnswer;
                if (!string.IsNullOrWhiteSpace(f.Rubric)) orig.Rubric = f.Rubric;
            }
        }
        return questions;
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

        var json = ExtractJsonBlock(result, expectArray: false);
        if (json == null) return null;
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            var answer = doc.TryGetProperty("standardAnswer", out var a) ? a.GetString() : null;
            var rubric = doc.TryGetProperty("rubric", out var r) ? r.GetString() : null;
            return (answer, rubric);
        }
        catch { return null; }
    }

    /// <summary>
    /// 从 AI 返回文本中提取可解析的 JSON：剥除 markdown 代码围栏、截取首个 [/{ 到末个 /}]；
    /// 截断的数组做尾部修复（输出超 max_tokens 时 JSON 断尾）。
    /// </summary>
    internal static string? ExtractJsonBlock(string? content, bool expectArray)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var text = content.Trim();

        // 1) 剥 markdown 围栏（```json ... ```）
        var fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var start = text.IndexOf('\n', fence);
            var end = text.LastIndexOf("```", StringComparison.Ordinal);
            if (start >= 0 && end > start)
                text = text[(start + 1)..end].Trim();
        }

        // 2) 截取 JSON 主体（期望数组但实际是对象包装时，按实际类型截取，由调用方展开）
        var first = text.IndexOfAny(new[] { '[', '{' });
        if (first < 0) return null;
        var openActual = text[first];
        var closeActual = openActual == '[' ? ']' : '}';
        var last = text.LastIndexOf(closeActual);
        if (last <= first) return null;
        var json = text[first..(last + 1)];

        // 3) 截断修复（输出顶满 max_tokens 时 JSON 断尾）：
        //    裁到最后一个完整对象 '}' 闭合处（数组元素必须完整，不能停在字符串引号上），
        //    去掉尾部逗号后补外层闭合
        if (json[^1] != closeActual)
        {
            var cut = json.LastIndexOf('}');
            if (cut <= 0) return null;
            json = json[..(cut + 1)].TrimEnd();
            if (json.EndsWith(",")) json = json[..^1];
            if (openActual == '[') json += closeActual;
        }
        return json;
    }

    /// <summary>出题场景的原始 AI 响应落盘到 data/ai-raw/（保留最近 20 份），便于排查"0 题"类问题。</summary>
    private void SaveRawResponse(string endpoint, string? content)
    {
        if (string.IsNullOrEmpty(content)) return;
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "data", "ai-raw");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{endpoint}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.txt");
            File.WriteAllText(path, content);
            var files = Directory.GetFiles(dir, $"{endpoint}-*.txt")
                .OrderByDescending(f => f)
                .Skip(20)
                .ToList();
            foreach (var old in files)
            {
                try { File.Delete(old); } catch { }
            }
        }
        catch { }
    }

    /// <summary>解析 AI 返回的题目清单：裸数组或 {questions|data|items|result:[…]} 包装均可。</summary>
    internal static List<ExtractedQuestion>? ParseQuestions(string? content)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // 截断残片可能拼出无题干无答案的空壳项，过滤掉
        static List<ExtractedQuestion>? Clean(List<ExtractedQuestion>? list)
        {
            var filtered = list?.Where(q => !string.IsNullOrWhiteSpace(q.Content)
                                            || !string.IsNullOrWhiteSpace(q.StandardAnswer)).ToList();
            return filtered is { Count: > 0 } ? filtered : null;
        }

        var json = ExtractJsonBlock(content, expectArray: true);
        if (json != null)
        {
            if (json.StartsWith('['))
            {
                try { return Clean(JsonSerializer.Deserialize<List<ExtractedQuestion>>(json, opts)); }
                catch { }
            }
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var key in new[] { "questions", "data", "items", "result" })
                    {
                        if (doc.RootElement.TryGetProperty(key, out var arr) &&
                            arr.ValueKind == JsonValueKind.Array)
                        {
                            var list = Clean(JsonSerializer.Deserialize<List<ExtractedQuestion>>(arr.GetRawText(), opts));
                            if (list != null) return list;
                        }
                    }
                }
            }
            catch { }
        }
        return null;
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

        // 租户使用平台配置（未自配独立 AI）：校验并按实际消耗扣减额度；
        // 租户自配（区域覆盖）与主区域调用不受限
        var isTenantPlatformUsage = !AgoraIn.Server.Security.RegionContext.IsManager && !settings.HasRegionOverride;
        if (isTenantPlatformUsage)
        {
            await _quota.EnsureSufficientAsync(AgoraIn.Server.Security.RegionContext.Current, ct);
        }

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
                var message = json.GetProperty("choices")[0].GetProperty("message");
                var content = message.TryGetProperty("content", out var c) ? c.GetString() : null;
                // 推理型模型（content 为空、思考在 reasoning_content）时回落
                if (string.IsNullOrWhiteSpace(content) &&
                    message.TryGetProperty("reasoning_content", out var rc))
                {
                    content = rc.GetString();
                }

                var usage = json.TryGetProperty("usage", out var u) ? u : default;
                await WriteLogAsync(endpoint, model, usage, Environment.TickCount64 - started,
                    success: true, error: null, ct);

                // 按实际消耗扣减租户额度
                if (isTenantPlatformUsage &&
                    usage.ValueKind == JsonValueKind.Object &&
                    usage.TryGetProperty("total_tokens", out var tt) && tt.TryGetInt64(out var totalTokens))
                {
                    await _quota.DeductAsync(AgoraIn.Server.Security.RegionContext.Current, totalTokens, model, ct);
                }
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
