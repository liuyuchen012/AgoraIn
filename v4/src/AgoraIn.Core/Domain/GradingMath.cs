namespace AgoraIn.Core.Domain;

/// <summary>
/// 双判合分规则：两位教师分数取平均（"近似值"），再**进位到 0.5**
/// （用户规则示例：平均 1.48 → 1.5，1.78 → 2，1.23 → 1.5，0.98 → 1
///  —— 全部向上进位到下一个 0.5，恰好落在 0.5 边界上的不变）。
/// </summary>
public static class GradingMath
{
    /// <summary>进位到 0.5 的整数倍（1.23 → 1.5，0.98 → 1.0，1.5 → 1.5）。</summary>
    public static double RoundToHalfCeil(double value)
        => Math.Ceiling(value * 2) / 2;

    /// <summary>两判分差是否触发仲裁（严格大于阈值）。</summary>
    public static bool NeedsArbitration(double a, double b, double threshold)
        => Math.Abs(a - b) > threshold + 1e-9;
}
