using AgoraIn.Core.Entities;
using AgoraIn.Core.Security;
using AgoraIn.Server.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>CSES 课表 API。前缀 /api/v4/timetable</summary>
[ApiController]
[Route("api/v4/timetable")]
[Authorize]
[RequirePermission(Permissions.TimetableManage)]
public class TimetableController : ControllerBase
{
    private readonly ServerDbContext _db;
    public TimetableController(ServerDbContext db) => _db = db;

    /// <summary>完整课表（科目 + 时间布局 + 班级课表），供桌面端/小程序拉取。</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? classId)
    {
        var subjects = await _db.Subjects
            .Where(s => classId == null || s.ClassId == classId || s.ClassId == null)
            .ToListAsync();

        var layouts = await _db.TimeLayouts
            .Where(t => classId == null || t.ClassId == classId || t.ClassId == null)
            .ToListAsync();

        var layoutIds = layouts.Select(l => l.Id).ToList();
        var entries = await _db.TimeLayoutEntries
            .Where(e => layoutIds.Contains(e.TimeLayoutId))
            .OrderBy(e => e.Index)
            .ToListAsync();

        var plans = await _db.ClassPlans
            .Where(p => classId == null || p.ClassId == classId)
            .ToListAsync();

        var planIds = plans.Select(p => p.Id).ToList();
        var planEntries = await _db.ClassPlanEntries
            .Where(e => planIds.Contains(e.ClassPlanId))
            .ToListAsync();

        return Ok(new
        {
            subjects,
            timeLayouts = layouts.Select(l => new
            {
                l.Id,
                l.Name,
                l.ClassId,
                entries = entries.Where(e => e.TimeLayoutId == l.Id),
            }),
            classPlans = plans.Select(p => new
            {
                p.Id,
                p.Name,
                p.ClassId,
                p.TimeLayoutId,
                p.IsActive,
                entries = planEntries.Where(e => e.ClassPlanId == p.Id),
            }),
        });
    }

    // ── 科目 ──

    [HttpPost("subjects")]
    public async Task<IActionResult> CreateSubject([FromBody] Subject subject)
    {
        _db.Subjects.Add(subject);
        await _db.SaveChangesAsync();
        return Created("", subject);
    }

    [HttpDelete("subjects/{id}")]
    public async Task<IActionResult> DeleteSubject(string id)
    {
        var s = await _db.Subjects.FindAsync(id);
        if (s == null) return NotFound();
        _db.Subjects.Remove(s);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── 时间布局 ──

    /// <summary>创建/替换时间布局（含节次，全量覆盖）。</summary>
    [HttpPost("layouts")]
    public async Task<IActionResult> SaveLayout([FromBody] TimeLayoutSaveRequest req)
    {
        var layout = string.IsNullOrEmpty(req.Id) ? new TimeLayout() : await _db.TimeLayouts.FindAsync(req.Id);
        if (layout == null) layout = new TimeLayout();

        layout.Name = req.Name;
        layout.ClassId = req.ClassId;

        if (string.IsNullOrEmpty(req.Id))
            _db.TimeLayouts.Add(layout);
        else
            _db.TimeLayoutEntries.RemoveRange(
                await _db.TimeLayoutEntries.Where(e => e.TimeLayoutId == layout.Id).ToListAsync());

        foreach (var e in req.Entries)
        {
            _db.TimeLayoutEntries.Add(new TimeLayoutEntry
            {
                TimeLayoutId = layout.Id,
                Index = e.Index,
                StartTime = e.StartTime,
                EndTime = e.EndTime,
                Kind = e.IsBreak ? TimeEntryKind.Break : TimeEntryKind.Class,
                Name = e.Name,
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { layout.Id, layout.Name });
    }

    // ── 班级课表 ──

    /// <summary>保存班级课表（全量覆盖某布局的排课）。</summary>
    [HttpPost("plans")]
    public async Task<IActionResult> SavePlan([FromBody] ClassPlanSaveRequest req)
    {
        var plan = string.IsNullOrEmpty(req.Id) ? new ClassPlan() : await _db.ClassPlans.FindAsync(req.Id);
        if (plan == null) plan = new ClassPlan();

        plan.Name = req.Name;
        plan.ClassId = req.ClassId ?? "";
        plan.TimeLayoutId = req.TimeLayoutId;
        plan.IsActive = req.IsActive;
        plan.UpdatedAt = DateTime.Now;

        if (string.IsNullOrEmpty(req.Id))
            _db.ClassPlans.Add(plan);
        else
            _db.ClassPlanEntries.RemoveRange(
                await _db.ClassPlanEntries.Where(e => e.ClassPlanId == plan.Id).ToListAsync());

        foreach (var e in req.Entries)
        {
            _db.ClassPlanEntries.Add(new ClassPlanEntry
            {
                ClassPlanId = plan.Id,
                WeekDay = e.WeekDay,
                SlotIndex = e.SlotIndex,
                SubjectId = e.SubjectId,
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { plan.Id, plan.Name });
    }

    /// <summary>当前生效课表（供桌面端课表驱动行为使用）。</summary>
    [HttpGet("active")]
    public async Task<IActionResult> Active([FromQuery] string? classId)
    {
        var plan = await _db.ClassPlans
            .FirstOrDefaultAsync(p => p.IsActive && (classId == null || p.ClassId == classId));
        if (plan == null) return Ok(new { hasPlan = false });

        var entries = await _db.TimeLayoutEntries
            .Where(e => e.TimeLayoutId == plan.TimeLayoutId && e.Kind == TimeEntryKind.Class)
            .OrderBy(e => e.Index)
            .ToListAsync();

        var planEntries = await _db.ClassPlanEntries
            .Where(e => e.ClassPlanId == plan.Id)
            .ToListAsync();

        var subjects = await _db.Subjects.ToListAsync();

        return Ok(new
        {
            hasPlan = true,
            planId = plan.Id,
            planName = plan.Name,
            slots = entries.Select(e => new { e.Index, start = e.StartTime, end = e.EndTime, e.Name }),
            schedule = planEntries.Select(e => new
            {
                e.WeekDay,
                e.SlotIndex,
                subjectId = e.SubjectId,
                subjectName = subjects.FirstOrDefault(s => s.Id == e.SubjectId)?.Name ?? "",
            }),
        });
    }
}

public record TimeLayoutSaveRequest(
    string? Id, string Name, string? ClassId, List<TimeEntryDto> Entries);

public record TimeEntryDto(int Index, TimeOnly StartTime, TimeOnly EndTime, bool IsBreak, string? Name);

public record ClassPlanSaveRequest(
    string? Id, string Name, string? ClassId, string TimeLayoutId, bool IsActive, List<ClassPlanEntryDto> Entries);

public record ClassPlanEntryDto(int WeekDay, int SlotIndex, string SubjectId);
