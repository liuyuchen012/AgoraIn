using System.Security.Cryptography;
using AgoraIn.Core.Security;
using AgoraIn.Server.Models;
using AgoraIn.Server.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 账户自助服务：邮箱验证码、注册、重置密码。前缀 /api/v4/account
///
/// 相比 v3.2 的加固：
///  · 发送验证码有限流（邮箱冷却 60s / 日配额 5 次 / IP 小时配额 20 次）
///  · 验证码以 BCrypt 哈希存储、校验失败计次（5 次即失效），防暴力枚举
///  · 用途白名单（register / reset），发信失败时回滚验证码记录
///  · 提供过期验证码清理（见 EmailCodeCleaner 后台服务）
///  · 注册校验更严：用户名/邮箱唯一、密码强度、必须同意隐私条款
/// </summary>
[ApiController]
[Route("api/v4/account")]
public class AccountController : ControllerBase
{
    private readonly ServerDbContext _db;
    private readonly Services.EmailSender _email;
    private readonly Services.EmailRateLimiter _limiter;
    private readonly ILogger<AccountController> _logger;

    /// <summary>验证码有效期。</summary>
    private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(10);

    /// <summary>允许的验证码用途。</summary>
    private static readonly string[] AllowedPurposes = ["register", "reset"];

    public AccountController(
        ServerDbContext db,
        Services.EmailSender email,
        Services.EmailRateLimiter limiter,
        ILogger<AccountController> logger)
    {
        _db = db;
        _email = email;
        _limiter = limiter;
        _logger = logger;
    }

    // ══════════════ 发送验证码 ══════════════

