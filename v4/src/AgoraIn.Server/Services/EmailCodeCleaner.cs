using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Services;

/// <summary>
/// 邮箱验证码清理服务：定期删除已过期/已使用/尝试超限的记录。
///
/// v3.2 的 <c>EmailCodes</c> 表没有任何清理任务，已用与过期行永久堆积；
/// v4 每小时清理一次（保留最近 1 天用于审计）。
/// </summary>
public sealed class EmailCodeCleaner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailCodeCleaner> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public EmailCodeCleaner(IServiceScopeFactory scopeFactory, ILogger<EmailCodeCleaner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动后延迟 1 分钟再首次清理，避免与启动流程争抢
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            await CleanupAsync(stoppingToken);
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

            var cutoff = DateTime.Now.AddDays(-1);
            var removed = await db.EmailCodes
                .Where(c => c.ExpireAt < cutoff || (c.Used && c.CreatedAt < cutoff))
                .ExecuteDeleteAsync(ct);

            if (removed > 0)
            {
                _logger.LogInformation("已清理 {Count} 条过期邮箱验证码记录", removed);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理邮箱验证码失败（不影响主流程）");
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
