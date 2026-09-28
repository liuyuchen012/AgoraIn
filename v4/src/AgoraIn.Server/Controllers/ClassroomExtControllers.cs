using AgoraIn.Core.Domain;
using AgoraIn.Core.Entities;
using AgoraIn.Core.Security;
using AgoraIn.Server.Models;
using AgoraIn.Server.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>座位编排 API。前缀 /api/v4/seats。一个班级可存多套布局（日常/考试），支持快照与随机换座。</summary>
[ApiController]
[Route("api/v4/seats")]
[Authorize]
[RequirePermission(Permissions.ClassesManage)]
public class SeatsController : ControllerBase
{
    private readonly ServerDbContext _db;
    private readonly IHubContext<LiveHub> _hub;

    public SeatsController(ServerDbContext db, IHubContext<LiveHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    /// <summary>班级的布局列表（含历史快照）。</summary>
    [HttpGet("charts")]
    public async Task<IActionResult> Charts([FromQuery] string classId)
    {
        var list = await _db.SeatCharts
            .Where(c => c.ClassId == classId)
            .OrderByDescending(c => c.IsActive).ThenByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id, c.ClassId, c.Name, c.Rows, c.Cols, c.Podium, c.IsActive, c.CreatedAt,
                seatCount = _db.Seats.Count(s => s.ChartId == c.Id),
                occupiedCount = _db.Seats.Count(s => s.ChartId == c.Id && s.StudentId != null),
            })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>创建布局（自动生成行列座位网格）。</summary>
    [HttpPost("charts")]
    public async Task<IActionResult> CreateChart([FromBody] SeatChart chart)
    {
        if (chart.Rows is < 1 or > 30 || chart.Cols is < 1 or > 30) return BadRequest("行列数须在 1-30 之间");
        chart.Id = Guid.NewGuid().ToString();
        chart.CreatedAt = DateTime.Now;
        if (chart.IsActive) await DeactivateOthersAsync(chart.ClassId, null);
        _db.SeatCharts.Add(chart);

        for (var r = 0; r < chart.Rows; r++)
        {
            for (var c = 0; c < chart.Cols; c++)
            {
                _db.Seats.Add(new Seat { ChartId = chart.Id, Row = r, Col = c });
            }
        }
        await _db.SaveChangesAsync();
        return Created("", chart);
    }

