using System.Text.Json;
using AgoraIn.Core.Security;
using AgoraIn.Server.Models;
using AgoraIn.Server.Security;
using AgoraIn.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>AI 批改设置 API。前缀 /api/v4/settings/ai（在线调整模型/温度/隐私开关，Token 日志可查）。</summary>
[ApiController]
[Route("api/v4/settings/ai")]
[Authorize]
[RequirePermission(Permissions.AiSettings)]
public class AiSettingsController : ControllerBase
{
    private readonly ServerDbContext _db;
    private readonly AiSettingsService _settings;

    public AiSettingsController(ServerDbContext db, AiSettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    /// <summary>读取 AI 批改设置（API 密钥脱敏返回）。租户读到的是本区域生效配置（含作用域标注）。</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var s = await _settings.LoadAsync(ct);
        return Ok(new
        {
            s.BaseUrl, s.Model, s.VisionModel, s.Temperature, s.MaxTokens,
            s.AllowImageToCloud, s.HumanReviewThreshold, s.Retries,
            s.GradingPromptTemplate,
            hasApiKey = !string.IsNullOrEmpty(s.ApiKey),
            apiKeyMasked = MaskKey(s.ApiKey),
            s.Scope,
            s.HasRegionOverride,
        });
    }

    /// <summary>
    /// 保存 AI 批改设置。apiKey 语义：null/缺省 = 不变；空串 = 清除（回落 appsettings）；非空 = 更新。
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Save([FromBody] JsonElement body, CancellationToken ct)
    {
        var current = await _settings.LoadAsync(ct);

        var s = new AiRuntimeSettings
        {
            ApiKey = current.ApiKey,
            BaseUrl = body.TryGetProperty("baseUrl", out var bu) ? bu.GetString() ?? current.BaseUrl : current.BaseUrl,
            Model = body.TryGetProperty("model", out var m) ? m.GetString() ?? current.Model : current.Model,
            VisionModel = body.TryGetProperty("visionModel", out var vm) ? vm.GetString() ?? "" : current.VisionModel,
            Temperature = body.TryGetProperty("temperature", out var t) && t.TryGetDouble(out var tv) ? tv : current.Temperature,
            MaxTokens = body.TryGetProperty("maxTokens", out var mt) && mt.TryGetInt32(out var mv) ? mv : current.MaxTokens,
            AllowImageToCloud = !body.TryGetProperty("allowImageToCloud", out var ai) || ai.ValueKind != JsonValueKind.False,
            HumanReviewThreshold = body.TryGetProperty("humanReviewThreshold", out var ht) && ht.TryGetDouble(out var hv) ? hv : current.HumanReviewThreshold,
            // 思考模式开关：UI 没传就沿用当前值（默认关思考——DeepSeek 系默认开启会把输出预算全烧在推理上）
            DisableThinking = body.TryGetProperty("disableThinking", out var dt) && dt.ValueKind == JsonValueKind.False
                ? false : current.DisableThinking,
            Retries = body.TryGetProperty("retries", out var r) && r.TryGetInt32(out var rv) ? rv : current.Retries,
            GradingPromptTemplate = body.TryGetProperty("gradingPromptTemplate", out var gpt)
                ? (gpt.ValueKind == JsonValueKind.String ? gpt.GetString() : null)
                : current.GradingPromptTemplate,
        };

        // API 密钥：显式传 apiKey 字段时才变更（null=不变，""=清除）
        if (body.TryGetProperty("apiKey", out var ak))
        {
            s.ApiKey = ak.ValueKind == JsonValueKind.String ? ak.GetString() ?? "" : "";
        }

        if (s.Temperature is < 0 or > 2) return BadRequest(new { error = "温度须在 0-2 之间" });
        if (s.MaxTokens is < 128 or > 32768) return BadRequest(new { error = "maxTokens 须在 128-32768 之间" });
        if (s.HumanReviewThreshold is < 0 or > 1) return BadRequest(new { error = "低置信度阈值须在 0-1 之间" });

        // 作用域：主区域保存平台默认；区域保存本区域独立覆盖（优先于平台默认）
        var isRegion = !AgoraIn.Server.Security.RegionContext.IsManager;
        var regionScope = isRegion ? AgoraIn.Server.Security.RegionContext.Current : null;

        if (isRegion && body.TryGetProperty("resetRegion", out var rr) && rr.ValueKind == JsonValueKind.True)
        {
            await _settings.ResetRegionAsync(AgoraIn.Server.Security.RegionContext.Current, ct);
        }
        else
        {
            await _settings.SaveAsync(s, regionScope, ct);
        }

        var saved = await _settings.LoadAsync(ct);
        return Ok(new
        {
            saved.BaseUrl, saved.Model, saved.VisionModel, saved.Temperature, saved.MaxTokens,
            saved.AllowImageToCloud, saved.HumanReviewThreshold, saved.Retries,
            saved.GradingPromptTemplate,
            hasApiKey = !string.IsNullOrEmpty(saved.ApiKey),
            apiKeyMasked = MaskKey(saved.ApiKey),
            saved.Scope,
            saved.HasRegionOverride,
        });
    }

