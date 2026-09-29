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
[RequirePermission(Permissions.SystemSettings)]
public class AiSettingsController : ControllerBase
{
    private readonly ServerDbContext _db;
    private readonly AiSettingsService _settings;

    public AiSettingsController(ServerDbContext db, AiSettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    /// <summary>读取 AI 批改设置。</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await _settings.LoadAsync(ct));

    /// <summary>保存 AI 批改设置（数据库覆盖 appsettings 默认值）。</summary>
    [HttpPut]
    public async Task<IActionResult> Save([FromBody] AiRuntimeSettings req, CancellationToken ct)
    {
        if (req.Temperature is < 0 or > 2) return BadRequest("温度须在 0-2 之间");
        if (req.MaxTokens is < 128 or > 32768) return BadRequest("maxTokens 须在 128-32768 之间");
        if (req.HumanReviewThreshold is < 0 or > 1) return BadRequest("低置信度阈值须在 0-1 之间");
        await _settings.SaveAsync(req, ct);
        return Ok(await _settings.LoadAsync(ct));
    }

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
