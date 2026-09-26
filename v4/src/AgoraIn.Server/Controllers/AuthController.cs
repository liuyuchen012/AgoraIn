using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 认证控制器：登录、Token 签发、用户管理。
/// API 前缀 /api/v4/auth
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

    /// <summary>登录，返回 JWT Token。</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { error = "用户名或密码错误" });

        var token = GenerateToken(user);
        return Ok(new { token, role = user.Role, username = user.Username });
    }

    /// <summary>初始化向导：首次运行创建管理员账户。</summary>
    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] SetupRequest req)
    {
        if (await _db.Users.AnyAsync())
            return BadRequest(new { error = "管理员已存在，无法重复初始化" });

        var user = new User
        {
            Username = req.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = "admin",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return Ok(new { message = "管理员创建成功" });
    }

    /// <summary>修改密码。</summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(req.OldPassword, user.PasswordHash))
            return BadRequest(new { error = "原密码错误" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await _db.SaveChangesAsync();
        return Ok(new { message = "密码修改成功" });
    }

    private string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _config["Jwt:Key"] ?? "AgoraIn-v4-default-key-change-in-production!"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
        };

        return new JwtSecurityToken(
            expires: DateTime.Now.AddHours(24),
            signingCredentials: creds,
            claims: claims).ToString();
    }
}

public record LoginRequest(string Username, string Password);
public record SetupRequest(string Username, string Password);
public record ChangePasswordRequest(string OldPassword, string NewPassword);
