using AgoraIn.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 家长端 API。前缀 /api/v4/parent
/// 家长绑定孩子、查看通知/概览/资源/值日、留言。
/// </summary>
[ApiController]
[Route("api/v4/parent")]
[Authorize]
public class ParentController : ControllerBase
{
    private readonly ServerDbContext _db;
    public ParentController(ServerDbContext db) => _db = db;

    /// <summary>用邀请码绑定孩子。</summary>
    [HttpPost("bind")]
    public async Task<IActionResult> Bind([FromBody] BindRequest req)
    {
        var binding = await _db.ParentBindings
            .FirstOrDefaultAsync(b => b.InviteCode == req.InviteCode && b.Status == Core.Entities.ParentBindingStatus.Pending);
        if (binding == null) return BadRequest(new { error = "邀请码无效或已被使用" });

        var username = User.Identity?.Name ?? "";
        binding.ParentUserId = username;
        binding.ParentAlias = req.Alias ?? "";
        binding.Status = Core.Entities.ParentBindingStatus.Bound;
        binding.BoundAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return Ok(new { message = "绑定成功", studentId = binding.StudentId });
    }

    /// <summary>获取已绑定的孩子列表。</summary>
    [HttpGet("children")]
    public async Task<IActionResult> Children()
    {
        var username = User.Identity?.Name ?? "";
        var bindings = await _db.ParentBindings
            .Where(b => b.ParentUserId == username && b.Status == Core.Entities.ParentBindingStatus.Bound)
            .ToListAsync();

        var result = new List<object>();
        foreach (var b in bindings)
        {
            var stu = await _db.Students.FindAsync(b.StudentId);
            if (stu == null) continue;
            var cls = await _db.Classes.FindAsync(stu.ClassId);
            result.Add(new
            {
                studentId = stu.Id,
                name = stu.Name,
                studentNo = stu.StudentNo,
                className = cls?.Name ?? "",
                bindingId = b.Id,
            });
        }
        return Ok(result);
    }