    /// <summary>布局详情（含全部座位）。</summary>
    [HttpGet("charts/{id}")]
    public async Task<IActionResult> Chart(string id)
    {
        var chart = await _db.SeatCharts.FindAsync(id);
        if (chart == null) return NotFound();
        var seats = await _db.Seats
            .Where(s => s.ChartId == id)
            .OrderBy(s => s.Row).ThenBy(s => s.Col)
            .ToListAsync();
        // 回填学生姓名，前端免二次查询
        var studentIds = seats.Where(s => s.StudentId != null).Select(s => s.StudentId!).Distinct().ToList();
        var names = await _db.Students
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);
        return Ok(new
        {
            chart,
            seats = seats.Select(s => new
            {
                s.Id, s.Row, s.Col, s.Disabled, s.GroupName, s.StudentId,
                studentName = s.StudentId != null && names.TryGetValue(s.StudentId, out var n) ? n : null,
            }),
        });
    }

    /// <summary>删除布局。</summary>
    [HttpDelete("charts/{id}")]
    public async Task<IActionResult> DeleteChart(string id)
    {
        var chart = await _db.SeatCharts.FindAsync(id);
        if (chart == null) return NotFound();
        var seats = await _db.Seats.Where(s => s.ChartId == id).ToListAsync();
        _db.Seats.RemoveRange(seats);
        _db.SeatCharts.Remove(chart);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>设为当前生效布局（同班级其余布局自动停用）。</summary>
    [HttpPut("charts/{id}/activate")]
    public async Task<IActionResult> Activate(string id)
    {
        var chart = await _db.SeatCharts.FindAsync(id);
        if (chart == null) return NotFound();
        await DeactivateOthersAsync(chart.ClassId, id);
        chart.IsActive = true;
        await _db.SaveChangesAsync();
        return Ok(chart);
    }

    /// <summary>更新单个座位（就座学生 / 禁用 / 分组）。</summary>
    [HttpPut("seats/{seatId}")]
    public async Task<IActionResult> UpdateSeat(string seatId, [FromBody] SeatUpdateRequest req)
    {
        var seat = await _db.Seats.FindAsync(seatId);
        if (seat == null) return NotFound();
        var chart = await _db.SeatCharts.FindAsync(seat.ChartId);

        if (req.StudentId != null && req.StudentId.Length > 0)
        {
            // 座位与学生在布局内唯一绑定：清掉该学生原来占的座位
            var others = await _db.Seats
                .Where(s => s.ChartId == seat.ChartId && s.StudentId == req.StudentId && s.Id != seatId)
                .ToListAsync();
            foreach (var o in others) o.StudentId = null;
            seat.StudentId = req.StudentId;
        }
        else if (req.ClearStudent)
        {
            seat.StudentId = null;
        }
        if (req.Disabled != null) seat.Disabled = req.Disabled.Value;
        if (req.GroupName != null) seat.GroupName = req.GroupName.Length == 0 ? null : req.GroupName;

        await _db.SaveChangesAsync();
        if (chart != null)
        {
            await _hub.Clients.Group(chart.ClassId).SendAsync("SeatChartUpdate", new { chartId = chart.Id });
        }
        return Ok(seat);
    }

    /// <summary>
    /// 随机换座：把已就座学生随机重排（换座前自动留存一份"快照"布局，可回溯/撤销）。
    /// 可通过 excludeStudentIds 排除特定学生（其座位保持原样）。
    /// </summary>
    [HttpPost("charts/{id}/shuffle")]
    public async Task<IActionResult> Shuffle(string id, [FromBody] ShuffleRequest? req)
    {
        var chart = await _db.SeatCharts.FindAsync(id);
        if (chart == null) return NotFound();
        var seats = await _db.Seats.Where(s => s.ChartId == id && !s.Disabled).ToListAsync();
        var exclude = req?.ExcludeStudentIds ?? [];

        // 1. 快照：复制当前布局为历史（不激活）
        var snapshot = new SeatChart
        {
            ClassId = chart.ClassId,
            Name = $"快照 {DateTime.Now:yyyy-MM-dd HH:mm}",
            Rows = chart.Rows,
            Cols = chart.Cols,
            Podium = chart.Podium,
            IsActive = false,
        };
        _db.SeatCharts.Add(snapshot);
        await _db.SaveChangesAsync();
        _db.Seats.AddRange(seats.Select(s => new Seat
        {
            ChartId = snapshot.Id, Row = s.Row, Col = s.Col,
            Disabled = s.Disabled, GroupName = s.GroupName, StudentId = s.StudentId,
        }));

        // 2. 随机重排：被排除学生的座位保持不动，其余学生洗牌回填到剩余可坐座位
        var excludeSet = exclude.ToHashSet();
        var fixedSeats = seats.Where(s => s.StudentId != null && excludeSet.Contains(s.StudentId)).ToHashSet();
        var movableStudents = seats.Where(s => s.StudentId != null && !excludeSet.Contains(s.StudentId))
            .Select(s => s.StudentId!).ToList();
        var availableSeats = seats.Where(s => !fixedSeats.Contains(s)).ToList();

        var rng = new Random();
        for (var i = availableSeats.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (availableSeats[i], availableSeats[j]) = (availableSeats[j], availableSeats[i]);
        }

        foreach (var seat in availableSeats) seat.StudentId = null;
        for (var i = 0; i < movableStudents.Count && i < availableSeats.Count; i++)
        {
            availableSeats[i].StudentId = movableStudents[i];
        }

        await _db.SaveChangesAsync();
        await _hub.Clients.Group(chart.ClassId).SendAsync("SeatChartUpdate", new { chartId = chart.Id });
        return Ok(new { snapshotId = snapshot.Id, moved = movableStudents.Count });
    }

    private async Task DeactivateOthersAsync(string classId, string? keepId)
    {
        var others = await _db.SeatCharts.Where(c => c.ClassId == classId && c.Id != keepId).ToListAsync();
        foreach (var c in others) c.IsActive = false;
    }
}

public record SeatUpdateRequest(string? StudentId, bool ClearStudent, bool? Disabled, string? GroupName);
public record ShuffleRequest(string[]? ExcludeStudentIds);

/// <summary>点名 API。前缀 /api/v4/rollcall。三种模式 + 结果标记 + 积分联动 + 场次报表。</summary>
[ApiController]
[Route("api/v4/rollcall")]
[Authorize]
[RequirePermission(Permissions.RollCallOperate)]
public class RollCallController : ControllerBase
{
    private readonly ServerDbContext _db;
    private readonly IHubContext<LiveHub> _hub;

