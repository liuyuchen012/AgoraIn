using System.Net.Http.Json;
using System.Text.Json;
using AgoraIn.Server.Models;

namespace AgoraIn.Server.Services;

/// <summary>
/// DeepSeek AI 题卡识别与批改服务。
/// 接口兼容 OpenAI 协议（DeepSeek API 兼容 OpenAI chat/completions）。
/// </summary>
public sealed class DeepSeekGradingService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public DeepSeekGradingService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    private string ApiKey => _config["DeepSeek:ApiKey"] ?? "";
    private string BaseUrl => _config["DeepSeek:BaseUrl"] ?? "https://api.deepseek.com";
    private string Model => _config["DeepSeek:Model"] ?? "deepseek-chat";

    /// <summary>识别答题卡图片：考号涂卡 + 客观题 OMR。</summary>
    public async Task<OmrResult?> RecognizeAnswerSheetAsync(byte[] imageData, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ApiKey)) return null;

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

        var result = await CallDeepSeekWithImageAsync(prompt, imageData, ct);
        if (result == null) return null;

        try
        {
            return JsonSerializer.Deserialize<OmrResult>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    /// <summary>AI 批改主观题（填空/简答/作文）。</summary>
    public async Task<AiGradingResponse?> GradeSubjectiveAsync(AiGradingRequest request, CancellationToken ct = default)
    {
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

        var result = await CallDeepSeekAsync(prompt, ct);
        if (result == null) return null;

        try
        {
            return JsonSerializer.Deserialize<AiGradingResponse>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    /// <summary>调用 DeepSeek chat/completions 接口（纯文本）。</summary>
    private async Task<string?> CallDeepSeekAsync(string prompt, CancellationToken ct)
    {
        try
        {
            var request = new
            {
                model = Model,
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.1,
                max_tokens = 1024,
            };

            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);

            var response = await _http.PostAsJsonAsync($"{BaseUrl}/v1/chat/completions", request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }
        catch { return null; }
    }

    /// <summary>调用 DeepSeek chat/completions 接口（含图片）。</summary>
    private async Task<string?> CallDeepSeekWithImageAsync(string prompt, byte[] imageData, CancellationToken ct)
    {
        try
        {
            var base64 = Convert.ToBase64String(imageData);
            var request = new
            {
                model = "deepseek-chat",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{base64}" } }
                        }
                    }
                },
                temperature = 0.1,
                max_tokens = 1024,
            };

            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);

            var response = await _http.PostAsJsonAsync($"{BaseUrl}/v1/chat/completions", request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }
        catch { return null; }
    }

    private static void WriteReport(string? path, IReadOnlyList<string> lines)
    {
        if (string.IsNullOrEmpty(path)) return;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllLines(path, lines);
    }
}
