using AgoraIn.Core.Entities;

namespace AgoraIn.Core.Domain;

/// <summary>
/// 积分领域服务：规则适用性校验与每日上限裁定。
/// </summary>
public static class PointService
{
    /// <summary>
    /// 判断规则是否适用于指定班级（规则未指定班级 = 全部通用）。
    /// </summary>
    public static bool IsApplicable(PointRule rule, string classId)
        => rule.Enabled && (string.IsNullOrEmpty(rule.ClassId) || rule.ClassId == classId);

    /// <summary>
    /// 计算本次实际入账分值，执行"每日上限"：
    /// 正分受 当日该生该规则累计加分为准的上限约束（不足则部分入账，返回 0 表示已封顶）；
    /// 负分（扣分）不受上限约束。
    /// </summary>
    /// <param name="rule">积分规则。</param>
    /// <param name="requestedDelta">请求分值（取规则默认或教师调整值）。</param>
    /// <param name="todayPositiveTotal">该生今日该规则已累计的正分总和。</param>
    public static int EffectiveDelta(PointRule rule, int requestedDelta, int todayPositiveTotal)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (requestedDelta <= 0)
        {
            return requestedDelta;
        }

        if (rule.DailyCap is not { } cap)
        {
            return requestedDelta;
        }

        var remaining = cap - todayPositiveTotal;
        return Math.Clamp(remaining, 0, requestedDelta);
    }

    /// <summary>
    /// 创建一条积分流水（入账前请先经 <see cref="EffectiveDelta"/> 裁定实际分值）。
    /// </summary>
    public static PointRecord CreateRecord(
        string studentId,
        int delta,
        PointSource source,
        string operatorName,
        string? ruleId = null,
        string reason = "",
        string? sourceId = null)
        => new()
        {
            StudentId = studentId,
            Delta = delta,
            Source = source,
            OperatorName = operatorName,
            RuleId = ruleId,
            Reason = reason,
            SourceId = sourceId,
            CreatedAt = DateTime.Now,
        };
}