    /// <summary>解除绑定（保留审计记录）。</summary>
    [HttpPost("unbind/{bindingId}")]
    public async Task<IActionResult> Unbind(string bindingId)
    {
        var binding = await _db.ParentBindings.FindAsync(bindingId);
        if (binding == null) return NotFound();

        var username = User.Identity?.Name ?? "";
        if (binding.ParentUserId != username) return Forbid();

        binding.Status = Core.Entities.ParentBindingStatus.Unbound;
        binding.UnboundAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { message = "已解除绑定" });
    }

    /// <summary>孩子概览：打卡/考勤/积分/值日/成绩。</summary>
    [HttpGet("child/{studentId}/overview")]
    public async Task<IActionResult> ChildOverview(string studentId)
    {
        // 权限校验：只能查看自己绑定的孩子
        var username = User.Identity?.Name ?? "";
        var bound = await _db.ParentBindings.AnyAsync(b =>
            b.StudentId == studentId && b.ParentUserId == username &&
            b.Status == Core.Entities.ParentBindingStatus.Bound);
        if (!bound) return Forbid();

        var since = DateTime.Now.AddDays(-30);
        var checkIns = await _db.CheckInRecords
            .Where(r => r.StudentId == studentId && r.CheckedAt > since)
            .OrderByDescending(r => r.CheckedAt)
            .Take(30)
            .ToListAsync();

        var points = await _db.PointRecords
            .Where(r => r.StudentId == studentId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .ToListAsync();

        return Ok(new
        {
            recentCheckIns = checkIns.Select(r => new { r.CheckedAt, r.Source }),
            attendanceRate = checkIns.Count / 30.0,
            totalPoints = points.Sum(p => p.Delta),
            recentPoints = points.Select(p => new { p.Delta, p.Reason, p.CreatedAt }),
        });
    }

    /// <summary>家长可见的通知列表（含已读状态）。</summary>
    [HttpGet("notices")]
    public async Task<IActionResult> Notices([FromQuery] string studentId)
    {
        var username = User.Identity?.Name ?? "";
        var bound = await _db.ParentBindings.AnyAsync(b =>
            b.StudentId == studentId && b.ParentUserId == username &&
            b.Status == Core.Entities.ParentBindingStatus.Bound);
        if (!bound) return Forbid();

        var stu = await _db.Students.FindAsync(studentId);
        if (stu == null) return NotFound();

        var notices = await _db.Notices
            .Where(n => n.ClassId == stu.ClassId && n.PublishAt <= DateTime.Now)
            .OrderByDescending(n => n.PublishAt)
            .Take(50)
            .ToListAsync();

        var readIds = await _db.NoticeReadReceipts
            .Where(r => r.ParentUserId == username)
            .Select(r => r.NoticeId)
            .ToListAsync();

        return Ok(notices.Select(n => new
        {
            n.Id, n.Title, n.Content, n.PublishAt,
            isRead = readIds.Contains(n.Id),
        }));
    }

    /// <summary>标记通知已读。</summary>
    [HttpPost("notices/{noticeId}/read")]
    public async Task<IActionResult> MarkRead(string noticeId)
    {
        var username = User.Identity?.Name ?? "";
        var exists = await _db.NoticeReadReceipts.AnyAsync(r =>
            r.NoticeId == noticeId && r.ParentUserId == username);
        if (!exists)
        {
            _db.NoticeReadReceipts.Add(new Core.Entities.NoticeReadReceipt
            {
                NoticeId = noticeId,
                ParentUserId = username,
            });
            await _db.SaveChangesAsync();
        }
        return Ok();
    }

    /// <summary>与教师的留言列表。</summary>
    [HttpGet("messages")]
    public async Task<IActionResult> Messages([FromQuery] string studentId)
    {
        var username = User.Identity?.Name ?? "";
        var msgs = await _db.Messages
            .Where(m => m.ParentUserId == username && (m.StudentId == studentId || m.StudentId == null))
            .OrderBy(m => m.CreatedAt)
            .Take(100)
            .ToListAsync();
        return Ok(msgs);
    }

    /// <summary>发送留言给教师。</summary>
    [HttpPost("messages")]
    public async Task<IActionResult> SendMessage([FromBody] ParentMessageRequest req)
    {
        var username = User.Identity?.Name ?? "";
        var msg = new Core.Entities.Message
        {
            ClassId = req.ClassId ?? "",
            StudentId = req.StudentId,
            ParentUserId = username,
            SenderRole = Core.Entities.MessageSenderRole.Parent,
            SenderName = req.SenderName ?? "家长",
            Content = req.Content,
            IsImage = req.IsImage,
        };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();
        return Ok(msg);
    }

    /// <summary>已下发给家长班的资源列表（按孩子班级过滤）。</summary>
    [HttpGet("resources")]
    public async Task<IActionResult> Resources([FromQuery] string studentId)
    {
        var username = User.Identity?.Name ?? "";
        var bound = await _db.ParentBindings.AnyAsync(b =>
            b.StudentId == studentId && b.ParentUserId == username &&
            b.Status == Core.Entities.ParentBindingStatus.Bound);
        if (!bound) return Forbid();

        var stu = await _db.Students.FindAsync(studentId);
        if (stu == null) return NotFound();

        var resources = await _db.Resources
            .Where(r => r.PublishedToParents && (r.ClassId == stu.ClassId || r.ClassId == null))
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .ToListAsync();
        return Ok(resources.Select(r => new
        {
            r.Id, r.Title, r.Kind, r.Subject, r.CreatedAt,
            downloadUrl = $"/api/v4/parent/resources/{r.Id}/file",
        }));
    }

    /// <summary>资源文件下载（家长侧，只允许已下发资源）。</summary>
    [HttpGet("resources/{id}/file")]
    public async Task<IActionResult> DownloadResource(string id)
    {
        var res = await _db.Resources.FindAsync(id);
        if (res == null || !res.PublishedToParents) return NotFound();
        if (res.Kind == Core.Entities.ResourceKind.Link)
            return Redirect(res.Location);

        var paths = HttpContext.RequestServices.GetRequiredService<ServerPaths>();
        var fullPath = Path.Combine(paths.DataDirectory, res.Location);
        if (!System.IO.File.Exists(fullPath)) return NotFound("文件不存在");
        return PhysicalFile(fullPath, "application/octet-stream", res.Title);
    }

    /// <summary>孩子的值日记录（家长端展示值日表现）。</summary>
    [HttpGet("child/{studentId}/duty")]
    public async Task<IActionResult> ChildDuty(string studentId, [FromQuery] int take = 30)
    {
        var username = User.Identity?.Name ?? "";
        var bound = await _db.ParentBindings.AnyAsync(b =>
            b.StudentId == studentId && b.ParentUserId == username &&
            b.Status == Core.Entities.ParentBindingStatus.Bound);
        if (!bound) return Forbid();

        var records = await _db.DutyRecords
            .Where(r => r.StudentId == studentId)
            .OrderByDescending(r => r.Date)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync();

        var postIds = records.Select(r => r.PostId).Distinct().ToList();
        var posts = await _db.DutyPosts
            .Where(p => postIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name);

        var total = records.Count;
        var completed = records.Count(r => r.Completed);
        return Ok(new
        {
            completedCount = completed,
            totalCount = total,
            completionRate = total == 0 ? 0 : Math.Round(completed * 100.0 / total, 1),
            records = records.Select(r => new
            {
                r.Date, r.Completed, r.Note,
                postName = posts.TryGetValue(r.PostId, out var name) ? name : "值日",
            }),
        });
    }

    /// <summary>获取孩子的成绩概览（受隐私开关控制）。</summary>
    [HttpGet("child/{studentId}/scores")]
    public async Task<IActionResult> ChildScores(string studentId)
    {
        var username = User.Identity?.Name ?? "";
        var bound = await _db.ParentBindings.AnyAsync(b =>
            b.StudentId == studentId && b.ParentUserId == username &&
            b.Status == Core.Entities.ParentBindingStatus.Bound);
        if (!bound) return Forbid();

        // 隐私开关：默认关闭成绩推送
        var allowScores = await _db.AppSettings
            .Where(s => s.Key == "parent.showScores")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        if (allowScores != "true")
            return Ok(new { enabled = false, message = "成绩推送未开启" });

        var results = await _db.QuestionResults
            .Where(r => _db.AnswerSheetSubmissions
                .Any(s => s.Id == r.SubmissionId && s.StudentId == studentId && s.Status == Core.Entities.SubmissionStatus.Confirmed))
            .ToListAsync();

        return Ok(new { enabled = true, results });
    }
}

