using AgoraIn.Core.Entities;
using AgoraIn.Core.Security;
using AgoraIn.Server.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 班级管理 API。前缀 /api/v4/classes
/// </summary>
[ApiController]
[Route("api/v4/classes")]
[Authorize]
[RequirePermission(Permissions.ClassesManage)]
public class ClassesController : ControllerBase
{
    private readonly ServerDbContext _db;
    public ClassesController(ServerDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await _db.Classes.ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AgoraIn.Core.Entities.ClassInfo cls)
    {
        _db.Classes.Add(cls);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = cls.Id }, cls);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var cls = await _db.Classes.FindAsync(id);
        return cls == null ? NotFound() : Ok(cls);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] AgoraIn.Core.Entities.ClassInfo cls)
    {
        var existing = await _db.Classes.FindAsync(id);
        if (existing == null) return NotFound();
        existing.Name = cls.Name;
        existing.Grade = cls.Grade;
        existing.Remark = cls.Remark;
        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var cls = await _db.Classes.FindAsync(id);
        if (cls == null) return NotFound();
        _db.Classes.Remove(cls);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

/// <summary>学生管理 API。前缀 /api/v4/students</summary>
[ApiController]
[Route("api/v4/students")]
[Authorize]
[RequirePermission(Permissions.ClassesManage)]
public class StudentsController : ControllerBase
{
    private readonly ServerDbContext _db;
    public StudentsController(ServerDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? classId)
    {
        var q = _db.Students.AsQueryable();
        if (!string.IsNullOrEmpty(classId)) q = q.Where(s => s.ClassId == classId);
        return Ok(await q.ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AgoraIn.Core.Entities.Student stu)
    {
        _db.Students.Add(stu);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = stu.Id }, stu);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var stu = await _db.Students.FindAsync(id);
        return stu == null ? NotFound() : Ok(stu);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] AgoraIn.Core.Entities.Student stu)
    {
        var existing = await _db.Students.FindAsync(id);
        if (existing == null) return NotFound();
        existing.Name = stu.Name;
        existing.StudentNo = stu.StudentNo;
        existing.Gender = stu.Gender;
        existing.Status = stu.Status;
        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var stu = await _db.Students.FindAsync(id);
        if (stu == null) return NotFound();
        _db.Students.Remove(stu);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

/// <summary>打卡 API。前缀 /api/v4/checkin</summary>
[ApiController]
[Route("api/v4/checkin")]
[Authorize]
[RequirePermission(Permissions.CheckInOperate)]
public class CheckInController : ControllerBase
{
    private readonly ServerDbContext _db;
    private readonly IHubContext<LiveHub> _hub;

    public CheckInController(ServerDbContext db, IHubContext<LiveHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    /// <summary>获取打卡记录（可按任务/学生筛选）。</summary>
    [HttpGet("records")]
    public async Task<IActionResult> GetRecords([FromQuery] string? taskId, [FromQuery] string? studentId)
    {
        var q = _db.CheckInRecords.AsQueryable();
        if (!string.IsNullOrEmpty(taskId)) q = q.Where(r => r.TaskId == taskId);
        if (!string.IsNullOrEmpty(studentId)) q = q.Where(r => r.StudentId == studentId);
        var records = await q
            .OrderBy(r => r.CheckedAt)
            .ToListAsync();
        return Ok(records);
    }

    /// <summary>提交打卡记录（桌面端同步上传）。</summary>
    [HttpPost("records")]
    public async Task<IActionResult> SubmitRecords([FromBody] List<AgoraIn.Core.Entities.CheckInRecord> records)
    {
        foreach (var r in records)
        {
            // 幂等：同一学生同一任务同一时间不重复插入
            var exists = await _db.CheckInRecords.AnyAsync(x =>
                x.TaskId == r.TaskId && x.StudentId == r.StudentId &&
                x.CheckedAt == r.CheckedAt);
            if (!exists) _db.CheckInRecords.Add(r);
        }
        await _db.SaveChangesAsync();
        return Ok(new { count = records.Count });
    }

    // ── 扫码签到（v3 语义：短码 + 二维码 + 签到密码 + 教室/科目） ──

    /// <summary>任务列表（含签到进度，学生端/管理端用）。</summary>
    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks()
    {
        var tasks = await _db.CheckInTasks
            .Where(t => t.Kind == AgoraIn.Core.Entities.CheckInTaskKind.Task)
            .OrderBy(t => t.SortOrder)
            .ToListAsync();

        var result = new List<object>();
        foreach (var t in tasks)
        {
            var roster = await _db.TaskRosterEntries.CountAsync(r => r.TaskId == t.Id);
            var checkedCount = await _db.CheckInRecords
                .Where(r => r.TaskId == t.Id)
                .Select(r => r.StudentId)
                .Distinct()
                .CountAsync();
            result.Add(new { taskId = t.Id, name = t.Name, subject = t.Subject, checkedCount, totalCount = roster });
        }
        return Ok(result);
    }

    /// <summary>生成扫码签到码（教师端/大屏展示二维码用）。</summary>
    [HttpPost("signin-codes")]
    public async Task<IActionResult> CreateSignInCode([FromBody] CreateSignInCodeRequest req)
    {
        if (string.IsNullOrEmpty(req.TaskId)) return BadRequest("必须指定签到任务");
        var task = await _db.CheckInTasks.FindAsync(req.TaskId);
        if (task == null) return NotFound("签到任务不存在");

        // 下码旧的未过期码，避免大屏多个码并存
        var oldCodes = await _db.SignInCodes.Where(c => c.TaskId == req.TaskId && c.Active).ToListAsync();
        foreach (var c in oldCodes) c.Active = false;

        Core.Entities.SignInCode code;
        do
        {
            code = new SignInCode
            {
                Code = GenerateShortCode(),
                TaskId = req.TaskId,
                Classroom = req.Classroom,
                Subject = req.Subject ?? task.Subject,
                Password = string.IsNullOrWhiteSpace(req.Password) ? null : req.Password.Trim(),
                CreatedBy = User.Identity?.Name ?? "",
                ExpiresAt = req.ExpiresMinutes is > 0 ? DateTime.Now.AddMinutes(req.ExpiresMinutes.Value) : null,
            };
        }
        while (await _db.SignInCodes.AnyAsync(c => c.Code == code.Code && c.Active));

        _db.SignInCodes.Add(code);
        await _db.SaveChangesAsync();
        return Created("", code);
    }

    /// <summary>当前生效的签到码列表（大屏渲染二维码用）。</summary>
    [HttpGet("signin-codes")]
    public async Task<IActionResult> ListSignInCodes()
    {
        var codes = await _db.SignInCodes
            .Where(c => c.Active)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        return Ok(codes.Where(c => c.IsUsable(DateTime.Now)));
    }

    /// <summary>下码（结束该签到）。</summary>
    [HttpDelete("signin-codes/{id}")]
    public async Task<IActionResult> DeactivateSignInCode(string id)
    {
        var code = await _db.SignInCodes.FindAsync(id);
        if (code == null) return NotFound();
        code.Active = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// 学生扫码签到（匿名：凭短码即可，签到码本身即凭据；设置了密码时须携带）。
    /// 幂等：同一学生同一任务重复提交不重复记录。
    /// 匿名请求区域上下文为主区域，须按签到码显式切换到其所属区域（隔离边界）。
    /// </summary>
    [HttpPost("scan")]
    [AllowAnonymous]
    public async Task<IActionResult> Scan([FromBody] ScanSignInRequest req)
    {
        var code = (await _db.SignInCodes
                .IgnoreQueryFilters()
                .Where(c => c.Code == req.Code && c.Active)
                .ToListAsync())
            .FirstOrDefault(c => c.IsUsable(DateTime.Now));
        if (code == null) return BadRequest(new { error = "签到码无效或已过期" });

        // 切换到签到码所属区域（后续学生查询/打卡写入都落在该区域；RegionId 为影子属性）
        AgoraIn.Server.Security.RegionContext.Set(
            Microsoft.EntityFrameworkCore.EF.Property<string>(code!, "RegionId"));

        if (!string.IsNullOrEmpty(code.Password) &&
            !string.Equals(code.Password, req.Password?.Trim(), StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "签到密码不正确" });

        if (string.IsNullOrEmpty(req.StudentId) && string.IsNullOrEmpty(req.Name))
            return BadRequest(new { error = "缺少学生标识或姓名" });

        // 学生定位：优先 StudentId；扫码页只填姓名时按任务名单匹配（重名须填学号）
        AgoraIn.Core.Entities.Student? student = null;
        if (!string.IsNullOrEmpty(req.StudentId))
        {
            student = await _db.Students.FindAsync(req.StudentId);
        }
        else
        {
            var rosterIds = await _db.TaskRosterEntries
                .Where(r => r.TaskId == code.TaskId)
                .Select(r => r.StudentId)
                .ToListAsync();
            var candidates = _db.Students.Where(s => s.Name == req.Name!.Trim());
            if (rosterIds.Count > 0)
                candidates = candidates.Where(s => rosterIds.Contains(s.Id));
            var matches = await candidates.ToListAsync();
            if (matches.Count > 1)
                return BadRequest(new { error = "存在重名学生，请填写学号后再签到" });
            student = matches.FirstOrDefault();
        }
        if (student == null) return NotFound(new { error = "学生不存在或不在该任务名单中" });

        var exists = await _db.CheckInRecords.AnyAsync(r =>
            r.TaskId == code.TaskId && r.StudentId == student.Id);
        if (exists)
            return Ok(new { success = true, message = "已签过到，请勿重复提交", studentName = student.Name, duplicate = true, rank = (int?)null });

        var record = new AgoraIn.Core.Entities.CheckInRecord
        {
            TaskId = code.TaskId,
            StudentId = student.Id,
            CheckedAt = DateTime.Now,
            Source = AgoraIn.Core.Entities.CheckInSource.Scan,
        };
        _db.CheckInRecords.Add(record);
        await _db.SaveChangesAsync();

        var rank = await _db.CheckInRecords
            .Where(r => r.TaskId == code.TaskId)
            .Select(r => r.StudentId)
            .Distinct()
            .CountAsync();

        // 实时推送到该学生所在班级（大屏/桌面端即时刷新）
        await _hub.Clients.Group(student.ClassId).SendAsync("CheckInUpdate",
            new { taskId = code.TaskId, studentId = student.Id, studentName = student.Name, source = "scan" });

        return Ok(new
        {
            success = true,
            message = "签到成功",
            studentName = student.Name,
            rank,
            duplicate = false,
            checkedAt = record.CheckedAt,
        });
    }

    private static string GenerateShortCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }
}

public record CreateSignInCodeRequest(string TaskId, string? Classroom, string? Subject, string? Password, int? ExpiresMinutes);
public record ScanSignInRequest(string Code, string? Password, string? StudentId, string? Name);

/// <summary>课时 API。前缀 /api/v4/classhours</summary>
[ApiController]
[Route("api/v4/classhours")]
[Authorize]
[RequirePermission(Permissions.ClassHoursManage)]
public class ClassHoursController : ControllerBase
{
    private readonly ServerDbContext _db;
    public ClassHoursController(ServerDbContext db) => _db = db;

    [HttpGet("accounts")]
    public async Task<IActionResult> GetAccounts([FromQuery] string? classId)
    {
        var q = _db.ClassHourAccounts.AsQueryable();
        return Ok(await q.ToListAsync());
    }

    [HttpPost("records")]
    public async Task<IActionResult> SubmitRecords([FromBody] List<AgoraIn.Core.Entities.ClassHourRecord> records)
    {
        foreach (var r in records)
        {
            if (!string.IsNullOrEmpty(r.SlotKey))
            {
                var exists = await _db.ClassHourRecords.AnyAsync(x => x.SlotKey == r.SlotKey);
                if (exists) continue; // 幂等
            }
            _db.ClassHourRecords.Add(r);
        }
        await _db.SaveChangesAsync();
        return Ok(new { count = records.Count });
    }
}

/// <summary>设备管理 API。前缀 /api/v4/devices</summary>
[ApiController]
[Route("api/v4/devices")]
[Authorize]
[RequirePermission(Permissions.DevicesManage)]
public class DevicesController : ControllerBase
{
    private readonly ServerDbContext _db;
    public DevicesController(ServerDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await _db.Devices.ToListAsync());

    /// <summary>注册设备（受授权限制：未激活/已过期/超出设备上限时拒绝）。</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(
        [FromBody] DeviceRegisterRequest req,
        [FromServices] Services.LicenseService license,
        CancellationToken ct)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceUuid == req.Uuid, ct);

        // 仅「新设备」受授权限制：已注册设备（含重连）不受影响，避免授权到期直接停服
        if (device == null)
        {
            var denial = await license.CheckDeviceRegistrationAsync(ct);
            if (denial != null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = denial });
            }
        }

        if (device == null)
        {
            device = new Device { DeviceUuid = req.Uuid, DeviceName = req.Name, PublicKey = req.PublicKey };
            _db.Devices.Add(device);
        }
        else
        {
            device.DeviceName = req.Name;
            device.LastSeen = DateTime.Now;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { deviceId = device.Id });
    }

    [HttpPost("{id}/heartbeat")]
    [Authorize]
    public async Task<IActionResult> Heartbeat(int id)
    {
        var device = await _db.Devices.FindAsync(id);
        if (device == null) return NotFound();
        device.LastSeen = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok();
    }
}

public record DeviceRegisterRequest(string Uuid, string Name, string PublicKey);