    public RollCallController(ServerDbContext db, IHubContext<LiveHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    /// <summary>创建点名场次。</summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] RollCallSession session)
    {
        session.Id = Guid.NewGuid().ToString();
        session.StartedAt = DateTime.Now;
        _db.RollCallSessions.Add(session);
        await _db.SaveChangesAsync();
        return Created("", session);
    }

    /// <summary>场次列表（历史）。</summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions([FromQuery] string? classId, [FromQuery] int take = 50)
    {
        var q = _db.RollCallSessions.AsQueryable();
        if (!string.IsNullOrEmpty(classId)) q = q.Where(s => s.ClassId == classId);
        var list = await q.OrderByDescending(s => s.StartedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(s => new
            {
                s.Id, s.ClassId, s.Mode, s.Subject, s.StartedAt, s.EndedAt, s.WeightFairness,
                calledCount = _db.RollCallRecords.Count(r => r.SessionId == s.Id),
            })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>结束场次。</summary>
    [HttpPost("sessions/{id}/end")]
    public async Task<IActionResult> EndSession(string id)
    {
        var session = await _db.RollCallSessions.FindAsync(id);
        if (session == null) return NotFound();
        session.EndedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(session);
    }

    /// <summary>
    /// 点下一名学生。随机模式（可加权"近期未点优先"）、顺序模式（按学号轮转）、
    /// 指定模式（请求体 studentId）。落点即时写入记录（Pending），并广播 RollCallUpdate。
    /// </summary>
    [HttpPost("sessions/{id}/pick")]
    public async Task<IActionResult> Pick(string id, [FromBody] PickRequest? req)
    {
        var session = await _db.RollCallSessions.FindAsync(id);
        if (session == null) return NotFound();
        if (session.EndedAt != null) return BadRequest("场次已结束");

        var students = await _db.Students
            .Where(s => s.ClassId == session.ClassId && s.Status == StudentStatus.Enrolled)
            .OrderBy(s => s.StudentNo).ThenBy(s => s.Name)
            .ToListAsync();
        if (students.Count == 0) return BadRequest("班级暂无在读学生");

        var stats = await _db.RollCallRecords
            .Where(r => r.SessionId == id)
            .GroupBy(r => r.StudentId)
            .Select(g => new { StudentId = g.Key, Times = g.Count(), Last = g.Max(x => x.CalledAt) })
            .ToDictionaryAsync(x => x.StudentId);

        string? picked;
        if (session.Mode == RollCallMode.Assigned)
        {
            picked = req?.StudentId;
            if (picked == null || students.All(s => s.Id != picked)) return BadRequest("指定模式必须传有效 studentId");
        }
        else
        {
            var candidates = students
                .Select(s => new RollCallCandidate(
                    s.Id,
                    stats.TryGetValue(s.Id, out var st) ? st.Last : null,
                    stats.TryGetValue(s.Id, out var st2) ? st2.Times : 0))
                .ToList();
            picked = session.Mode == RollCallMode.Sequential
                ? RollCallPicker.PickSequential(candidates)
                : session.WeightFairness
                    ? RollCallPicker.PickWeighted(candidates, DateTime.Now)
                    : PickPlain(candidates);
        }

        var student = students.First(s => s.Id == picked);
        var record = new RollCallRecord
        {
            SessionId = id,
            StudentId = student.Id,
            Result = RollCallResult.Pending,
            CalledAt = DateTime.Now,
        };
        _db.RollCallRecords.Add(record);
        await _db.SaveChangesAsync();

        await _hub.Clients.Group(session.ClassId).SendAsync("RollCallUpdate",
            new { sessionId = id, recordId = record.Id, studentId = student.Id, studentName = student.Name, mode = session.Mode.ToString() });

        return Ok(new { recordId = record.Id, studentId = student.Id, studentName = student.Name, studentNo = student.StudentNo });
    }

    /// <summary>未启用加权时的均匀随机。</summary>
    private static string? PickPlain(IReadOnlyList<RollCallCandidate> candidates)
        => candidates.Count == 0 ? null : candidates[Random.Shared.Next(candidates.Count)].StudentId;

    /// <summary>
    /// 标记点名结果（答到/缺勤/迟到）。可带 pointDelta 直接产生积分联动
    /// （Source=RollCall，SourceId=点名记录 Id，幂等：重复提交不重复加分）。
    /// </summary>
    [HttpPost("records/{recordId}/result")]
    public async Task<IActionResult> MarkResult(string recordId, [FromBody] MarkResultRequest req)
    {
        var record = await _db.RollCallRecords.FindAsync(recordId);
        if (record == null) return NotFound();
        if (req.Result is < RollCallResult.Present or > RollCallResult.Pending) return BadRequest("无效的点名结果");

        record.Result = req.Result;
        record.ResultAt = DateTime.Now;

        if (req.PointDelta != 0)
        {
            var exists = await _db.PointRecords.AnyAsync(p => p.SourceId == record.Id && p.Source == PointSource.RollCall);
            if (!exists)
            {
                var point = new PointRecord
                {
                    StudentId = record.StudentId,
                    Delta = req.PointDelta,
                    Reason = $"点名联动：{req.Result switch
                    {
                        RollCallResult.Present => "答到",
                        RollCallResult.Late => "迟到",
                        RollCallResult.Absent => "缺勤",
                        _ => "点名",
                    }}",
                    OperatorName = User.Identity?.Name ?? "系统",
                    Source = PointSource.RollCall,
                    SourceId = record.Id,
                };
                _db.PointRecords.Add(point);
                await _db.SaveChangesAsync();
                record.PointRecordId = point.Id;
            }
        }

        var session = await _db.RollCallSessions.FindAsync(record.SessionId);
        await _db.SaveChangesAsync();
        if (session != null)
        {
            await _hub.Clients.Group(session.ClassId).SendAsync("RollCallUpdate",
                new { sessionId = session.Id, recordId = record.Id, result = req.Result.ToString() });
        }
        return Ok(record);
    }

    /// <summary>点名记录查询（可按场次/学生/时间筛选，导出用）。</summary>
    [HttpGet("records")]
    public async Task<IActionResult> Records(
        [FromQuery] string? sessionId, [FromQuery] string? studentId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int take = 500)
    {
        var q = _db.RollCallRecords.AsQueryable();
        if (!string.IsNullOrEmpty(sessionId)) q = q.Where(r => r.SessionId == sessionId);
        if (!string.IsNullOrEmpty(studentId)) q = q.Where(r => r.StudentId == studentId);
        if (from != null) q = q.Where(r => r.CalledAt >= from);
        if (to != null) q = q.Where(r => r.CalledAt <= to);
        var records = await q.OrderByDescending(r => r.CalledAt)
            .Take(Math.Clamp(take, 1, 2000))
            .ToListAsync();

        var studentIds = records.Select(r => r.StudentId).Distinct().ToList();
        var names = await _db.Students
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);
        return Ok(records.Select(r => new
        {
            r.Id, r.SessionId, r.StudentId,
            studentName = names.TryGetValue(r.StudentId, out var n) ? n : "",
            r.Result, r.CalledAt, r.ResultAt, r.PointRecordId,
        }));
    }

    /// <summary>点名场次报表：出勤率、逐生被点次数与结果分布。</summary>
    [HttpGet("sessions/{id}/report")]
    public async Task<IActionResult> Report(string id)
    {
        var session = await _db.RollCallSessions.FindAsync(id);
        if (session == null) return NotFound();

        var records = await _db.RollCallRecords.Where(r => r.SessionId == id).ToListAsync();
        var studentIds = records.Select(r => r.StudentId).Distinct().ToList();
        var names = await _db.Students
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        var perStudent = records.GroupBy(r => r.StudentId)
            .Select(g => new
            {
                studentId = g.Key,
                studentName = names.TryGetValue(g.Key, out var n) ? n : "",
                calledTimes = g.Count(),
                present = g.Count(x => x.Result == RollCallResult.Present),
                late = g.Count(x => x.Result == RollCallResult.Late),
                absent = g.Count(x => x.Result == RollCallResult.Absent),
                pending = g.Count(x => x.Result == RollCallResult.Pending),
            })
            .OrderByDescending(x => x.calledTimes)
            .ToList();

        var finished = records.Where(r => r.Result != RollCallResult.Pending).ToList();
        var presentCount = finished.Count(r => r.Result is RollCallResult.Present or RollCallResult.Late);
        return Ok(new
        {
            session,
            totalCalled = records.Count,
            present = records.Count(r => r.Result == RollCallResult.Present),
            late = records.Count(r => r.Result == RollCallResult.Late),
            absent = records.Count(r => r.Result == RollCallResult.Absent),
            pending = records.Count(r => r.Result == RollCallResult.Pending),
            attendanceRate = finished.Count == 0 ? 0 : Math.Round(presentCount * 100.0 / finished.Count, 1),
            perStudent,
        });
    }
}

public record PickRequest(string? StudentId);
public record MarkResultRequest(RollCallResult Result, int PointDelta);
