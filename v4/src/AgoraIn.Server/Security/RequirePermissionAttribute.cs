using System.Security.Claims;
using AgoraIn.Core.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgoraIn.Server.Security;

/// <summary>
/// 细粒度权限校验：按 <see cref="RolePermissions"/> 矩阵判断当前用户角色是否具备指定权限，
/// 不具备则返回 403。用法：<c>[RequirePermission(Permissions.UsersManage)]</c>。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _permissions;

    public RequirePermissionAttribute(params string[] permissions) => _permissions = permissions;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var role = context.HttpContext.User.FindFirst(ClaimTypes.Role)?.Value
                   ?? context.HttpContext.User.FindFirst("role")?.Value;

        if (role is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (!RolePermissions.HasAll(role, _permissions))
        {
            var readable = string.Join(", ", _permissions);
            context.Result = new ObjectResult(new
            {
                error = $"当前角色（{AppRoles.DisplayName(role)}）无权执行此操作，需要权限：{readable}",
            })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
    }
}
