using AgoraIn.Core.Licensing;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Services;

/// <summary>
/// 授权服务：离线激活码校验、授权状态维护与限制判定。
///
/// 相比 v3.2 的改进：
///  1. **真正生效**：设备注册接口会检查「是否激活 / 是否过期 / 设备数是否超限」（v3 仅激活接口存在，
///     服务器软件授权无任何强制，等于摆设）。
///  2. **指纹固定**：首次激活时把当时算出的指纹写入库并固定，之后以库中值为准比对，
///     避免多网卡/容器/系统升级导致指纹漂移而授权突然失效；换机时由管理员提供新指纹重新激活。
///  3. **到期提醒**：剩余 ≤30 天时返回告警文案，Web 面板与桌面端均可展示。
/// </summary>
public sealed class LicenseService
{
    private readonly ServerDbContext _db;
    private readonly ILogger<LicenseService> _logger;

    public LicenseService(ServerDbContext db, ILogger<LicenseService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>读取当前授权状态（未激活时也会返回本机指纹，便于申请激活码）。</summary>
    public async Task<LicenseState> GetStateAsync(CancellationToken ct = default)
    {
        var entity = await _db.License.AsNoTracking().FirstOrDefaultAsync(ct);
        var currentFingerprint = MachineFingerprint.Compute();

        if (entity == null)
        {
            return new LicenseState { Fingerprint = currentFingerprint, Activated = false };
        }

        return new LicenseState
        {
            Fingerprint = entity.Fingerprint,
            Activated = entity.ExpireAt > DateTime.Now,
            Type = entity.Type,
            Customer = entity.Customer,
            Serial = entity.Serial,
            Months = entity.Months,
            MaxDevices = entity.MaxDevices,
            ActivatedAt = entity.ActivatedAt,
            ExpireAt = entity.ExpireAt,
        };
    }

    /// <summary>
    /// 激活（首次）或重新激活（续期/换机）。
    /// </summary>
    /// <param name="activationCode">激活码。</param>
    /// <param name="forceRebind">
    /// true 时忽略激活码内指纹与本机指纹不一致（用于换机重新绑定，需管理员操作并留痕）。
    /// </param>
    public async Task<(bool Ok, string Message, LicenseState? State)> ActivateAsync(
        string activationCode, bool forceRebind = false, CancellationToken ct = default)
    {
        LicensePayload payload;
        try
        {
            payload = LicenseCodec.Decode(activationCode);
        }
        catch (InvalidDataException ex)
        {
            return (false, ex.Message, null);
        }

        var machineFingerprint = MachineFingerprint.Compute();

        if (!forceRebind &&
            !string.Equals(payload.Fingerprint, machineFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return (false,
                $"激活码与本机不匹配。激活码绑定指纹 {payload.Fingerprint}，本机指纹 {machineFingerprint}。" +
                "若确为换机，请使用「强制重新绑定」并说明原因。",
                null);
        }

        var entity = await _db.License.FirstOrDefaultAsync(ct);
        var now = DateTime.Now;

        // 续期：在现有到期时间基础上叠加；首次/换机：从当前时刻起算
        var baseTime = entity is { ExpireAt: var exp } && exp > now ? exp : now;
        var expireAt = baseTime.AddMonths(payload.Months);

        if (entity == null)
        {
            entity = new LicenseEntity();
            _db.License.Add(entity);
        }

        entity.Fingerprint = machineFingerprint;
        entity.Type = payload.Type;
        entity.Months = payload.Months;
        entity.MaxDevices = payload.MaxDevices;
        entity.Customer = payload.Customer;
        entity.Serial = payload.Serial;
        entity.ActivatedAt = now;
        entity.ExpireAt = expireAt;
        entity.Code = LicenseCodec.Normalize(activationCode);
        entity.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "授权激活成功：类型={Type} 客户={Customer} 流水号={Serial} 设备上限={MaxDevices} 到期={ExpireAt:yyyy-MM-dd} 重绑={Force}",
            entity.Type, entity.Customer, entity.Serial, entity.MaxDevices, entity.ExpireAt, forceRebind);

        return (true, $"激活成功，有效期至 {expireAt:yyyy-MM-dd}（设备上限 {entity.MaxDevices} 台）。", await GetStateAsync(ct));
    }

    /// <summary>
    /// 设备注册前的授权检查。返回 null 表示允许；否则返回拒绝原因。
    /// </summary>
    public async Task<string?> CheckDeviceRegistrationAsync(CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);

        if (state.Status != LicenseStatus.Active)
        {
            return state.WarningMessage;
        }

        var deviceCount = await _db.Devices.CountAsync(ct);
        if (deviceCount >= state.MaxDevices)
        {
            return $"设备数量已达授权上限（{state.MaxDevices} 台）。如需增加，请续期或升级授权。";
        }

        return null;
    }
}
