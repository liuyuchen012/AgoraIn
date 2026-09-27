using AgoraIn.Core.Entities;
using AgoraIn.Core.Security;
using AgoraIn.Server.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>积分 API。前缀 /api/v4/points</summary>
[ApiController]
[Route("api/v4/points")]
[Authorize]
[RequirePermission(Permissions.PointsManage)]
public class PointsController : ControllerBase
{
    private readonly ServerDbContext _db;
    public PointsController(ServerDbContext db) => _db = db;

    /// <summary>积分规则列表。</summary>
    [HttpGet("rules")]
    public async Task<IActionResult> Rules([FromQuery] string? classId)
    {
        var q = _db.PointRules.Where(r => r.Enabled);
        if (!string.IsNullOrEmpty(classId))
            q = q.Where(r => r.ClassId == null || r.ClassId == classId);
        return Ok(await q.OrderByDescending(r => r.Pinned).ToListAsync());
    }

    /// <summary>创建积分规则。</summary>
    [HttpPost("rules")]
    public async Task<IActionResult> CreateRule([FromBody] PointRule rule)
    {
        _db.PointRules.Add(rule);
        await _db.SaveChangesAsync();
        return Created("", rule);
    }

    /// <summary>积分流水（可按学生筛选）。</summary>
    [HttpGet("records")]
    public async Task<IActionResult> Records([FromQuery] string? studentId, [FromQuery] int take = 100)
    {
        var q = _db.PointRecords.AsQueryable();
        if (!string.IsNullOrEmpty(studentId)) q = q.Where(r => r.StudentId == studentId);
        return Ok(await q.OrderByDescending(r => r.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync());
    }

    /// <summary>提交积分流水（教师端/桌面端同步，幂等按 SourceId 去重）。</summary>
    [HttpPost("records")]
    public async Task<IActionResult> SubmitRecords([FromBody] List<PointRecord> records)
    {
        var added = 0;
        foreach (var r in records)
        {
            if (!string.IsNullOrEmpty(r.SourceId))
            {
                var exists = await _db.PointRecords.AnyAsync(x =>
                    x.SourceId == r.SourceId && x.StudentId == r.StudentId);
                if (exists) continue;
            }
            _db.PointRecords.Add(r);
            added++;
        }
        await _db.SaveChangesAsync();
        return Ok(new { added, total = records.Count });
    }

    /// <summary>积分榜（按总分降序）。</summary>
    [HttpGet("ranking")]
    public async Task<IActionResult> Ranking([FromQuery] string? classId, [FromQuery] int take = 20)
    {
        var q = _db.PointRecords.AsQueryable();
        var grouped = await q
            .GroupBy(r => r.StudentId)
            .Select(g => new { StudentId = g.Key, Total = g.Sum(x => x.Delta) })
            .OrderByDescending(x => x.Total)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync();

        var result = new List<object>();
        var rank = 1;
        foreach (var item in grouped)
        {
            var stu = await _db.Students.FindAsync(item.StudentId);
            result.Add(new { rank = rank++, studentId = item.StudentId, name = stu?.Name ?? "", total = item.Total });
        }
        return Ok(result);
    }
}

/// <summary>值日 API。前缀 /api/v4/duty</summary>
[ApiController]
[Route("api/v4/duty")]
[Authorize]
[RequirePermission(Permissions.DutyManage)]
public class DutyController : ControllerBase
{
    private readonly ServerDbContext _db;
    public DutyController(ServerDbContext db) => _db = db;

    /// <summary>值日岗位列表。</summary>
    [HttpGet("posts")]
    public async Task<IActionResult> Posts([FromQuery] string? classId)
    {
        var q = _db.DutyPosts.AsQueryable();
        if (!string.IsNullOrEmpty(classId)) q = q.Where(p => p.ClassId == classId);
        return Ok(await q.OrderBy(p => p.SortOrder).ToListAsync());
    }

    [HttpPost("posts")]
    public async Task<IActionResult> CreatePost([FromBody] DutyPost post)
    {
        _db.DutyPosts.Add(post);
        await _db.SaveChangesAsync();
        return Created("", post);
    }

