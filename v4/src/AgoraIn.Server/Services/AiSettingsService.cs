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
    /// 是否显式关闭模型的"思考模式"（DeepSeek 系默认 true）。
    /// 官方文档：请求体带 <c>thinking: {{"type":"disabled"}}</c> 即关闭；不传则默认开启且 effort=high，
    /// 会把输出预算全烧在推理上——现场的"图片导题 JSON 解析失败"就是这么来的：
    /// 23715 字全是思考，被 max_tokens 截断，一个 JSON 字符都没吐。
    /// 其他厂商若不认这个字段，调用层会收到 400 后自动去掉它重试。
    /// </summary>
    public bool DisableThinking { get; set; } = true;

    /// <summary>
    /// 主观题批改提示词模板（占位符：{Type} 题型 / {Question} 题干 / {StudentAnswer} 学生答案 /
    /// {StandardAnswer} 标准答案 / {Rubric} 评分要点 / {MaxScore} 满分；空 = 使用内置默认模板）。
    /// </summary>
    public string? GradingPromptTemplate { get; set; }

    /// <summary>配置作用域：global（平台默认）/ region（区域独立配置）。读取接口填充。</summary>
    public string? Scope { get; set; }

    /// <summary>当前区域是否存在独立覆盖（仅区域请求填充）。</summary>
    public bool HasRegionOverride { get; set; }
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

        var regionId = Security.RegionContext.Current;
        var isRegion = regionId != Security.RegionContext.ManagerRegion;
        var regionPrefix = KeyPrefix + regionId + ".";

        var rows = await _db.AppSettings
            .Where(x => x.Key.StartsWith(KeyPrefix))
            .ToListAsync(ct);
        // 全局键 = ai. 后恰一段（ai.model）；区域键 = ai.{regionId}.xxx（区域代号无点，不会混淆）
        var globalEntries = rows.Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.Key, @"^ai\.[^.]+$"));
        var regionEntries = rows.Where(x => x.Key.StartsWith(regionPrefix));

        void Apply(IEnumerable<KeyValuePair<string, string>> entries, int skip)
        {
            foreach (var e in entries)
            {
                var key = e.Key;
                var value = e.Value;
                var field = key[skip..];
                switch (field)
                {
                    case "apiKey": if (value.Length > 0) s.ApiKey = value; break;
                    case "baseUrl": if (value.Length > 0) s.BaseUrl = value; break;
                    case "model": if (value.Length > 0) s.Model = value; break;
                    case "visionModel": s.VisionModel = value; break;
                    case "temperature": if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var tv)) s.Temperature = tv; break;
                    case "maxTokens": if (int.TryParse(value, out var mv)) s.MaxTokens = mv; break;
                    case "allowImageToCloud": s.AllowImageToCloud = value != "false"; break;
                    case "humanReviewThreshold": if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var hv)) s.HumanReviewThreshold = hv; break;
                    case "retries": if (int.TryParse(value, out var rv)) s.Retries = rv; break;
                    case "disableThinking": s.DisableThinking = value != "false"; break;
                    case "gradingPromptTemplate": s.GradingPromptTemplate = value; break;
                }
            }
        }

        // 第一层：全局覆盖（主区域维护的平台默认）
        Apply(globalEntries.Select(e => new KeyValuePair<string, string>(e.Key, e.Value)), KeyPrefix.Length);

        // 第二层：区域覆盖（租户自配，优先级最高）
        if (isRegion)
        {
            Apply(regionEntries.Select(e => new KeyValuePair<string, string>(e.Key, e.Value)), regionPrefix.Length);
            s.HasRegionOverride = regionEntries.Any();
        }
        s.Scope = isRegion ? "region" : "global";
        return s;
    }

    /// <summary>
    /// 保存设置。apiKey 语义：null = 不变；"" = 清除（回落上级配置）；非空 = 更新。
    /// regionScope 为 null 时写入全局覆盖（主区域维护平台默认）；
    /// 为区域代号时写入该区域独立覆盖（ai.{regionId}.*，优先于全局）。
    /// </summary>
    public async Task SaveAsync(AiRuntimeSettings s, string? regionScope = null, CancellationToken ct = default)
    {
        var prefix = string.IsNullOrEmpty(regionScope) || regionScope == Security.RegionContext.ManagerRegion
            ? KeyPrefix
            : KeyPrefix + regionScope + ".";

        var updates = new Dictionary<string, string?>
        {
            [prefix + "baseUrl"] = string.IsNullOrWhiteSpace(s.BaseUrl) ? null : s.BaseUrl.Trim().TrimEnd('/'),
            [prefix + "model"] = s.Model,
            [prefix + "visionModel"] = s.VisionModel,
            [prefix + "temperature"] = s.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [prefix + "maxTokens"] = s.MaxTokens.ToString(),
            [prefix + "allowImageToCloud"] = s.AllowImageToCloud ? "true" : "false",
            [prefix + "humanReviewThreshold"] = s.HumanReviewThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [prefix + "retries"] = s.Retries.ToString(),
            [prefix + "disableThinking"] = s.DisableThinking ? "true" : "false",
            [prefix + "gradingPromptTemplate"] = string.IsNullOrWhiteSpace(s.GradingPromptTemplate) ? null : s.GradingPromptTemplate,
        };
        if (s.ApiKey != null)
        {
            updates[prefix + "apiKey"] = s.ApiKey.Length == 0 ? null : s.ApiKey;
        }

        var keys = updates.Keys.ToList();
        var existing = await _db.AppSettings
            .Where(x => keys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, ct);
        foreach (var (key, value) in updates)
        {
            if (value == null)
            {
                // null = 清除该覆盖项（回落上级默认值）
                if (existing.TryGetValue(key, out var row0)) _db.AppSettings.Remove(row0);
                continue;
            }
            if (existing.TryGetValue(key, out var row)) row.Value = value;
            else _db.AppSettings.Add(new Core.Entities.AppSetting { Key = key, Value = value });
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>清除指定区域的独立覆盖（恢复使用平台默认配置）。</summary>
    public async Task ResetRegionAsync(string regionId, CancellationToken ct = default)
    {
        var prefix = KeyPrefix + regionId + ".";
        var rows = await _db.AppSettings
            .Where(x => x.Key.StartsWith(prefix))
            .ToListAsync(ct);
        _db.AppSettings.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
    }

    private static double ParseDouble(string? raw, double fallback)
        => double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
}

/// <summary>AI 调用结果（含 Token 用量，供日志记录）。</summary>
public sealed record AiCallOutcome(bool Success, string? Error, int? PromptTokens, int? CompletionTokens, int? TotalTokens, int DurationMs);
