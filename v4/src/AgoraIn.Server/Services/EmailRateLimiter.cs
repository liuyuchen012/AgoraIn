using System.Collections.Concurrent;

namespace AgoraIn.Server.Services;

/// <summary>
/// 验证码发送限流器（内存实现）。
///
/// v3.2 的 <c>/api/account/send_code</c> **完全没有服务端限流**：同一邮箱可无限次发信、
/// 6 位验证码可无限次试错，存在刷信与暴力枚举风险。v4 补上：
///  · 同邮箱冷却（默认 60 秒）
///  · 同邮箱日配额（默认 5 次）
///  · 同 IP 小时配额（默认 20 次）
///  · 验证码校验失败次数上限（默认 5 次，见 EmailCodeEntity.Attempts）
///
/// 单机部署足够；若将来多实例，替换为 Redis 等共享存储即可。
/// </summary>
public sealed class EmailRateLimiter
{
    private sealed class Entry
    {
        public DateTime LastSent;
        public int SentToday;
        public DateOnly Day;
    }

    private readonly ConcurrentDictionary<string, Entry> _byEmail = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<DateTime>> _byIp = new(StringComparer.OrdinalIgnoreCase);

    private DateTime _lastPrune = DateTime.Now;

    /// <summary>同邮箱冷却时长。</summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>同邮箱每日上限。</summary>
    public int DailyPerEmail { get; init; } = 5;

    /// <summary>同 IP 每小时上限。</summary>
    public int HourlyPerIp { get; init; } = 20;

    /// <summary>
    /// 检查是否允许发送；返回 null 表示允许，否则返回拒绝原因与建议等待秒数。
    /// </summary>
    public (string Reason, int RetryAfterSeconds)? Check(string email, string? ip)
    {
        Prune();

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);

        var entry = _byEmail.GetOrAdd(email, _ => new Entry { Day = today });
        lock (entry)
        {
            if (entry.Day != today)
            {
                entry.Day = today;
                entry.SentToday = 0;
            }

            var sinceLast = now - entry.LastSent;
            if (entry.LastSent != default && sinceLast < Cooldown)
            {
                var wait = (int)Math.Ceiling((Cooldown - sinceLast).TotalSeconds);
                return ($"请求过于频繁，请在 {wait} 秒后重试。", wait);
            }

            if (entry.SentToday >= DailyPerEmail)
            {
                return ($"该邮箱今日验证码发送次数已达上限（{DailyPerEmail} 次），请明天再试。", 0);
            }
        }

        if (!string.IsNullOrEmpty(ip))
        {
            var list = _byIp.GetOrAdd(ip, _ => []);
            lock (list)
            {
                var recent = list.Where(t => (now - t).TotalHours < 1).ToList();
                if (recent.Count >= HourlyPerIp)
                {
                    return ("当前网络请求验证码过于频繁，请稍后再试。", 0);
                }
            }
        }

        return null;
    }

    /// <summary>记录一次成功发送。</summary>
    public void Record(string email, string? ip)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);

        var entry = _byEmail.GetOrAdd(email, _ => new Entry { Day = today });
        lock (entry)
        {
            if (entry.Day != today)
            {
                entry.Day = today;
                entry.SentToday = 0;
            }

            entry.LastSent = now;
            entry.SentToday++;
        }

        if (!string.IsNullOrEmpty(ip))
        {
            var list = _byIp.GetOrAdd(ip, _ => []);
            lock (list)
            {
                list.Add(now);
            }
        }
    }

    /// <summary>清理 24 小时前的记录，避免字典无限增长。</summary>
    private void Prune()
    {
        var now = DateTime.Now;
        if ((now - _lastPrune).TotalMinutes < 10) return;
        _lastPrune = now;

        foreach (var key in _byEmail.Keys)
        {
            if (_byEmail.TryGetValue(key, out var e) && (now - e.LastSent).TotalHours > 24 && e.SentToday == 0)
                _byEmail.TryRemove(key, out _);
        }

        foreach (var key in _byIp.Keys)
        {
            if (!_byIp.TryGetValue(key, out var list)) continue;
            lock (list)
            {
                list.RemoveAll(t => (now - t).TotalHours >= 1);
                if (list.Count == 0) _byIp.TryRemove(key, out _);
            }
        }
    }
}
