using System.Security.Claims;
using AgoraIn.Core.Security;
using AgoraIn.Server.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 用户与子账户管理 API。前缀 /api/v4/users
///
/// 分级权限：
///  · <b>系统管理员（admin）</b>：管理全部账户、可指定任意角色
///  · <b>机构管理员（owner）</b>：只能创建/管理**自己名下的子账户**，且角色限于 teacher/parent/student
///  · 教师/家长/学生：无用户管理权限
///
/// 越权访问返回 403；owner 尝试操作他人账户同样 403（不是 404，便于排查）。
/// </summary>
[ApiController]
[Route("api/v4/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly ServerDbContext _db;

    public UsersController(ServerDbContext db) => _db = db;

    // ══════════════ 查询 ══════════════

    /// <summary>用户列表（管理员看全部；机构管理员看自己 + 自己的子账户）。</summary>
    [HttpGet]
    [RequirePermission(Permissions.UsersView)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (me, role) = await CurrentUserAsync(ct);
        if (me == null) return Unauthorized();

        var query = _db.Users.AsNoTracking().AsQueryable();

        if (role != AppRoles.Admin)
        {
            // 机构管理员：自己 + 归属自己的子账户
            query = query.Where(u => u.Id == me.Id || u.OwnerUserId == me.Id);
        }

        var users = await query.OrderBy(u => u.Id).ToListAsync(ct);
        return Ok(users.Select(ToDto));
    }

    /// <summary>角色与权限矩阵（供前端渲染权限说明与下拉选项）。</summary>
    [HttpGet("roles")]
    [RequirePermission(Permissions.UsersView)]
    public IActionResult Roles()
    {
        var (_, role) = CurrentUserAsync(CancellationToken.None).GetAwaiter().GetResult();
        var creatable = role == AppRoles.Admin
            ? AppRoles.All
            : AppRoles.SubAccountRoles;

        return Ok(new
        {
            all = AppRoles.All.Select(r => new
            {
                role = r,
                name = AppRoles.DisplayName(r),
                permissions = RolePermissions.For(r),
            }),
            creatable,
        });
    }

    // ══════════════ 创建 ══════════════

    /// <summary>创建用户 / 子账户。</summary>
    [HttpPost]
    [RequirePermission(Permissions.SubAccountsManage)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req, CancellationToken ct)
    {
        var (me, myRole) = await CurrentUserAsync(ct);
        if (me == null) return Unauthorized();

        var username = req.Username?.Trim() ?? "";
        if (username.Length < 3 || username.Length > 32)
            return BadRequest(new { error = "用户名长度需为 3–32 个字符。" });

        if (string.IsNullOrEmpty(req.Password) || req.Password.Length < 6)
            return BadRequest(new { error = "密码至少 6 个字符。" });

        var targetRole = AppRoles.Normalize(req.Role);
        if (!AppRoles.IsValid(targetRole))
            return BadRequest(new { error = $"无效的角色：{req.Role}" });

        // 分级约束：非系统管理员只能创建教师/家长/学生
        if (myRole != AppRoles.Admin && !AppRoles.SubAccountRoles.Contains(targetRole))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "只有系统管理员可以创建管理员类账户；您只能创建教师、学生或家长账户。" });
        }

        // 多区域：用户名在区域内唯一；新账户归属创建者所在区域
        var myRegion = me.RegionIdOrManager;
        if (await _db.Users.AnyAsync(u => u.Username == username && u.RegionId == myRegion, ct))
            return Conflict(new { error = "该用户名在当前区域已存在。" });

        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = targetRole,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? username : req.DisplayName!.Trim(),
            Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email!.Trim().ToLowerInvariant(),
            // 系统管理员创建的账户不归属任何人；机构管理员创建的挂在自己名下
            OwnerUserId = myRole == AppRoles.Admin ? null : me.Id,
            // User 不参与影子属性隔离，必须显式落区域
            RegionId = myRegion,
            IsActive = true,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return Created("", ToDto(user));
    }

    // ══════════════ 更新 ══════════════

    /// <summary>更新用户（显示名 / 邮箱 / 角色 / 启用状态）。</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(Permissions.SubAccountsManage)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest req, CancellationToken ct)
    {
        var (me, myRole) = await CurrentUserAsync(ct);
        if (me == null) return Unauthorized();

        var user = await _db.Users.FindAsync([id], ct);
        if (user == null) return NotFound();

        if (!CanManage(me, myRole, user, out var denial)) return denial!;

        if (req.DisplayName != null) user.DisplayName = req.DisplayName.Trim();
        if (req.Email != null) user.Email = req.Email.Trim().ToLowerInvariant();
        if (req.IsActive is { } active) user.IsActive = active;

        if (!string.IsNullOrWhiteSpace(req.Role))
        {
            var newRole = AppRoles.Normalize(req.Role);
            if (!AppRoles.IsValid(newRole)) return BadRequest(new { error = $"无效的角色：{req.Role}" });

            if (myRole != AppRoles.Admin && !AppRoles.SubAccountRoles.Contains(newRole))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "只有系统管理员可以授予管理员类角色。" });

            // 不允许降级最后一个系统管理员
            if (user.Role == AppRoles.Admin && newRole != AppRoles.Admin)
            {
                var adminCount = await _db.Users.CountAsync(u => u.Role == AppRoles.Admin && u.IsActive, ct);
                if (adminCount <= 1) return BadRequest(new { error = "不能降级唯一的管理员账户。" });
            }

            user.Role = newRole;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(user));
    }

    /// <summary>重置指定账户的密码（管理员 / 机构管理员对自己的子账户）。</summary>
    [HttpPost("{id:int}/reset-password")]
    [RequirePermission(Permissions.SubAccountsManage)]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] AdminResetPasswordRequest req, CancellationToken ct)
    {
        var (me, myRole) = await CurrentUserAsync(ct);
        if (me == null) return Unauthorized();

        var user = await _db.Users.FindAsync([id], ct);
        if (user == null) return NotFound();

        if (!CanManage(me, myRole, user, out var denial)) return denial!;

        if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < 6)
            return BadRequest(new { error = "新密码至少 6 个字符。" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"已重置 {user.Username} 的密码。" });
    }

    // ══════════════ 删除 ══════════════

    /// <summary>删除用户 / 子账户。</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.SubAccountsManage)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var (me, myRole) = await CurrentUserAsync(ct);
        if (me == null) return Unauthorized();

        if (me.Id == id) return BadRequest(new { error = "不能删除当前登录的账户。" });

        var user = await _db.Users.FindAsync([id], ct);
        if (user == null) return NotFound();

        if (!CanManage(me, myRole, user, out var denial)) return denial!;

        if (user.Role == AppRoles.Admin)
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == AppRoles.Admin, ct);
            if (adminCount <= 1) return BadRequest(new { error = "不能删除唯一的管理员账户。" });
        }

        _db.Users.Remove(user);

        // 级联解除子账户归属（被删除者的子账户变为无主，由管理员接管）
        var children = await _db.Users.Where(u => u.OwnerUserId == user.Id).ToListAsync(ct);
        foreach (var child in children) child.OwnerUserId = null;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ══════════════ 内部 ══════════════

    private async Task<(User? Me, string Role)> CurrentUserAsync(CancellationToken ct)
    {
        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username)) return (null, AppRoles.Teacher);

        // 多区域：按「用户名 + 区域」定位当前用户
        var regionId = RegionContext.FromUser(User) ?? RegionContext.ManagerRegion;
        var me = await _db.Users.FirstOrDefaultAsync(
            u => u.Username == username && (u.RegionId == regionId || (u.RegionId == null && regionId == RegionContext.ManagerRegion)), ct);
        return (me, AppRoles.Normalize(me?.Role));
    }

    /// <summary>判断当前用户是否有权管理目标账户。无权时返回 false 并给出 403 结果。</summary>
    private static bool CanManage(User me, string myRole, User target, out IActionResult? denial)
    {
        denial = null;

        if (myRole == AppRoles.Admin) return true;

        // 机构管理员：只能管理自己名下的子账户
        if (myRole == AppRoles.Owner && target.OwnerUserId == me.Id) return true;

        denial = new ObjectResult(new { error = "无权管理该账户（只能管理自己创建的子账户）。" })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
        return false;
    }

    private static object ToDto(User u) => new
    {
        id = u.Id,
        username = u.Username,
        role = AppRoles.Normalize(u.Role),
        roleName = AppRoles.DisplayName(u.Role),
        displayName = u.DisplayName,
        email = u.Email,
        isActive = u.IsActive,
        isSubAccount = u.OwnerUserId != null,
        ownerUserId = u.OwnerUserId,
        lastLoginAt = u.LastLoginAt,
        createdAt = u.CreatedAt,
    };
}

// ══════════════ 请求模型 ══════════════

public record CreateUserRequest(
    string? Username, string? Password, string? Role, string? DisplayName, string? Email);

public record UpdateUserRequest(string? DisplayName, string? Email, string? Role, bool? IsActive);

public record AdminResetPasswordRequest(string? NewPassword);
