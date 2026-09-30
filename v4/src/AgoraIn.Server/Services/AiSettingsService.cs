using System.Text.Json;
using AgoraIn.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Services;

/// <summary>AI 批改运行时配置（appsettings 兜底 + 数据库 AppSetting 覆盖，管理端可在线调整）。</summary>
public sealed class AiRuntimeSettings
{
    /// <summary>API 密钥（数据库覆盖 appsettings 的 DeepSeek:ApiKey；管理端在线可改，读取时脱敏）。</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>API 地址（OpenAI 兼容 BaseUrl；数据库覆盖 appsettings 的 DeepSeek:BaseUrl）。</summary>
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    /// <summary>文本批改模型（OpenAI 兼容）。</summary>
    public string Model { get; set; } = "";

    /// <summary>视觉识别模型（答题卡图片识别用；为空时回落到 <see cref="Model"/>）。</summary>
    public string VisionModel { get; set; } = "";

    /// <summary>采样温度（批改一致性要求低温，默认 0.1）。</summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>单次调用 max_tokens。</summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>
    /// 是否允许把学生作答图像发送给第三方大模型（隐私开关，默认允许；
    /// 关闭后答题卡识别不可用，主观题只支持手判）。
    /// </summary>
    public bool AllowImageToCloud { get; set; } = true;

    /// <summary>低置信度阈值（低于该值的状态机推进到"待人工"）。</summary>
    public double HumanReviewThreshold { get; set; } = 0.6;

    /// <summary>失败重试次数。</summary>
    public int Retries { get; set; } = 2;

    /// <summary>
    /// 主观题批改提示词模板（占位符：{Type} 题型 / {Question} 题干 / {StudentAnswer} 学生答案 /
    /// {StandardAnswer} 标准答案 / {Rubric} 评分要点 / {MaxScore} 满分；空 = 使用内置默认模板）。
    /// </summary>
    public string? GradingPromptTemplate { get; set; }
}

/// <summary>
/// AI 设置的存取：数据库 AppSetting 表（键前缀 ai.）为主，appsettings.json 的 DeepSeek 节为默认值。
/// 作用域为 scoped（控制器内直接注入），DeepSeekGradingService 调用时按参数传入。
/// </summary>
public sealed class AiSettingsService
{
    public const string KeyPrefix = "ai.";

    private readonly ServerDbContext _db;
    private readonly IConfiguration _config;

    public AiSettingsService(ServerDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<AiRuntimeSettings> LoadAsync(CancellationToken ct = default)
    {
        var s = new AiRuntimeSettings
        {
            ApiKey = _config["DeepSeek:ApiKey"] ?? "",
            BaseUrl = _config["DeepSeek:BaseUrl"] ?? "https://api.deepseek.com",
            Model = _config["DeepSeek:Model"] ?? "deepseek-chat",
            VisionModel = _config["DeepSeek:VisionModel"] ?? "",
            Temperature = ParseDouble(_config["DeepSeek:Temperature"], 0.1),
            MaxTokens = (int)ParseDouble(_config["DeepSeek:MaxTokens"], 1024),
        };

        var rows = await _db.AppSettings
            .Where(x => x.Key.StartsWith(KeyPrefix))
            .ToListAsync(ct);
        var map = rows.ToDictionary(x => x.Key, x => x.Value);

        if (map.TryGetValue(KeyPrefix + "apiKey", out var k) && k.Length > 0) s.ApiKey = k;
        if (map.TryGetValue(KeyPrefix + "baseUrl", out var bu) && bu.Length > 0) s.BaseUrl = bu;
        if (map.TryGetValue(KeyPrefix + "model", out var m) && m.Length > 0) s.Model = m;
        if (map.TryGetValue(KeyPrefix + "visionModel", out var vm)) s.VisionModel = vm;
        if (map.TryGetValue(KeyPrefix + "temperature", out var t) && double.TryParse(t, out var tv)) s.Temperature = tv;
        if (map.TryGetValue(KeyPrefix + "maxTokens", out var mt) && int.TryParse(mt, out var mv)) s.MaxTokens = mv;
        if (map.TryGetValue(KeyPrefix + "allowImageToCloud", out var ai)) s.AllowImageToCloud = ai != "false";
        if (map.TryGetValue(KeyPrefix + "humanReviewThreshold", out var ht) && double.TryParse(ht, out var hv)) s.HumanReviewThreshold = hv;
        if (map.TryGetValue(KeyPrefix + "retries", out var r) && int.TryParse(r, out var rv)) s.Retries = rv;
        if (map.TryGetValue(KeyPrefix + "gradingPromptTemplate", out var gpt)) s.GradingPromptTemplate = gpt;
        return s;
    }

    /// <summary>
    /// 保存设置。apiKey 语义：null = 不变；"" = 清除（回落 appsettings）；非空 = 更新。
    /// </summary>
    public async Task SaveAsync(AiRuntimeSettings s, CancellationToken ct = default)
    {
        var updates = new Dictionary<string, string?>
        {
            [KeyPrefix + "baseUrl"] = string.IsNullOrWhiteSpace(s.BaseUrl) ? null : s.BaseUrl.Trim().TrimEnd('/'),
            [KeyPrefix + "model"] = s.Model,
            [KeyPrefix + "visionModel"] = s.VisionModel,
            [KeyPrefix + "temperature"] = s.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [KeyPrefix + "maxTokens"] = s.MaxTokens.ToString(),
            [KeyPrefix + "allowImageToCloud"] = s.AllowImageToCloud ? "true" : "false",
            [KeyPrefix + "humanReviewThreshold"] = s.HumanReviewThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [KeyPrefix + "retries"] = s.Retries.ToString(),
            [KeyPrefix + "gradingPromptTemplate"] = string.IsNullOrWhiteSpace(s.GradingPromptTemplate) ? null : s.GradingPromptTemplate,
        };
        if (s.ApiKey != null)
        {
            updates[KeyPrefix + "apiKey"] = s.ApiKey.Length == 0 ? null : s.ApiKey;
        }

        var keys = updates.Keys.ToList();
        var existing = await _db.AppSettings
            .Where(x => keys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, ct);
        foreach (var (key, value) in updates)
        {
            if (value == null)
            {
                // null = 清除该覆盖项（回落 appsettings 默认值）
                if (existing.TryGetValue(key, out var row0)) _db.AppSettings.Remove(row0);
                continue;
            }
            if (existing.TryGetValue(key, out var row)) row.Value = value;
            else _db.AppSettings.Add(new Core.Entities.AppSetting { Key = key, Value = value });
        }
        await _db.SaveChangesAsync(ct);
    }

    private static double ParseDouble(string? raw, double fallback)
        => double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
}

/// <summary>AI 调用结果（含 Token 用量，供日志记录）。</summary>
public sealed record AiCallOutcome(bool Success, string? Error, int? PromptTokens, int? CompletionTokens, int? TotalTokens, int DurationMs);
