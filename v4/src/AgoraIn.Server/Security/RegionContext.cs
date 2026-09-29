using System.Security.Claims;

namespace AgoraIn.Server.Security;

/// <summary>
/// 当前请求的区域上下文（AsyncLocal，随异步流传递）。
/// - 主区域（房主）固定标识 <see cref="ManagerRegion"/>，由 <c>用户名@manager</c> 登录；
/// - 子区域（租户）在登录时由 JWT 的 region claim 写入；
/// - 数据隔离依赖 ServerDbContext 的全局查询过滤器读取 <see cref="Current"/>。
/// </summary>
public static class RegionContext
{
    /// <summary>主区域（房主）的区域标识。</summary>
    public const string ManagerRegion = "manager";

    private static readonly AsyncLocal<string?> CurrentSetter = new();

    /// <summary>当前请求的区域标识（未设置时回落主区域）。</summary>
    public static string Current => CurrentSetter.Value is { Length: > 0 } r ? r : ManagerRegion;

    /// <summary>当前请求是否属于主区域。</summary>
    public static bool IsManager => Current == ManagerRegion;

    /// <summary>设置当前区域（中间件从 JWT region claim 调用；匿名请求回落主区域）。</summary>
    public static void Set(string? region)
        => CurrentSetter.Value = string.IsNullOrWhiteSpace(region) ? ManagerRegion : region.Trim();

    /// <summary>从已认证用户解析区域 claim（登录时签发）。</summary>
    public static string? FromUser(ClaimsPrincipal user)
        => user.FindFirst("region")?.Value;
}
