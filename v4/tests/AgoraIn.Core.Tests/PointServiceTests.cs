using AgoraIn.Core.Domain;
using AgoraIn.Core.Entities;
using Xunit;

namespace AgoraIn.Core.Tests;

public class PointServiceTests
{
    [Fact]
    public void IsApplicable_全班级规则适用()
    {
        var rule = new PointRule { Enabled = true, ClassId = null };
        Assert.True(PointService.IsApplicable(rule, "class-1"));
    }

    [Fact]
    public void IsApplicable_限定班级不匹配返回false()
    {
        var rule = new PointRule { Enabled = true, ClassId = "class-1" };
        Assert.False(PointService.IsApplicable(rule, "class-2"));
    }

    [Fact]
    public void IsApplicable_禁用规则返回false()
    {
        var rule = new PointRule { Enabled = false, ClassId = null };
        Assert.False(PointService.IsApplicable(rule, "class-1"));
    }

    [Fact]
    public void EffectiveDelta_无限额返回请求值()
    {
        var rule = new PointRule { DailyCap = null };
        Assert.Equal(5, PointService.EffectiveDelta(rule, 5, 0));
    }

    [Fact]
    public void EffectiveDelta_限额范围内返回请求值()
    {
        var rule = new PointRule { DailyCap = 10 };
        Assert.Equal(5, PointService.EffectiveDelta(rule, 5, 3));
    }

    [Fact]
    public void EffectiveDelta_超出限额返回剩余()
    {
        var rule = new PointRule { DailyCap = 10 };
        Assert.Equal(2, PointService.EffectiveDelta(rule, 5, 8));
    }

    [Fact]
    public void EffectiveDelta_封顶返回零()
    {
        var rule = new PointRule { DailyCap = 10 };
        Assert.Equal(0, PointService.EffectiveDelta(rule, 5, 10));
    }

    [Fact]
    public void EffectiveDelta_负分不受限额约束()
    {
        var rule = new PointRule { DailyCap = 10 };
        Assert.Equal(-3, PointService.EffectiveDelta(rule, -3, 10));
    }

    [Fact]
    public void CreateRecord_字段赋值正确()
    {
        var r = PointService.CreateRecord("s1", 5, PointSource.RollCall, "系统",
            "r1", "答到加分", "rec-1");

        Assert.Equal("s1", r.StudentId);
        Assert.Equal(5, r.Delta);
        Assert.Equal(PointSource.RollCall, r.Source);
        Assert.Equal("系统", r.OperatorName);
        Assert.Equal("r1", r.RuleId);
        Assert.Equal("答到加分", r.Reason);
        Assert.Equal("rec-1", r.SourceId);
    }
}
