namespace AgoraIn.Core.Domain;

/// <summary>点名候选：学生与其历史被点情况（供落点算法加权）。</summary>
/// <param name="StudentId">学生 Id。</param>
/// <param name="LastCalledAt">最近一次被点时间（null = 本周期从未被点）。</param>
/// <param name="TimesCalled">累计被点次数。</param>
public sealed record RollCallCandidate(string StudentId, DateTime? LastCalledAt, int TimesCalled);

/// <summary>
/// 点名落点算法（纯函数、可单测）。
/// 随机模式支持"近期未被点过优先"加权：距上次被点越久、累计被点越少，权重越高；
/// 顺序模式按调用方传入的名单顺序（学号序）轮转。
/// </summary>
public static class RollCallPicker
{
    /// <summary>加权随机的闲置分钟封顶值（也是"从未被点过"学生的闲置记法）。</summary>
    public const double MaxIdleMinutes = 60;

    /// <summary>
    /// 加权随机挑出下一个被点学生；人人保底权重 1，绝不会被完全排除。
    /// <paramref name="candidates"/> 为空返回 null。
    /// </summary>
    public static string? PickWeighted(IReadOnlyList<RollCallCandidate> candidates, DateTime now, Random? rng = null)
    {
        if (candidates.Count == 0) return null;
        rng ??= Random.Shared;

        var weights = new double[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var idle = c.LastCalledAt is { } last ? (now - last).TotalMinutes : MaxIdleMinutes;
            if (idle < 0) idle = 0;
            if (idle > MaxIdleMinutes) idle = MaxIdleMinutes;
            // 权重 = 保底 1 + 闲置分钟（封顶） - 累计被点次数的均衡项（不低于 0）
            var balance = Math.Max(0, MaxIdleMinutes / 4 - c.TimesCalled);
            weights[i] = 1 + idle + balance;
        }

        var roll = rng.NextDouble() * weights.Sum();
        for (var i = 0; i < candidates.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0) return candidates[i].StudentId;
        }
        return candidates[^1].StudentId;
    }

    /// <summary>
    /// 顺序模式：按本场次被点次数最少者轮转；并列时保持调用方传入的名单顺序
    /// （调用方按学号序传入即得"按学号轮流"语义）。空名单返回 null。
    /// </summary>
    public static string? PickSequential(IReadOnlyList<RollCallCandidate> candidates)
    {
        if (candidates.Count == 0) return null;
        var min = candidates.Min(c => c.TimesCalled);
        return candidates.First(c => c.TimesCalled == min).StudentId;
    }
}
