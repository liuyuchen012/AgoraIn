using AgoraIn.Core.Domain;
using AgoraIn.Core.Entities;
using Xunit;

namespace AgoraIn.Core.Tests;

public class DutyRotationServiceTests
{
    private static List<DutyPost> TwoPosts => new()
    {
        new DutyPost { Id = "p1", Name = "扫地", Capacity = 2, SortOrder = 0 },
        new DutyPost { Id = "p2", Name = "擦黑板", Capacity = 1, SortOrder = 1 },
    };

    [Fact]
    public void BuildRotation_展开为3个槽位()
    {
        var slots = DutyRotationService.BuildRotation(TwoPosts);
        Assert.Equal(3, slots.Count);
        Assert.Equal("p1", slots[0].PostId);
        Assert.Equal(0, slots[0].SlotIndex);
        Assert.Equal("p1", slots[1].PostId);
        Assert.Equal(1, slots[1].SlotIndex);
        Assert.Equal("p2", slots[2].PostId);
        Assert.Equal(0, slots[2].SlotIndex);
    }

    [Fact]
    public void Generate_按日轮换_round_robin分配()
    {
        var students = new List<string> { "张三", "李四", "王五", "赵六" };
        var entries = DutyRotationService.Generate(TwoPosts, students, 1);

        // 3 slots × 1 period = 3 条
        Assert.Equal(3, entries.Count);
        Assert.Equal("张三", entries[0].StudentId);
        Assert.Equal("李四", entries[1].StudentId);
        Assert.Equal("王五", entries[2].StudentId);
    }

    [Fact]
    public void Generate_2期_第二期接续轮转()
    {
        var students = new List<string> { "张三", "李四", "王五", "赵六" };
        var entries = DutyRotationService.Generate(TwoPosts, students, 2);

        Assert.Equal(6, entries.Count);
        // 第 2 期（period=1）：[赵六, 张三, 李四]
        var period2 = entries.Where(e => e.PeriodIndex == 1).ToList();
        Assert.Equal("赵六", period2[0].StudentId);
        Assert.Equal("张三", period2[1].StudentId);
        Assert.Equal("李四", period2[2].StudentId);
    }

    [Fact]
    public void PeriodIndexOf_按日轮换()
    {
        var start = new DateOnly(2026, 9, 1); // 周二
        Assert.Equal(0, DutyRotationService.PeriodIndexOf(DutyCycle.Daily, start, new DateOnly(2026, 9, 1)));
        Assert.Equal(6, DutyRotationService.PeriodIndexOf(DutyCycle.Daily, start, new DateOnly(2026, 9, 7)));
    }

    [Fact]
    public void PeriodIndexOf_按周轮换()
    {
        var start = new DateOnly(2026, 9, 1);
        Assert.Equal(0, DutyRotationService.PeriodIndexOf(DutyCycle.Weekly, start, new DateOnly(2026, 9, 5)));
        Assert.Equal(0, DutyRotationService.PeriodIndexOf(DutyCycle.Weekly, start, new DateOnly(2026, 9, 7)));
        Assert.Equal(1, DutyRotationService.PeriodIndexOf(DutyCycle.Weekly, start, new DateOnly(2026, 9, 8)));
    }

    [Fact]
    public void Generate_空岗位返回空()
    {
        Assert.Empty(DutyRotationService.Generate([], new List<string> { "张三" }, 1));
    }

    [Fact]
    public void Generate_空学生列表抛异常()
    {
        Assert.Throws<ArgumentException>(() => DutyRotationService.Generate(TwoPosts, [], 1));
    }
}