    /// <summary>值日完成记录。</summary>
    [HttpGet("records")]
    public async Task<IActionResult> Records([FromQuery] string? classId, [FromQuery] string? date)
    {
        var q = _db.DutyRecords.AsQueryable();
        if (!string.IsNullOrEmpty(classId)) q = q.Where(r => r.ClassId == classId);
        if (DateOnly.TryParse(date, out var d)) q = q.Where(r => r.Date == d);
        return Ok(await q.OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync());
    }

    /// <summary>标记值日完成/未完成。</summary>
    [HttpPost("records")]
    public async Task<IActionResult> SubmitRecord([FromBody] DutyRecord record)
    {
        _db.DutyRecords.Add(record);
        await _db.SaveChangesAsync();
        return Created("", record);
    }
}

/// <summary>通知公告 API。前缀 /api/v4/notices</summary>
[ApiController]
[Route("api/v4/notices")]
[Authorize]
[RequirePermission(Permissions.NoticesManage)]
public class NoticesController : ControllerBase
{
    private readonly ServerDbContext _db;
    public NoticesController(ServerDbContext db) => _db = db;

    /// <summary>通知列表（教师端可见全部含未发布）。</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? classId)
    {
        var q = _db.Notices.AsQueryable();
        if (!string.IsNullOrEmpty(classId)) q = q.Where(n => n.ClassId == classId);
        return Ok(await q.OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync());
    }

    /// <summary>发布通知（支持定时发布：PublishAt 设为未来时刻）。</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Notice notice)
    {
        var username = User.Identity?.Name ?? "";
        notice.PublishedBy = username;
        _db.Notices.Add(notice);
        await _db.SaveChangesAsync();
        return Created("", notice);
    }

    /// <summary>未读名单（已绑定家长 - 已读回执）。</summary>
    [HttpGet("{noticeId}/unread")]
    public async Task<IActionResult> Unread(string noticeId)
    {
        var notice = await _db.Notices.FindAsync(noticeId);
        if (notice == null) return NotFound();

        // 该班级所有学生的已绑定家长
        var boundParents = await _db.ParentBindings
            .Where(b => b.Status == ParentBindingStatus.Bound)
            .Join(_db.Students.Where(s => s.ClassId == notice.ClassId),
                b => b.StudentId, s => s.Id,
                (b, s) => new { b.ParentUserId, s.Name })
            .ToListAsync();

        var readIds = await _db.NoticeReadReceipts
            .Where(r => r.NoticeId == noticeId)
            .Select(r => r.ParentUserId)
            .ToListAsync();

        var unread = boundParents
            .Where(p => p.ParentUserId != null && !readIds.Contains(p.ParentUserId))
            .Select(p => p.Name)
            .Distinct()
            .ToList();

        return Ok(new { unreadCount = unread.Count, students = unread });
    }
}

/// <summary>资源库 API。前缀 /api/v4/resources</summary>
[ApiController]
[Route("api/v4/resources")]
[Authorize]
[RequirePermission(Permissions.ResourcesManage)]
public class ResourcesController : ControllerBase
{
    private readonly ServerDbContext _db;
    private readonly ServerPaths _paths;

    public ResourcesController(ServerDbContext db, ServerPaths paths)
    {
        _db = db;
        _paths = paths;
    }

    /// <summary>资源列表（按班级/科目/标签筛选）。</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? classId, [FromQuery] string? subject)
    {
        var q = _db.Resources.AsQueryable();
        if (!string.IsNullOrEmpty(classId)) q = q.Where(r => r.ClassId == classId || r.ClassId == null);
        if (!string.IsNullOrEmpty(subject)) q = q.Where(r => r.Subject == subject);
        return Ok(await q.OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync());
    }

    /// <summary>上传资源文件。</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] string? title,
        [FromForm] string? classId,
        [FromForm] string? subject)
    {
        if (file == null || file.Length == 0) return BadRequest("请选择文件");

        // 本地磁盘存储（IFileStorage 抽象的默认实现）
        var dir = _paths.ResourceDirectory;
        Directory.CreateDirectory(dir);

        var safeName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var fullPath = Path.Combine(dir, safeName);
        await using (var fs = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(fs);
        }

        var resource = new Resource
        {
            Title = string.IsNullOrEmpty(title) ? file.FileName : title,
            Kind = GuessKind(file.ContentType, file.FileName),
            Location = $"resources/{safeName}",
            ClassId = classId,
            Subject = subject,
            UploadedBy = User.Identity?.Name ?? "",
        };

        _db.Resources.Add(resource);
        await _db.SaveChangesAsync();
        return Created("", resource);
    }

