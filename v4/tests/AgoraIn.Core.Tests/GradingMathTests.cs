using AgoraIn.Core.Domain;
using Xunit;

namespace AgoraIn.Core.Tests;

/// <summary>
/// 双判合分 / 仲裁触发规则。这些是直接写进成绩的数字，且有前科：
/// 早期实现带 epsilon 让 1.25 掉档成 1.0，故边界值全部锁进测试。
/// </summary>
public class GradingMathTests
{
    [Theory]
    [InlineData(1.48, 1.5)]   // 用户规则示例
    [InlineData(1.78, 2.0)]
    [InlineData(1.23, 1.5)]
    [InlineData(0.98, 1.0)]
    [InlineData(1.25, 1.5)]   // 曾是 bug 现场：进位到 1.0
    [InlineData(0.75, 1.0)]   // 生产实测：一判 0 + 二判 1.5 的平均
    [InlineData(2.75, 3.0)]
    public void 平均分向上进位到0点5(double average, double expected)
    {
        Assert.Equal(expected, GradingMath.RoundToHalfCeil(average));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.5)]
    [InlineData(13.0)]
    public void 已落在0点5边界上的分数不再变化(double value)
    {
        Assert.Equal(value, GradingMath.RoundToHalfCeil(value));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.1, 0.5)]
    [InlineData(0.49, 0.5)]
    [InlineData(0.51, 1.0)]
    public void 零分与极小分处理(double value, double expected)
    {
        Assert.Equal(expected, GradingMath.RoundToHalfCeil(value));
    }

    [Theory]
    [InlineData(5.0, 6.0, 5.5)]     // 平均值已是 0.5 的整数倍 → 不动
    [InlineData(5.0, 6.5, 6.0)]     // 5.75 → 进位
    [InlineData(3.0, 3.5, 3.5)]
    public void 两位教师分数取平均后进位(double a, double b, double expected)
    {
        Assert.Equal(expected, GradingMath.RoundToHalfCeil((a + b) / 2));
    }

    [Theory]
    [InlineData(0.0, 5.0)]
    [InlineData(2.5, 0.0)]      // 平均 1.25 → 1.5
    [InlineData(1.0, 0.0)]      // 平均 0.5 → 0.5（已是边界，不动）
    [InlineData(4.5, 4.5)]
    [InlineData(0.0, 0.5)]
    public void 合分只进位不下调且幅度不超过0点5(double a, double b)
    {
        var average = (a + b) / 2;
        var rounded = GradingMath.RoundToHalfCeil(average);
        Assert.True(rounded >= average - 1e-9, $"不得低于原始平均：{average} → {rounded}");
        Assert.True(rounded - average <= 0.5 + 1e-9, $"进位幅度不超过 0.5：{average} → {rounded}");
        Assert.Equal(0.0, Math.Round(rounded * 2) % 1, 9);   // 结果必是 0.5 的整数倍
    }

    [Theory]
    [InlineData(4.0, 5.0, 1.0, false)]    // 恰好等于阈值：不触发（严格大于）
    [InlineData(4.0, 5.5, 1.0, true)]
    [InlineData(0.0, 2.5, 1.0, true)]     // 生产实测：一判 0 + 二判 2.5 → 待仲裁
    [InlineData(3.0, 3.0, 0.0, false)]
    [InlineData(3.0, 3.5, 0.5, false)]
    [InlineData(3.0, 4.0, 0.5, true)]
    public void 分差超阈值才触发仲裁(double a, double b, double threshold, bool expected)
    {
        Assert.Equal(expected, GradingMath.NeedsArbitration(a, b, threshold));
        // 分差是对称的：谁先判不影响
        Assert.Equal(expected, GradingMath.NeedsArbitration(b, a, threshold));
    }
}
