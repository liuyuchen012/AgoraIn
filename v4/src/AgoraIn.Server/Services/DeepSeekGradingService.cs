using System.Net.Http.Json;
using System.Text.Json;
using AgoraIn.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Services;

/// <summary>
/// AI 题卡识别与批改服务（OpenAI 兼容 chat/completions，DeepSeek/GLM-4V/Qwen-VL 等均可）。
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

    private string ApiKey => _config["DeepSeek:ApiKey"] ?? "";
    private string BaseUrl => _config["DeepSeek:BaseUrl"] ?? "https://api.deepseek.com";

    /// <summary>识别答题卡图片：考号涂卡 + 客观题 OMR（走视觉模型）。</summary>
    public async Task<OmrResult?> RecognizeAnswerSheetAsync(byte[] imageData, CancellationToken ct = default)
    {
        var settings = await _settings.LoadAsync(ct);
        if (string.IsNullOrEmpty(ApiKey)) return null;

        // 隐私合规开关（规格 6.9）：关闭后不把学生作答图像发给第三方模型
        if (!settings.AllowImageToCloud) return null;

        var prompt = @"你是一个答题卡识别专家。请识别这张答题卡图片：
1. 识别顶部的考号涂卡区（OMR气泡），读取考号数字
2. 识别所有客观题（选择题/判断题）的涂卡答案
3. 返回 JSON 格式：
{
  ""student_ref"": ""考号"",
  ""answers"": [{""index"": 1, ""answer"": ""A"", ""confidence"": 0.95}],
  ""confidence"": 0.9
}
只返回 JSON，不要其他文字。";

        var model = string.IsNullOrEmpty(settings.VisionModel) ? settings.Model : settings.VisionModel;
        var result = await CallAsync(prompt, model, settings, imageData, "recognize", ct);
        if (result == null) return null;

        try
        {
            return JsonSerializer.Deserialize<OmrResult>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    /// <summary>AI 批改主观题（填空/简答/作文，纯文本走批改模型）。</summary>
    public async Task<AiGradingResponse?> GradeSubjectiveAsync(AiGradingRequest request, CancellationToken ct = default)
    {
        var settings = await _settings.LoadAsync(ct);
        if (string.IsNullOrEmpty(ApiKey)) return null;

        var prompt = $@"你是一个专业的试卷批改老师。请批改以下题目：

题型：{request.Type}
题目：{request.QuestionText}
学生答案：{request.StudentAnswer}
标准答案：{request.StandardAnswer ?? "无"}
评分要点：{request.Rubric ?? "无"}
满分：{request.MaxScore}分

请按评分要点给分，并给出简短评语。返回 JSON：
{{""score"": 分数, ""comment"": ""评语"", ""confidence"": 0.0-1.0}}
只返回 JSON，不要其他文字。";

        var result = await CallAsync(prompt, settings.Model, settings, null, "grade", ct);
        if (result == null) return null;

        try
        {
            return JsonSerializer.Deserialize<AiGradingResponse>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    /// <summary>调用 OpenAI 兼容 chat/completions；含重试与调用日志（Token 用量）。</summary>
    private async Task<string?> CallAsync(
        string prompt, string model, AiRuntimeSettings settings,
        byte[]? imageData, string endpoint, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(model)) model = "deepseek-chat";

        object BuildRequest() => new
        {
            model,
            messages = imageData == null
                ? new object[] { new { role = "user", content = prompt } }
                : new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{Convert.ToBase64String(imageData)}" } }
                        }
                    }
                },
            temperature = settings.Temperature,
            max_tokens = settings.MaxTokens,
        };

        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);

        var retries = Math.Clamp(settings.Retries, 0, 5);
        var started = Environment.TickCount64;
        string? error = null;

        for (var attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                var response = await _http.PostAsJsonAsync($"{BaseUrl}/v1/chat/completions", BuildRequest(), ct);
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