    /// <summary>下发资源给家长端。</summary>
    [HttpPost("{id}/publish")]
    public async Task<IActionResult> Publish(string id)
    {
        var res = await _db.Resources.FindAsync(id);
        if (res == null) return NotFound();
        res.PublishedToParents = true;
        await _db.SaveChangesAsync();
        return Ok(res);
    }

    /// <summary>下载/预览资源文件。</summary>
    [HttpGet("{id}/file")]
    public async Task<IActionResult> Download(string id)
    {
        var res = await _db.Resources.FindAsync(id);
        if (res == null) return NotFound();

        if (res.Kind == ResourceKind.Link)
            return Redirect(res.Location);

        var fullPath = Path.Combine(_paths.DataDirectory, res.Location);
        if (!System.IO.File.Exists(fullPath)) return NotFound("文件不存在");

        return PhysicalFile(fullPath, "application/octet-stream", res.Title);
    }

    private static ResourceKind GuessKind(string? contentType, string fileName)
    {
        var ct = contentType ?? "";
        if (ct.StartsWith("image/")) return ResourceKind.Image;
        if (ct.StartsWith("video/")) return ResourceKind.Video;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => ResourceKind.Image,
            ".mp4" or ".mov" or ".avi" or ".mkv" => ResourceKind.Video,
            _ => ResourceKind.File,
        };
    }
}

/// <summary>师生留言 API（教师端）。前缀 /api/v4/messages</summary>
[ApiController]
[Route("api/v4/messages")]
[Authorize]
[RequirePermission(Permissions.MessagesHandle)]
public class MessagesController : ControllerBase
{
    private readonly ServerDbContext _db;
    public MessagesController(ServerDbContext db) => _db = db;

    /// <summary>会话列表（按家长分组，取最新一条）。</summary>
    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations()
    {
        var all = await _db.Messages
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();

        var conversations = all
            .GroupBy(m => m.ParentUserId)
            .Select(g => new
            {
                parentUserId = g.Key,
                studentId = g.First().StudentId,
                className = g.First().ClassId,
                lastMessage = g.First().Content,
                lastTime = g.First().CreatedAt,
                messageCount = g.Count(),
                hasFlagged = g.Any(x => x.Flagged),
            })
            .ToList();

        return Ok(conversations);
    }

    /// <summary>与某位家长的完整会话。</summary>
    [HttpGet("conversation/{parentUserId}")]
    public async Task<IActionResult> Conversation(string parentUserId, [FromQuery] string? studentId)
    {
        var q = _db.Messages.Where(m => m.ParentUserId == parentUserId);
        if (!string.IsNullOrEmpty(studentId)) q = q.Where(m => m.StudentId == studentId);
        return Ok(await q.OrderBy(m => m.CreatedAt).Take(200).ToListAsync());
    }

    /// <summary>教师回复家长。</summary>
    [HttpPost("reply")]
    public async Task<IActionResult> Reply([FromBody] TeacherReplyRequest req)
    {
        var msg = new Message
        {
            ClassId = req.ClassId ?? "",
            StudentId = req.StudentId,
            ParentUserId = req.ParentUserId,
            SenderRole = MessageSenderRole.Teacher,
            SenderName = User.Identity?.Name ?? "老师",
            Content = req.Content,
            IsImage = req.IsImage,
            Flagged = ContainsSensitive(req.Content),
        };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();
        return Created("", msg);
    }

    /// <summary>敏感词简单过滤（命中即标记待审核）。</summary>
    private static bool ContainsSensitive(string content)
    {
        // 生产环境应读取可配置词库；此处保留最小实现
        string[] words = ["身份证", "银行卡", "转账", "红包"];
        return words.Any(w => content.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
}

public record TeacherReplyRequest(string? ClassId, string? StudentId, string ParentUserId, string Content, bool IsImage);
