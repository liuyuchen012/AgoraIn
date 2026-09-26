using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 班级管理 API。前缀 /api/v4/classes
/// </summary>
[ApiController]
[Route("api/v4/classes")]
[Authorize]
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
public class CheckInController : ControllerBase
{
    private readonly ServerDbContext _db;
    public CheckInController(ServerDbContext db) => _db = db;

    /// <summary>获取指定任务的打卡记录。</summary>
    [HttpGet("records")]
    public async Task<IActionResult> GetRecords([FromQuery] string taskId)
    {
        var records = await _db.CheckInRecords
            .Where(r => r.TaskId == taskId)
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
}

/// <summary>课时 API。前缀 /api/v4/classhours</summary>
[ApiController]
[Route("api/v4/classhours")]
[Authorize]
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
public class DevicesController : ControllerBase
{
    private readonly ServerDbContext _db;
    public DevicesController(ServerDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await _db.Devices.ToListAsync());

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] DeviceRegisterRequest req)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceUuid == req.Uuid);
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
        await _db.SaveChangesAsync();
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
