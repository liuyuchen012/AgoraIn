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

    /// <summary>
    /// 仪表盘概览统计。
    /// 教学数据（班级/学生/打卡/通知/资源）经全局查询过滤器自动按请求区域隔离——
    /// 租户看到的是本区域统计，主区域看到全服务器统计。
    /// 设备与平台用户为全局数据，仅主区域可见；租户额外返回本区域授权状态。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var isManager = AgoraIn.Server.Security.RegionContext.IsManager;

        var classCount = await _db.Classes.CountAsync(ct);
        var studentCount = await _db.Students.CountAsync(ct);
        var todayCheckins = await _db.CheckInRecords
            .CountAsync(r => r.CheckedAt.Date == DateTime.Today, ct);
        var noticeCount = await _db.Notices.CountAsync(ct);
        var resourceCount = await _db.Resources.CountAsync(ct);

        // 设备与平台用户数为全局数据，仅主区域可见
        var deviceCount = 0;
        var onlineDevices = 0;
        var userCount = 0;
        var devices = new List<object>();

        if (isManager)
        {
            deviceCount = await _db.Devices.CountAsync(ct);
            onlineDevices = await _db.Devices
                .CountAsync(d => d.LastSeen > DateTime.Now.AddMinutes(-5), ct);
            userCount = await _db.Users.CountAsync(u => u.IsActive, ct);
            devices = (await _db.Devices
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
                .ToListAsync(ct)).Cast<object>().ToList();
        }

        // 最近打卡记录（区域隔离）
        var recentCheckins = await _db.CheckInRecords
            .OrderByDescending(r => r.CheckedAt)
            .Take(10)
            .ToListAsync(ct);

        // 租户：附带本区域授权状态
        object? region = null;
        if (!isManager)
        {
            var regionId = AgoraIn.Server.Security.RegionContext.Current;
            var r = await _db.Regions.FirstOrDefaultAsync(x => x.RegionId == regionId, ct);
            if (r != null)
            {
                region = new
                {
                    regionId = r.RegionId,
                    name = r.Name,
                    activated = r.Activated,
                    isActive = r.IsActive,
                    expireAt = r.ExpireAt,
                    maxDevices = r.MaxDevices,
                    remainingDays = r.ExpireAt is { } exp ? (int)Math.Ceiling((exp - DateTime.Now).TotalDays) : 0,
                };
            }
        }

        return Ok(new
        {
            isManager,
            region,
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
