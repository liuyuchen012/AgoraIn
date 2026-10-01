using System.Security.Claims;
using AgoraIn.Core.Security;
using AgoraIn.Server.Models;
using AgoraIn.Server.Security;
using AgoraIn.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 区域（租户）管理 API。前缀 /api/v4/regions
/// 主区域（manager，房主）创建/管理子区域并颁发激活码；子区域主账号用激活码激活自己的区域。
/// </summary>
[ApiController]
[Route("api/v4/regions")]
[Authorize]
public class RegionsController : ControllerBase
{
    private readonly ServerDbContext _db;
    private readonly RegionCodeService _codes;

    public RegionsController(ServerDbContext db, RegionCodeService codes)
    {
        _db = db;
        _codes = codes;
    }

    /// <summary>区域列表（仅主区域）。</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!await IsManagerAsync(ct)) return Forbid();

        var regions = await _db.Regions
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        var ownerIds = regions.Select(r => r.OwnerUserId).Distinct().ToList();
        var owners = await _db.Users
            .Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        return Ok(regions.Select(r => new
        {
            r.Id, r.RegionId, r.Name, r.DevicePassword,
            ownerUsername = owners.TryGetValue(r.OwnerUserId, out var o) ? o : "",
            r.Activated, r.ExpireAt, r.MaxDevices, r.ActivatedAt, r.CreatedAt,
            isActive = r.IsActive,
            remainingDays = r.ExpireAt is { } exp ? (int)Math.Ceiling((exp - DateTime.Now).TotalDays) : 0,
        }));
    }

    /// <summary>创建子区域（仅主区域；同时创建区域主账号，用于线下销售开通）。</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRegionRequest req, CancellationToken ct)
    {
        if (!await IsManagerAsync(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "区域名称必填" });
        if (string.IsNullOrWhiteSpace(req.OwnerUsername) || string.IsNullOrWhiteSpace(req.OwnerPassword))
            return BadRequest(new { error = "区域主账号用户名与密码必填" });
        if (req.OwnerPassword.Length < 6) return BadRequest(new { error = "密码至少 6 位" });

        var regionId = await NormalizeRegionIdAsync(req.RegionId, ct);
        if (await _db.Regions.AnyAsync(r => r.Name == req.Name.Trim(), ct))
            return BadRequest(new { error = "区域名称已存在（区域名称不可重复）" });

        var owner = new User
        {
            Username = req.OwnerUsername.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.OwnerPassword),
            Role = AppRoles.Owner,
            DisplayName = string.IsNullOrWhiteSpace(req.OwnerDisplayName) ? req.OwnerUsername.Trim() : req.OwnerDisplayName,
            RegionId = regionId,
            IsActive = true,
        };
        _db.Users.Add(owner);
        await _db.SaveChangesAsync(ct);

        var region = new Region
        {
            RegionId = regionId,
            Name = req.Name.Trim(),
            DevicePassword = GenerateSecret(),
            OwnerUserId = owner.Id,
        };
        _db.Regions.Add(region);
        await _db.SaveChangesAsync(ct);

        return Created("", new { region.Id, region.RegionId, region.Name, owner.Username, activated = false });
    }

    /// <summary>为子区域颁发激活码（仅主区域）。</summary>
    [HttpPost("{regionId}/issue-code")]
    public async Task<IActionResult> IssueCode(string regionId, [FromBody] IssueRegionCodeRequest req, CancellationToken ct)
    {
        if (!await IsManagerAsync(ct)) return Forbid();
        var region = await _db.Regions.FirstOrDefaultAsync(r => r.RegionId == regionId, ct);
        if (region == null) return NotFound(new { error = "区域不存在" });
        if (req.Months < 1 || req.MaxDevices < 1)
            return BadRequest(new { error = "时长与设备数必须为正数" });

        var code = await _codes.IssueAsync(region.RegionId, req.Months, req.MaxDevices, ct);
        return Ok(new { activationCode = code, months = req.Months, maxDevices = req.MaxDevices });
    }

    /// <summary>激活自己的区域（子区域主账号；输入主区域颁发的激活码）。</summary>
    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] ActivateRegionRequest req, CancellationToken ct)
    {
        var username = User.Identity?.Name ?? "";
        var regionId = RegionContext.Current;
        if (regionId == RegionContext.ManagerRegion)
            return BadRequest(new { error = "主区域由服务器整体授权激活，无需区域激活码" });

        var user = await FindUserAsync(username, regionId, ct);
        if (user == null) return NotFound();
        if (user.Role is not (AppRoles.Owner or AppRoles.Admin))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "仅区域主账号可激活" });

        var region = await _db.Regions.FirstOrDefaultAsync(r => r.RegionId == regionId, ct);
        if (region == null) return NotFound(new { error = "区域不存在" });

        var payload = await _codes.VerifyAsync(req.ActivationCode ?? "", region.RegionId, ct);
        if (payload == null) return BadRequest(new { error = "激活码无效或不属于当前区域" });

        // 激活时开始计时；未到期续期在原到期时间上叠加
        var now = DateTime.Now;
        var baseTime = region.ExpireAt is { } exp && exp > now ? exp : now;
        region.Activated = true;
        region.ExpireAt = baseTime.AddMonths(payload.Months);
        region.MaxDevices = payload.MaxDevices;
        region.ActivatedAt = now;
        region.ActivationCode = Mask(req.ActivationCode!);
        await _db.SaveChangesAsync(ct);

        return Ok(new { region.RegionId, expireAt = region.ExpireAt, maxDevices = region.MaxDevices });
    }

    /// <summary>当前用户所属区域的状态。</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var regionId = RegionContext.Current;
        if (regionId == RegionContext.ManagerRegion)
            return Ok(new { regionId, isManager = true, isActive = true });

        var region = await _db.Regions.FirstOrDefaultAsync(r => r.RegionId == regionId, ct);
        if (region == null) return NotFound();
        return Ok(new
        {
            regionId = region.RegionId, name = region.Name, isManager = false,
            region.Activated, region.ExpireAt, region.MaxDevices,
            isActive = region.IsActive,
            remainingDays = region.ExpireAt is { } exp ? (int)Math.Ceiling((exp - DateTime.Now).TotalDays) : 0,
        });
    }

    /// <summary>
    /// 读取区域自定义隐私协议（匿名，注册页展示用）。
    /// 仅当区域管理员制作了协议时返回内容。
    /// </summary>
    [HttpGet("{regionId}/agreement")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAgreement(string regionId, CancellationToken ct)
    {
        var region = await _db.Regions.FirstOrDefaultAsync(r => r.RegionId == regionId, ct);
        if (region == null) return NotFound(new { error = "区域不存在" });
        var hasCustom = !string.IsNullOrWhiteSpace(region.CustomPrivacyContent);
        return Ok(new
        {
            regionId = region.RegionId,
            regionName = region.Name,
            hasCustom,
            content = hasCustom ? region.CustomPrivacyContent : null,
            updatedAt = region.CustomPrivacyUpdatedAt,
        });
    }

    /// <summary>保存本区域的自定义隐私协议（区域主账号自助维护）。</summary>
    [HttpPut("me/agreement")]
    public async Task<IActionResult> SaveOwnAgreement([FromBody] RegionAgreementRequest req, CancellationToken ct)
    {
        var username = User.Identity?.Name ?? "";
        var regionId = RegionContext.Current;
        if (regionId == RegionContext.ManagerRegion)
            return BadRequest(new { error = "主区域使用平台统一协议，无需自制协议" });

        var user = await FindUserAsync(username, regionId, ct);
        if (user == null) return NotFound();
        if (user.Role is not (AppRoles.Owner or AppRoles.Admin))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "仅区域主账号可维护协议" });

        var region = await _db.Regions.FirstOrDefaultAsync(r => r.RegionId == regionId, ct);
        if (region == null) return NotFound();

        region.CustomPrivacyContent = string.IsNullOrWhiteSpace(req.Content) ? null : req.Content;
        region.CustomPrivacyUpdatedAt = string.IsNullOrWhiteSpace(req.Content) ? null : DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return Ok(new { regionId = region.RegionId, hasCustom = region.CustomPrivacyContent != null, updatedAt = region.CustomPrivacyUpdatedAt });
    }

    /// <summary>保存指定区域的自定义隐私协议（仅主区域，代机构维护）。</summary>
    [HttpPut("{regionId}/agreement")]
    public async Task<IActionResult> SaveAgreement(string regionId, [FromBody] RegionAgreementRequest req, CancellationToken ct)
    {
        if (!await IsManagerAsync(ct)) return Forbid();
        var region = await _db.Regions.FirstOrDefaultAsync(r => r.RegionId == regionId, ct);
        if (region == null) return NotFound(new { error = "区域不存在" });

        region.CustomPrivacyContent = string.IsNullOrWhiteSpace(req.Content) ? null : req.Content;
        region.CustomPrivacyUpdatedAt = string.IsNullOrWhiteSpace(req.Content) ? null : DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return Ok(new { regionId = region.RegionId, hasCustom = region.CustomPrivacyContent != null, updatedAt = region.CustomPrivacyUpdatedAt });
    }

    /// <summary>删除子区域（仅主区域；区域内数据将不可见但保留，误删可联系运维恢复）。</summary>
    [HttpDelete("{regionId}")]
    public async Task<IActionResult> Delete(string regionId, CancellationToken ct)
    {
        if (!await IsManagerAsync(ct)) return Forbid();
        if (regionId == RegionContext.ManagerRegion)
            return BadRequest(new { error = "不能删除主区域" });

        var region = await _db.Regions.FirstOrDefaultAsync(r => r.RegionId == regionId, ct);
        if (region == null) return NotFound();
        _db.Regions.Remove(region);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── helpers ──

    private async Task<bool> IsManagerAsync(CancellationToken ct)
    {
        if (!RegionContext.IsManager) return false;
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var normalized = AppRoles.Normalize(role);
        return normalized is AppRoles.Admin or AppRoles.Owner;
    }

    private async Task<User?> FindUserAsync(string username, string regionId, CancellationToken ct)
        => await _db.Users.FirstOrDefaultAsync(
            u => u.Username == username && (u.RegionId == regionId || (u.RegionId == null && regionId == RegionContext.ManagerRegion)), ct);

    /// <summary>校验/生成区域代号（3-32 位字母数字下划线连字符，全局唯一）。</summary>
    private async Task<string> NormalizeRegionIdAsync(string? input, CancellationToken ct)
    {
        var regionId = string.IsNullOrWhiteSpace(input) ? GenerateRegionId() : input.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(regionId, @"^[a-zA-Z0-9_-]{3,32}$"))
            throw new BadHttpRequestException("区域代号仅支持 3~32 位字母/数字/下划线/连字符");
        if (await _db.Regions.AnyAsync(r => r.RegionId == regionId, ct))
            throw new BadHttpRequestException("该区域代号已被使用");
        return regionId;
    }

    private static string GenerateRegionId()
    {
        const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 8).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }

    private static string GenerateSecret() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    private static string Mask(string code) => code.Length <= 12
        ? code
        : $"{code[..8]}…{code[^4..]}";
}

public record CreateRegionRequest(string? RegionId, string Name, string OwnerUsername, string OwnerPassword, string? OwnerDisplayName);
public record IssueRegionCodeRequest(int Months, int MaxDevices);
public record ActivateRegionRequest(string? ActivationCode);
public record RegionAgreementRequest(string? Content);
