using System.Security.Claims;
using AgoraIn.Core.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgoraIn.Server.Security;

/// <summary>
/// 细粒度权限校验。未认证用户跳过（由 AllowAnonymous 端点使用）。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _permissions;
    public RequirePermissionAttribute(params string[] permissions) => _permissions = permissions;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // 未认证用户跳过（AllowAnonymous 端点不会有 JWT）
        if (!context.HttpContext.User.Identity?.IsAuthenticated ?? true)
            return;

        var role = context.HttpContext.User.FindFirst(ClaimTypes.Role)?.Value
                   ?? context.HttpContext.User.FindFirst("role")?.Value;
        if (role is null) { context.Result = new UnauthorizedResult(); return; }

        if (!RolePermissions.HasAll(role, _permissions))
        {
            context.Result = new ObjectResult(new { error = $"权限不足，需要：{string.Join(", ", _permissions)}" })
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
