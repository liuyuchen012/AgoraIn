using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgoraIn.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Services;

/// <summary>区域 AI 额度兑换码载荷（面额为人民币元）。</summary>
public sealed record RegionQuotaPayload(string Region, decimal AmountYuan, string IssuedAt, string Nonce);

/// <summary>区域激活码载荷（HMAC-SHA256 签名，服务端签发 + 服务端验证，与 v3.2 区域激活同思路）。</summary>
public sealed record RegionCodePayload(string Region, int Months, int MaxDevices, string IssuedAt, string Nonce);

/// <summary>
/// 子区域激活码签发/校验：主区域（manager）为子区域颁发，子区域主账号激活时校验。
/// 与服务器整体授权（RSA 离线激活码，见 LicenseService）是两套独立体系：
/// 服务器激活码由离线工具签发，区域激活码由本服务在服务端签发（HMAC 密钥首次使用时随机生成并落 AppSettings）。
/// </summary>
public sealed class RegionCodeService
{
    private const string KeySettingName = "regionCode.signingKey";
    private const string Prefix = "AGRR-";
    private static readonly JsonSerializerOptions JsonOpts = new();

    private readonly ServerDbContext _db;

    public RegionCodeService(ServerDbContext db) => _db = db;

    /// <summary>为指定区域签发激活码。</summary>
    public async Task<string> IssueAsync(string regionId, int months, int maxDevices, CancellationToken ct = default)
    {
        if (months is < 1 or > 600) throw new ArgumentException("时长须在 1-600 个月之间", nameof(months));
        if (maxDevices is < 1 or > 65535) throw new ArgumentException("设备数须在 1-65535 之间", nameof(maxDevices));

        var payload = new RegionCodePayload(
            regionId, months, maxDevices,
            DateTime.Now.ToString("O"),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(8)));

        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var key = await GetOrCreateKeyAsync(ct);
        var sig = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(json));

        return Prefix
            + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
            + "."
            + Convert.ToBase64String(sig).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>校验激活码；无效抛 <see cref="InvalidDataException"/>，区域不匹配返回 null。</summary>
    public async Task<RegionCodePayload?> VerifyAsync(string code, string expectedRegion, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(Prefix, StringComparison.Ordinal)) return null;

            var body = code[Prefix.Length..];
            var dot = body.IndexOf('.');
            if (dot <= 0) return null;

            var jsonB64 = body[..dot].Replace('-', '+').Replace('_', '/');
            var sigB64 = body[(dot + 1)..].Replace('-', '+').Replace('_', '/');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(jsonB64)));
            var sig = Convert.FromBase64String(Pad(sigB64));

            var key = await GetOrCreateKeyAsync(ct);
            var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(json));
            if (!CryptographicOperations.FixedTimeEquals(expected, sig)) return null;

            var payload = JsonSerializer.Deserialize<RegionCodePayload>(json, JsonOpts);
            if (payload == null) return null;
            if (payload.Months is < 1 or > 600 || payload.MaxDevices is < 1 or > 65535) return null;
            if (!string.Equals(payload.Region, expectedRegion, StringComparison.OrdinalIgnoreCase)) return null;
            return payload;
        }
        catch
        {
            return null;
        }
    }

    private async Task<byte[]> GetOrCreateKeyAsync(CancellationToken ct)
    {
        var row = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == KeySettingName, ct);
        if (row != null && row.Value.Length >= 32) return Convert.FromHexString(row.Value);

        var key = RandomNumberGenerator.GetBytes(32);
        var hex = Convert.ToHexString(key);
        if (row != null) row.Value = hex;
        else _db.AppSettings.Add(new Core.Entities.AppSetting { Key = KeySettingName, Value = hex });
        await _db.SaveChangesAsync(ct);
        return key;
    }

    /// <summary>
    /// 为区域签发 AI 额度兑换码（AGRT- 前缀，绑定区域，面额为人民币元）。
    /// 租户在 AI 批改设置页兑换后额度入账；兑换码与区域激活码（AGRR-）相互独立。
    /// </summary>
    public async Task<string> IssueQuotaCodeAsync(string regionId, decimal amountYuan, CancellationToken ct = default)
    {
        if (amountYuan is < 1 or > 100000) throw new ArgumentException("面额须在 1-100000 元之间", nameof(amountYuan));

        var payload = new RegionQuotaPayload(
            regionId, amountYuan,
            DateTime.Now.ToString("O"),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(8)));

        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var key = await GetOrCreateKeyAsync(ct);
        var sig = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(json));

        return "AGRT-"
            + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
            + "."
            + Convert.ToBase64String(sig).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>校验额度兑换码；无效返回 null，区域不匹配返回 null。</summary>
    public async Task<RegionQuotaPayload?> VerifyQuotaCodeAsync(string code, string expectedRegion, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(code) || !code.StartsWith("AGRT-", StringComparison.Ordinal)) return null;

            var body = code["AGRT-".Length..];
            var dot = body.IndexOf('.');
            if (dot <= 0) return null;

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(body[..dot].Replace('-', '+').Replace('_', '/'))));
            var sig = Convert.FromBase64String(Pad(body[(dot + 1)..].Replace('-', '+').Replace('_', '/')));

            var key = await GetOrCreateKeyAsync(ct);
            var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(json));
            if (!CryptographicOperations.FixedTimeEquals(expected, sig)) return null;

            var payload = JsonSerializer.Deserialize<RegionQuotaPayload>(json, JsonOpts);
            if (payload == null || payload.AmountYuan is < 1 or > 100000) return null;
            if (!string.Equals(payload.Region, expectedRegion, StringComparison.OrdinalIgnoreCase)) return null;
            return payload;
        }
        catch
        {
            return null;
        }
    }

    private static string Pad(string b64)
    {
        var remainder = b64.Length % 4;
        return remainder switch
        {
            2 => b64 + "==",
            3 => b64 + "=",
            _ => b64,
        };
    }
}