    private static string MaskKey(string key) => key.Length <= 8
        ? new string('•', key.Length)
        : $"{key[..4]}{new string('•', Math.Max(4, key.Length - 8))}{key[^4..]}";

    /// <summary>AI 调用日志（Token 消耗与失败原因，最近优先）。</summary>
    [HttpGet("logs")]
    public async Task<IActionResult> Logs([FromQuery] int take = 100, CancellationToken ct = default)
    {
        var logs = await _db.AiCallLogs
            .OrderByDescending(l => l.Id)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);
        var totalTokens = await _db.AiCallLogs.SumAsync(l => l.TotalTokens ?? 0, ct);
        return Ok(new { totalTokens, logs });
    }
}

/// <summary>
/// 同步 API。前缀 /api/v4/sync
/// 本地优先架构的变更集拉取端点：桌面端以上次水位（时间戳）增量拉取服务端数据；
/// 推送侧复用既有幂等端点（POST /checkin/records、/classhours/records、/points/records 均按 SlotKey/SourceId 去重）。
/// </summary>
[ApiController]
[Route("api/v4/sync")]
[Authorize]
[RequirePermission(Permissions.CheckInView)]
public class SyncController : ControllerBase
{
    private readonly ServerDbContext _db;
    public SyncController(ServerDbContext db) => _db = db;

    /// <summary>
    /// 变更集拉取。since 为上次同步的服务器时间戳（ ISO 8601；空 = 全量首同步）。
    /// 返回 serverTime 作为下次水位；记录类按时间过滤，班级/学生/任务为小表全量快照。
    /// </summary>
    [HttpGet("pull")]
    public async Task<IActionResult> Pull([FromQuery] DateTime? since, [FromQuery] int take = 1000, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 100, 5000);
        var now = DateTime.Now;

        var checkInQ = _db.CheckInRecords.AsQueryable();
        var classHourQ = _db.ClassHourRecords.AsQueryable();
        var pointQ = _db.PointRecords.AsQueryable();
        if (since != null)
        {
            checkInQ = checkInQ.Where(r => r.CheckedAt > since);
            classHourQ = classHourQ.Where(r => r.CreatedAt > since);
            pointQ = pointQ.Where(r => r.CreatedAt > since);
        }

        var classes = await _db.Classes.ToListAsync(ct);
        var students = await _db.Students.ToListAsync(ct);
        var tasks = await _db.CheckInTasks.ToListAsync(ct);
        var accounts = await _db.ClassHourAccounts.ToListAsync(ct);

        return Ok(new
        {
            serverTime = now,
            classes,
            students,
            checkInTasks = tasks,
            classHourAccounts = accounts,
            checkInRecords = await checkInQ.OrderBy(r => r.CheckedAt).Take(take).ToListAsync(ct),
            classHourRecords = await classHourQ.OrderBy(r => r.CreatedAt).Take(take).ToListAsync(ct),
            pointRecords = await pointQ.OrderBy(r => r.CreatedAt).Take(take).ToListAsync(ct),
        });
    }

    /// <summary>
    /// 冲突裁定推送（可变实体）。冲突策略：服务器时间戳裁定——
    /// 客户端 UpdatedAt 较新才应用，否则拒绝并返回服务端当前状态（conflicts），由教师端最终确认。
    /// 追加型记录（打卡/课时/积分流水）不走此端点，直接用各资源幂等 POST。
    /// </summary>
    [HttpPost("push")]
    public async Task<IActionResult> Push([FromBody] SyncPushRequest req, CancellationToken ct)
    {
        var applied = 0;
        var conflicts = new List<object>();

        foreach (var account in req.ClassHourAccounts)
        {
            var server = await _db.ClassHourAccounts.FirstOrDefaultAsync(a => a.StudentId == account.StudentId, ct);
            if (server == null)
            {
                _db.ClassHourAccounts.Add(account);
                applied++;
                continue;
            }

            // 服务器时间戳裁定：客户端更旧 → 拒绝并回报服务端状态
            if (server.UpdatedAt > account.UpdatedAt)
            {
                conflicts.Add(new
                {
                    entity = "classHourAccount",
                    studentId = server.StudentId,
                    client = new { account.TotalHours, account.UsedHours, account.UpdatedAt },
                    server = new { server.TotalHours, server.UsedHours, server.UpdatedAt },
                });
                continue;
            }

            server.TotalHours = account.TotalHours;
            server.UsedHours = account.UsedHours;
            server.Remark = account.Remark;
            server.UpdatedAt = DateTime.Now;
            applied++;
        }

        foreach (var student in req.Students)
        {
            var server = await _db.Students.FindAsync([student.Id], ct);
            if (server == null)
            {
                _db.Students.Add(student);
                applied++;
                continue;
            }

            server.Name = student.Name;
            server.StudentNo = student.StudentNo;
            server.Gender = student.Gender;
            server.Status = student.Status;
            server.Remark = student.Remark;
            applied++;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { applied, conflictCount = conflicts.Count, conflicts });
    }
}

public record SyncPushRequest(
    List<AgoraIn.Core.Entities.ClassHourAccount>? ClassHourAccounts,
    List<AgoraIn.Core.Entities.Student>? Students);