    [HttpPost("send-code")]
    public async Task<IActionResult> SendCode([FromBody] SendCodeRequest req, CancellationToken ct)
    {
        var email = req.Email?.Trim().ToLowerInvariant() ?? "";
        var purpose = string.IsNullOrWhiteSpace(req.Purpose) ? "register" : req.Purpose.Trim().ToLowerInvariant();

        if (!IsValidEmail(email))
            return BadRequest(new { error = "请输入正确的邮箱地址。" });

        if (!AllowedPurposes.Contains(purpose))
            return BadRequest(new { error = "不支持的验证码用途。" });

        // 注册用途：邮箱已被占用则直接提示，避免浪费验证码
        if (purpose == "register" && await _db.Users.AnyAsync(u => u.Email == email && u.IsActive, ct))
            return BadRequest(new { error = "该邮箱已被注册，请直接登录或使用「忘记密码」。" });

        var settings = await _db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null || !settings.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "邮件服务未配置，请联系系统管理员。" });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (_limiter.Check(email, ip) is { } denial)
        {
            Response.Headers.RetryAfter = denial.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = denial.Reason });
        }

        // 生成 6 位验证码并哈希存储（不落明文）
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var entity = new EmailCodeEntity
        {
            Email = email,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code),
            Purpose = purpose,
            RequestIp = ip,
            CreatedAt = DateTime.Now,
            ExpireAt = DateTime.Now.Add(CodeTtl),
        };

        _db.EmailCodes.Add(entity);
        await _db.SaveChangesAsync(ct);

        var (ok, error) = await _email.SendVerifyCodeAsync(settings, email, code, purpose, ct);
        if (!ok)
        {
            // 发信失败：标记该验证码为已用，避免用户拿不到码却占用记录
            entity.Used = true;
            await _db.SaveChangesAsync(ct);
            return StatusCode(StatusCodes.Status502BadGateway, new { error });
        }

        _limiter.Record(email, ip);
        _logger.LogInformation("验证码已发送：{Email} 用途={Purpose} IP={Ip}", email, purpose, ip);

        return Ok(new
        {
            message = "验证码已发送，请查收邮件（10 分钟内有效）。",
            expires_in = (int)CodeTtl.TotalSeconds,
        });
    }

    // ══════════════ 注册 ══════════════

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        var email = req.Email?.Trim().ToLowerInvariant() ?? "";
        var username = req.Username?.Trim() ?? "";
        var mode = string.Equals(req.Mode, "join", StringComparison.OrdinalIgnoreCase) ? "join" : "region";

        if (!IsValidEmail(email))
            return BadRequest(new { error = "请输入正确的邮箱地址。" });

        if (username.Length < 3 || username.Length > 32)
            return BadRequest(new { error = "用户名长度需为 3–32 个字符。" });

        // 用户名不得包含 @（@ 是登录名「用户名@区域」的区域分隔符）
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9_\-\.]+$"))
            return BadRequest(new { error = "用户名只能包含字母、数字、下划线、短横线与点（不能包含 @）。" });

        if (string.IsNullOrEmpty(req.Password) || req.Password.Length < 6)
            return BadRequest(new { error = "密码至少 6 个字符。" });

        if (!req.AgreeTerms)
            return BadRequest(new { error = "请先阅读并同意服务条款与隐私政策。" });

        // 邮箱全局唯一（便于找回密码）
        if (await _db.Users.AnyAsync(u => u.Email == email && u.IsActive, ct))
            return BadRequest(new { error = "该邮箱已被注册。" });

        // 校验邮箱验证码
        var codeError = await ConsumeCodeAsync(email, "register", req.Code ?? "", ct);
        if (codeError != null) return BadRequest(new { error = codeError });

        User user;
        string regionId;
        if (mode == "join")
        {
            // ── 加入已有区域（家长/学生自助注册，App/小程序通用）──
            regionId = req.RegionId?.Trim().ToLowerInvariant() ?? "";
            var region = await _db.Regions.FirstOrDefaultAsync(r => r.RegionId == regionId, ct);
            if (region == null)
                return BadRequest(new { error = "区域代号不存在，请向机构索取正确的区域代号。" });
            if (!region.IsActive)
                return BadRequest(new { error = "该区域尚未激活或已到期，请稍后再试。" });

            if (await _db.Users.AnyAsync(u => u.Username == username && u.RegionId == regionId, ct))
                return BadRequest(new { error = "该用户名在当前区域已被占用。" });

            // 自助注册仅开放家长角色（教师/学生子账户由区域管理员创建）
            user = new User
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                Role = AppRoles.Parent,
                DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? username : req.DisplayName!.Trim(),
                Email = email,
                RegionId = regionId,
                IsActive = true,
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("家长自助注册成功：{Username}@{Region}（{Email}）", username, regionId, email);
            return Ok(new
            {
                message = "注册成功，请用「用户名@区域代号」登录。",
                username,
                regionId,
                loginName = $"{username}@{regionId}",
                role = AppRoles.Parent,
            });
        }

        // ── 创建新区域（v3.2 语义）：注册即创建区域并成为该区域主账号（owner），区域需激活后可用 ──
        var regionName = req.RegionName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(regionName))
            return BadRequest(new { error = "请填写区域名称（如机构/学校名称）。" });
        if (await _db.Regions.AnyAsync(r => r.Name == regionName, ct))
            return BadRequest(new { error = "区域名称已存在（区域名称不可重复）。" });

        var newRegionId = string.IsNullOrWhiteSpace(req.RegionId) ? GenerateRegionId() : req.RegionId!.Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(newRegionId, @"^[a-z0-9_-]{3,32}$"))
            return BadRequest(new { error = "区域代号仅支持 3~32 位小写字母/数字/下划线/连字符。" });
        if (await _db.Regions.AnyAsync(r => r.RegionId == newRegionId, ct))
            return BadRequest(new { error = "该区域代号已被使用。" });

        if (await _db.Users.AnyAsync(u => u.Username == username && (u.RegionId == newRegionId || u.RegionId == null), ct))
            return BadRequest(new { error = "该用户名已被占用。" });

        user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = AppRoles.Owner,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? username : req.DisplayName!.Trim(),
            Email = email,
            RegionId = newRegionId,
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        var created = new Region
        {
            RegionId = newRegionId,
            Name = regionName,
            DevicePassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(),
            OwnerUserId = user.Id,
        };
        _db.Regions.Add(created);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("新区域注册成功：{Username}@{Region}（{RegionName}，{Email}）", username, newRegionId, regionName, email);
        return Ok(new
        {
            message = "注册成功。区域需使用主区域颁发的激活码激活后，其他成员才能登录使用。",
            username,
            regionId = newRegionId,
            regionName,
            loginName = $"{username}@{newRegionId}",
            role = AppRoles.Owner,
            devicePassword = created.DevicePassword,
        });
    }

    /// <summary>生成不重复的区域代号（8 位小写字母数字，与 v3.2 GenerateRegionId 同规则）。</summary>
    private static string GenerateRegionId()
    {
        const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 8).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }

    // ══════════════ 重置密码 ══════════════

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        var email = req.Email?.Trim().ToLowerInvariant() ?? "";

        if (!IsValidEmail(email))
            return BadRequest(new { error = "请输入正确的邮箱地址。" });

        if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < 6)
            return BadRequest(new { error = "新密码至少 6 个字符。" });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user == null)
            return BadRequest(new { error = "该邮箱未注册。" });

        var codeError = await ConsumeCodeAsync(email, "reset", req.Code ?? "", ct);
        if (codeError != null) return BadRequest(new { error = codeError });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("用户重置密码：{Username}（{Email}）", user.Username, email);

        return Ok(new { message = "密码已重置，请用新密码登录。" });
    }

    // ══════════════ 内部 ══════════════

    /// <summary>
    /// 校验并消费验证码。成功返回 null；失败返回错误说明。
    /// 校验失败会累加尝试次数，超过 5 次即失效（防暴力枚举）。
    /// </summary>
    private async Task<string?> ConsumeCodeAsync(string email, string purpose, string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "请输入邮箱验证码。";

        var record = await _db.EmailCodes
            .Where(c => c.Email == email && c.Purpose == purpose && !c.Used)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);

        if (record == null)
            return "请先获取邮箱验证码。";

        if (record.ExpireAt <= DateTime.Now)
            return "验证码已过期，请重新获取。";

        if (record.Attempts >= 5)
            return "验证码尝试次数过多，请重新获取。";

        record.Attempts++;

        if (!BCrypt.Net.BCrypt.Verify(code.Trim(), record.CodeHash))
        {
            await _db.SaveChangesAsync(ct);
            return $"验证码不正确（还可尝试 {Math.Max(0, 5 - record.Attempts)} 次）。";
        }

        record.Used = true;
        await _db.SaveChangesAsync(ct);
        return null;
    }

    private static bool IsValidEmail(string email)
        => !string.IsNullOrWhiteSpace(email)
           && email.Length <= 128
           && email.Count(c => c == '@') == 1
           && email.IndexOf('@') > 0
           && email.IndexOf('@') < email.Length - 1
           && email.Contains('.');
}

// ══════════════ 请求模型 ══════════════

public record SendCodeRequest(string? Email, string? Purpose);

public record RegisterRequest(
    string? Email, string? Code, string? Username, string? Password,
    string? DisplayName, bool AgreeTerms,
    // region = 创建新区域（默认，v3.2 语义）；join = 加入已有区域（家长/学生自助）
    string? Mode, string? RegionName, string? RegionId);

public record ResetPasswordRequest(string? Email, string? Code, string? NewPassword);
