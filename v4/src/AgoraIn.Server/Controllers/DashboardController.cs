using AgoraIn.Core.Security;
using AgoraIn.Server.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 仪表盘统计 API。前缀 /api/v4/dashboard
/// 为 Web 管理面板首页提供聚合数据。
/// </summary>
[ApiController]
[Route("api/v4/dashboard")]
[Authorize]
[RequirePermission(Permissions.ClassesView)]
public class DashboardController : ControllerBase
{
    private readonly ServerDbContext _db;
    public DashboardController(ServerDbContext db) => _db = db;

    /// <summary>仪表盘概览统计。</summary>
    [HttpGet]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var classCount = await _db.Classes.CountAsync(ct);
        var studentCount = await _db.Students.CountAsync(ct);
        var deviceCount = await _db.Devices.CountAsync(ct);
        var onlineDevices = await _db.Devices
            .CountAsync(d => d.LastSeen > DateTime.Now.AddMinutes(-5), ct);
        var userCount = await _db.Users.CountAsync(u => u.IsActive, ct);
        var todayCheckins = await _db.CheckInRecords
            .CountAsync(r => r.CheckedAt.Date == DateTime.Today, ct);
        var noticeCount = await _db.Notices.CountAsync(ct);
        var resourceCount = await _db.Resources.CountAsync(ct);

        // 最近打卡记录
        var recentCheckins = await _db.CheckInRecords
            .OrderByDescending(r => r.CheckedAt)
            .Take(10)
            .ToListAsync(ct);

        // 设备状态
        var devices = await _db.Devices
            .OrderByDescending(d => d.LastSeen)
            .Take(10)
            .Select(d => new
            {
                d.Id,
                d.DeviceName,
                d.DeviceUuid,
                d.LastSeen,
                isOnline = d.LastSeen > DateTime.Now.AddMinutes(-5),
            })
            .ToListAsync(ct);

        return Ok(new
        {
            stats = new
            {
                classCount,
                studentCount,
                deviceCount,
                onlineDevices,
                userCount,
                todayCheckins,
                noticeCount,
                resourceCount,
            },
            recentCheckins,
            devices,
        });
    }
}
