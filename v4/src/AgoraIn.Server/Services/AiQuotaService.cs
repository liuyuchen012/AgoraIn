using AgoraIn.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Services;

/// <summary>平台 AI 额度不足（租户使用平台配置且余额耗尽时抛出，全局过滤器转为 402 响应）。</summary>
public sealed class AiQuotaException : Exception
{
    public AiQuotaException(string message) : base(message) { }
}

/// <summary>
/// 租户 AI 额度服务：租户使用平台配置（未自配独立 AI 服务）时按实际消耗扣减额度。
/// 额度以人民币元计价存储（跨模型通用），兑换码由主区域签发。
/// 定价（等比例）：40 元 = Mimo 3.9 亿 Token = deepseek-v4-flash-vision-exp 3700 万 Token。
/// 租户自配独立 AI 服务（区域覆盖）时不消耗平台额度，由其直接与第三方结算。
/// 余额存于 AppSetting ai.quota.{regionId}（十进制元）；消耗明细可由 AiCallLogs 审计。
/// </summary>
public sealed class AiQuotaService(ServerDbContext db)
{
    public const decimal MimoTokensPerYuan = 9_750_000m;      // 40 元 = 3.9 亿
    public const decimal DeepSeekTokensPerYuan = 925_000m;    // 40 元 = 3700 万

    private static string BalanceKey(string regionId) => $"ai.quota.{regionId}";

    /// <summary>按模型折算 Token → 元。</summary>
    public static decimal TokensToYuan(long tokens, string model) =>
        model.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
            ? tokens / DeepSeekTokensPerYuan
            : tokens / MimoTokensPerYuan;

    /// <summary>读取区域余额（元；未兑换过为 0）。</summary>
    public async Task<decimal> GetBalanceAsync(string regionId, CancellationToken ct = default)
    {
        var value = await db.AppSettings
            .Where(x => x.Key == BalanceKey(regionId))
            .Select(x => x.Value)
            .FirstOrDefaultAsync(ct);
        return decimal.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }

    /// <summary>校验额度充足；不足抛 AiQuotaException（全局过滤器转 402）。</summary>
    public async Task EnsureSufficientAsync(string regionId, CancellationToken ct = default)
    {
        var balance = await GetBalanceAsync(regionId, ct);
        if (balance <= 0)
        {
            throw new AiQuotaException(
                "平台 AI 额度已用尽。请在本页输入额度激活码兑换，或在上方配置本区域独立的 AI 服务（使用自有第三方服务不消耗平台额度）");
        }
    }

    /// <summary>按实际消耗扣减额度（允许单次调用后短暂为负，展示层按 ≤0 视为耗尽）。</summary>
    public async Task DeductAsync(string regionId, long tokens, string model, CancellationToken ct = default)
    {
        if (tokens <= 0) return;
        var cost = TokensToYuan(tokens, model);
        var balance = await GetBalanceAsync(regionId, ct);
        var key = BalanceKey(regionId);
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        var newValue = (balance - cost).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        if (row != null) row.Value = newValue;
        else db.AppSettings.Add(new Core.Entities.AppSetting { Key = key, Value = newValue });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>兑换：额度入账。</summary>
    public async Task<decimal> AddAsync(string regionId, decimal amountYuan, CancellationToken ct = default)
    {
        var balance = await GetBalanceAsync(regionId, ct);
        var key = BalanceKey(regionId);
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        var newValue = (balance + amountYuan).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        if (row != null) row.Value = newValue;
        else db.AppSettings.Add(new Core.Entities.AppSetting { Key = key, Value = newValue });
        await db.SaveChangesAsync(ct);
        return balance + amountYuan;
    }
}
