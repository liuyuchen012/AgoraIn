namespace AgoraIn.Core.Licensing;

/// <summary>
/// 授权码载荷（与 v3.2 签发工具的数据契约 **逐字兼容**，存量激活码可直接使用）。
/// </summary>
public sealed class LicensePayload
{
    /// <summary>绑定的硬件指纹（大写 16 位 hex）。</summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>授权类型：server（服务器软件）/ region（区域）。</summary>
    public string Type { get; set; } = "server";

    /// <summary>授权时长（月）。</summary>
    public int Months { get; set; }

    /// <summary>允许的设备台数。</summary>
    public int MaxDevices { get; set; }

    /// <summary>客户名称（可空）。</summary>
    public string? Customer { get; set; }

    /// <summary>授权流水号（形如 SLyyyyMMdd-XXXXXXXX）。</summary>
    public string? Serial { get; set; }

    /// <summary>签发时间（ISO-8601）。</summary>
    public string? IssuedAt { get; set; }

    /// <summary>载荷版本。</summary>
    public int Version { get; set; } = 1;
}

/// <summary>授权状态。</summary>
public enum LicenseStatus
{
    /// <summary>尚未激活。</summary>
    NotActivated = 0,

    /// <summary>已激活且在有效期内。</summary>
    Active = 1,

    /// <summary>已过期。</summary>
    Expired = 2,
}

/// <summary>授权状态快照（对外展示 / API 返回）。</summary>
public sealed class LicenseState
{
    public string Fingerprint { get; set; } = "";
    public bool Activated { get; set; }
    public string? Type { get; set; }
    public string? Customer { get; set; }
    public string? Serial { get; set; }
    public int Months { get; set; }
    public int MaxDevices { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? ExpireAt { get; set; }

    /// <summary>剩余天数（未激活为 0；已过期为负数）。</summary>
    public int RemainingDays => ExpireAt is { } exp
        ? (int)Math.Ceiling((exp - DateTime.Now).TotalDays)
        : 0;

    public LicenseStatus Status => !Activated
        ? LicenseStatus.NotActivated
        : ExpireAt is { } exp && exp <= DateTime.Now
            ? LicenseStatus.Expired
            : LicenseStatus.Active;

    /// <summary>是否允许注册新设备（未激活或已过期时不允许）。</summary>
    public bool AllowsNewDevice => Status == LicenseStatus.Active;

    /// <summary>活动授权返回的提示文案（无问题时为 null）。</summary>
    public string? WarningMessage => Status switch
    {
        LicenseStatus.NotActivated => "服务端尚未激活，无法注册新设备。请在 Web 管理面板「授权」页输入激活码。",
        LicenseStatus.Expired => $"授权已于 {ExpireAt:yyyy-MM-dd} 到期，无法注册新设备。请续期后重新激活。",
        _ when RemainingDays <= 30 => $"授权将在 {RemainingDays} 天后到期（{ExpireAt:yyyy-MM-dd}），请及时续期。",
        _ => null,
    };
}
