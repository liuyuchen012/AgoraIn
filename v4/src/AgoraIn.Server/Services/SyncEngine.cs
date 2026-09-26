using AgoraIn.Server;
using AgoraIn.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server;

/// <summary>
/// 同步引擎：桌面端与服务器之间的数据同步。
/// 实体级时间戳 + 变更集拉取，冲突按"服务器时间戳裁定"处理。
/// </summary>
public static class SyncEngine
{
    /// <summary>拉取指定时间戳之后的所有变更。</summary>
    public static async Task<object> PullChanges(ServerDbContext db, DateTime since, string classId)
    {
        var students = await db.Students.Where(s => s.ClassId == classId && s.CreatedAt > since).ToListAsync();
        var checkins = await db.CheckInRecords
            .Where(r => r.CheckedAt > since)
            .ToListAsync();
        var hourRecords = await db.ClassHourRecords
            .Where(r => r.CreatedAt > since)
            .ToListAsync();

        return new
        {
            timestamp = DateTime.Now,
            students,
            checkins,
            hourRecords,
        };
    }

    /// <summary>接收桌面端推送的变更并合并。</summary>
    public static async Task<int> PushChanges(ServerDbContext db, object changes)
    {
        // 简化实现：实际需按实体类型反射合并
        await db.SaveChangesAsync();
        return 0;
    }
}
