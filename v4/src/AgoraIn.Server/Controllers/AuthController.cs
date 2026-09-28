using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AgoraIn.Core.Security;
using AgoraIn.Server.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 认证控制器：登录、首次初始化、修改密码。API 前缀 /api/v4/auth
/// </summary>
[ApiController]
[Route("api/v4/auth")]
public class AuthController : ControllerBase
{
    private readonly ServerDbContext _db;
    private readonly IConfiguration _config;

    public AuthController(ServerDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    /// <summary>登录，返回 JWT Token 与该角色的权限点（前端据此控制按钮显隐）。</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == req.Username, ct);
        if (user == null || !VerifyPassword(user, req.Password))
            return Unauthorized(new { error = "用户名或密码错误" });

        if (!user.IsActive)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "该账户已被禁用，请联系管理员" });

        user.LastLoginAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        var role = AppRoles.Normalize(user.Role);
        var token = GenerateToken(user.Username, role);

        return Ok(new
        {
            token,
            username = user.Username,
            role,
            roleName = AppRoles.DisplayName(role),
            displayName = user.DisplayName,
            email = user.Email,
            isSubAccount = user.OwnerUserId != null,
            permissions = RolePermissions.For(role),
        });
    }

    /// <summary>初始化向导：首次运行创建系统管理员账户。</summary>
    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] SetupRequest req, CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(ct))
            return BadRequest(new { error = "管理员已存在，无法重复初始化" });

        if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Trim().Length < 3)
            return BadRequest(new { error = "用户名至少 3 个字符" });

        if (string.IsNullOrEmpty(req.Password) || req.Password.Length < 6)
            return BadRequest(new { error = "密码至少 6 个字符" });

        var user = new User
        {
            Username = req.Username.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = AppRoles.Admin,
            DisplayName = "系统管理员",
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "管理员创建成功" });
    }

    /// <summary>修改密码（本人）。</summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user == null) return NotFound();

        if (!VerifyPassword(user, req.OldPassword))
            return BadRequest(new { error = "原密码错误" });

        if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < 6)
            return BadRequest(new { error = "新密码至少 6 个字符" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "密码修改成功" });
    }

    /// <summary>当前登录用户的资料与权限（前端刷新页面后恢复菜单可见性）。</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user == null) return NotFound();

        var role = AppRoles.Normalize(user.Role);
        return Ok(new
        {
            username = user.Username,
            role,
            roleName = AppRoles.DisplayName(role),
            displayName = user.DisplayName,
            email = user.Email,
            isSubAccount = user.OwnerUserId != null,
            permissions = RolePermissions.For(role),
        });
    }

    // ── 密码校验与哈希升级 ──

    /// <summary>
    /// 校验密码。兼容从 v3.2 迁移过来的旧哈希（<c>salt:sha256(salt+password)</c> 或纯 SHA256），
    /// 校验通过后自动升级为 BCrypt 并回写。
    /// </summary>
    private static bool VerifyPassword(User user, string password)
    {
        var hash = user.PasswordHash;

        if (hash.StartsWith("$2", StringComparison.Ordinal))
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ── v3.2 兼容路径 ──
        var legacyOk = hash.Contains(':') ? VerifyLegacySalted(hash, password) : VerifyLegacyPlain(hash, password);
        if (legacyOk)
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        }

        return legacyOk;
    }

    /// <summary>v3.2：<c>salt(16字节hex):sha256(salt + password)</c>。</summary>
    private static bool VerifyLegacySalted(string stored, string password)
    {
        var parts = stored.Split(':', 2);
        if (parts.Length != 2) return false;

        var salt = Encoding.UTF8.GetBytes(parts[0]);
        var expected = parts[1];
        var actual = Convert.ToHexString(SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(password)]));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actual.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(expected.ToLowerInvariant()));
    }

    /// <summary>更早版本：无盐 sha256。</summary>
    private static bool VerifyLegacyPlain(string stored, string password)
    {
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actual),
            Encoding.ASCII.GetBytes(stored.ToLowerInvariant()));
    }

    private string GenerateToken(string username, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _config["Jwt:Key"] ?? "AgoraIn-v4-default-key-change-in-production!"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // 注意：不能用 new JwtSecurityToken(...).ToString() —— 新版 Microsoft.IdentityModel 中
        // SecurityToken.ToString() 返回的是**调试用 JSON 表示**（claim 名含 URI 时会出现多个点号），
        // 客户端拿它当 Bearer 令牌会被判为非法格式：
        //   IDX14122: JWT is not a well formed JWE, there are more than four dots
        // 必须用 JsonWebTokenHandler.CreateToken 才能得到紧凑序列化（header.payload.signature）的合法 JWS。
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, username),
                // SignalR 的 DefaultUserIdProvider 按 NameIdentifier 解析 Clients.User(...)，
                // 缺该 claim 时定向通知永远匹配不到连接（本系统以用户名作为用户标识）
                new Claim(ClaimTypes.NameIdentifier, username),
                new Claim(ClaimTypes.Role, role),
            ]),
            Expires = DateTime.Now.AddHours(24),
            SigningCredentials = creds,
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}

public record LoginRequest(string Username, string Password);
public record SetupRequest(string Username, string Password);
public record ChangePasswordRequest(string OldPassword, string NewPassword);