public record BindRequest(string InviteCode, string? Alias);
public record ParentMessageRequest(string? ClassId, string? StudentId, string Content, string? SenderName, bool IsImage);

/// <summary>
/// 教师端：生成家长绑定邀请码。前缀 /api/v4/parent/invite
/// </summary>
[ApiController]
[Route("api/v4/parent/invite")]
[Authorize(Roles = "admin,teacher")]
public class ParentInviteController : ControllerBase
{
    private readonly ServerDbContext _db;
    public ParentInviteController(ServerDbContext db) => _db = db;

    /// <summary>为学生生成绑定邀请码。</summary>
    [HttpPost("{studentId}")]
    public async Task<IActionResult> CreateInvite(string studentId)
    {
        var stu = await _db.Students.FindAsync(studentId);
        if (stu == null) return NotFound(new { error = "学生不存在" });

        // 已有待绑定邀请码则复用
        var existing = await _db.ParentBindings.FirstOrDefaultAsync(b =>
            b.StudentId == studentId && b.Status == Core.Entities.ParentBindingStatus.Pending);
        if (existing != null)
            return Ok(new { inviteCode = existing.InviteCode, studentName = stu.Name });

        var code = GenerateCode();
        _db.ParentBindings.Add(new Core.Entities.ParentBinding
        {
            StudentId = studentId,
            InviteCode = code,
            Status = Core.Entities.ParentBindingStatus.Pending,
        });
        await _db.SaveChangesAsync();
        return Ok(new { inviteCode = code, studentName = stu.Name });
    }

    private static string GenerateCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rng = new Random();
        return new string(Enumerable.Range(0, 6).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
    }
}
