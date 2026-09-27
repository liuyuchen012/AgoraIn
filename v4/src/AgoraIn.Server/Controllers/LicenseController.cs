using AgoraIn.Core.Licensing;
using AgoraIn.Core.Security;
using AgoraIn.Server.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 授权（离线激活）API。前缀 /api/v4/license
///
/// 说明：v4 保留离线激活机制（提示词 6.1 要求「离线激活/授权校验逻辑等价迁移」），
/// 激活码格式与 v3.2 签发工具完全兼容，存量激活码可直接使用。
/// </summary>
[ApiController]
[Route("api/v4/license")]
[Authorize]
[RequirePermission(Permissions.LicenseManage)]
public class LicenseController : ControllerBase
{
    private readonly Services.LicenseService _license;
    public LicenseController(Services.LicenseService license) => _license = license;

    /// <summary>查询授权状态（含本机指纹，用于申请激活码）。</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var state = await _license.GetStateAsync(ct);
        return Ok(new
        {
            fingerprint = state.Fingerprint,
            activated = state.Activated,
            status = state.Status.ToString(),
            type = state.Type,
            customer = state.Customer,
            serial = state.Serial,
            months = state.Months,
            max_devices = state.MaxDevices,
            activated_at = state.ActivatedAt,
            expire_at = state.ExpireAt,
            remaining_days = state.RemainingDays,
            warning = state.WarningMessage,
        });
    }

    /// <summary>激活 / 续期 / 换机重绑（仅管理员）。</summary>
    [HttpPost("activate")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Activate([FromBody] ActivateRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ActivationCode))
            return BadRequest(new { error = "请输入激活码。" });

        var (ok, message, state) = await _license.ActivateAsync(req.ActivationCode, req.ForceRebind, ct);
        if (!ok) return BadRequest(new { error = message });

        return Ok(new
        {
            message,
            fingerprint = state!.Fingerprint,
            activated = state.Activated,
            type = state.Type,
            customer = state.Customer,
            serial = state.Serial,
            max_devices = state.MaxDevices,
            expire_at = state.ExpireAt,
            remaining_days = state.RemainingDays,
        });
    }

    /// <summary>校验激活码但不落库（用于管理员预览：类型/时长/设备数/是否匹配本机）。</summary>
    [HttpPost("verify")]
    [Authorize(Roles = "admin")]
    public IActionResult Verify([FromBody] VerifyRequest req)
    {
        try
        {
            var payload = LicenseCodec.Decode(req.ActivationCode);
            var machineFingerprint = MachineFingerprint.Compute();
            var matches = string.Equals(payload.Fingerprint, machineFingerprint, StringComparison.OrdinalIgnoreCase);

            return Ok(new
            {
                valid = true,
                matches_this_machine = matches,
                this_fingerprint = machineFingerprint,
                payload.Fingerprint,
                payload.Type,
                payload.Months,
                payload.MaxDevices,
                payload.Customer,
                payload.Serial,
                payload.IssuedAt,
                payload.Version,
            });
        }
        catch (InvalidDataException ex)
        {
            return Ok(new { valid = false, error = ex.Message });
        }
    }
}

public record ActivateRequest(string ActivationCode, bool ForceRebind);

public record VerifyRequest(string ActivationCode);
