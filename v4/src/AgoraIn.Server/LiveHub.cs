using Microsoft.AspNetCore.SignalR;

namespace AgoraIn.Server;

/// <summary>
/// SignalR 实时推送 Hub：打卡更新、点名落点、积分变动、通知。
/// 路径 /hub/live，分组按班级。
/// </summary>
public sealed class LiveHub : Hub
{
    /// <summary>桌面端连接时加入班级分组。</summary>
    public async Task JoinClass(string classId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, classId);
    }

    /// <summary>广播打卡更新到同一班级的所有客户端。</summary>
    public async Task BroadcastCheckIn(string classId, object data)
    {
        await Clients.Group(classId).SendAsync("CheckInUpdate", data);
    }

    /// <summary>广播点名落点。</summary>
    public async Task BroadcastRollCall(string classId, object data)
    {
        await Clients.Group(classId).SendAsync("RollCallUpdate", data);
    }

    /// <summary>广播积分变动。</summary>
    public async Task BroadcastPoints(string classId, object data)
    {
        await Clients.Group(classId).SendAsync("PointsUpdate", data);
    }

    /// <summary>广播通知。</summary>
    public async Task SendNotification(string userId, object data)
    {
        await Clients.User(userId).SendAsync("Notification", data);
    }
}
