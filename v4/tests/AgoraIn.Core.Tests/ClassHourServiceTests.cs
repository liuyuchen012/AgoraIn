using AgoraIn.Core.Domain;
using AgoraIn.Core.Entities;
using Xunit;

namespace AgoraIn.Core.Tests;

public class ClassHourServiceTests
{
    [Fact]
    public void ApplyRecord_赠送课时TotalHours增加()
    {
        var account = new ClassHourAccount { StudentId = "s1", TotalHours = 10, UsedHours = 0 };

        var record = ClassHourService.ApplyRecord(account, 5, new DateOnly(2026, 1, 1), "赠送", ClassHourSource.Manual);

        Assert.NotNull(record);
        Assert.Equal(15, account.TotalHours);
        Assert.Equal(0, account.UsedHours);
        Assert.Equal(5, record!.Delta);
    }

    [Fact]
    public void ApplyRecord_划消课时UsedHours增加()
    {
        var account = new ClassHourAccount { StudentId = "s1", TotalHours = 10, UsedHours = 0 };

        var record = ClassHourService.ApplyRecord(account, -3, new DateOnly(2026, 1, 1), "划消", ClassHourSource.Manual);

        Assert.NotNull(record);
        Assert.Equal(10, account.TotalHours);
        Assert.Equal(3, account.UsedHours);
        Assert.Equal(-3, record!.Delta);
    }

    [Fact]
    public void ApplyRecord_SlotKey幂等命中返回null账户不变()
    {
        var account = new ClassHourAccount { StudentId = "s1", TotalHours = 10, UsedHours = 0 };
        var existing = new HashSet<string> { "2026-01-01|s1|08:00" };

        var record = ClassHourService.ApplyRecord(account, -2, new DateOnly(2026, 1, 1), "自动", ClassHourSource.AutoSchedule,
            slotKey: "2026-01-01|s1|08:00", existingSlotKeys: existing);

        Assert.Null(record);
        Assert.Equal(10, account.TotalHours);
        Assert.Equal(0, account.UsedHours);
    }

    [Fact]
    public void ApplyRecord_delta为零抛出参数异常()
    {
        var account = new ClassHourAccount { StudentId = "s1" };
        Assert.Throws<ArgumentException>(() =>
            ClassHourService.ApplyRecord(account, 0, new DateOnly(2026, 1, 1), "", ClassHourSource.Manual));
    }

    [Fact]
    public void CreateScheduleEntry_下课等于上课抛异常()
    {
        Assert.Throws<ArgumentException>(() =>
            ClassHourService.CreateScheduleEntry(new DateOnly(2026, 1, 1), "s1",
                new TimeOnly(8, 0), new TimeOnly(8, 0)));
    }

    [Fact]
    public void CreateScheduleEntry_跨天课时CrossesMidnight为true()
    {
        var entry = ClassHourService.CreateScheduleEntry(
            new DateOnly(2026, 1, 1), "s1", new TimeOnly(23, 0), new TimeOnly(1, 0));

        Assert.True(entry.CrossesMidnight);
        Assert.Equal(new DateTime(2026, 1, 2), entry.EndDateTime.Date);
        Assert.Equal(2.0, entry.DurationHours, 1);
    }

    [Fact]
    public void CreateScheduleEntry_正常课时CrossesMidnight为false()
    {
        var entry = ClassHourService.CreateScheduleEntry(
            new DateOnly(2026, 1, 1), "s1", new TimeOnly(8, 0), new TimeOnly(9, 30));

        Assert.False(entry.CrossesMidnight);
        Assert.Equal(1.5, entry.DurationHours, 1);
    }

    [Fact]
    public void ComputeAutoDeduct_正常扣减()
    {
        var entry = ClassHourService.CreateScheduleEntry(
            new DateOnly(2026, 1, 1), "s1", new TimeOnly(8, 0), new TimeOnly(9, 30));
        var delta = ClassHourService.ComputeAutoDeduct(entry, 1.0);
        Assert.Equal(-1.5, delta, 2);
    }

    [Fact]
    public void ComputeAutoDeduct_超出范围抛参数异常()
    {
        var entry = ClassHourService.CreateScheduleEntry(
            new DateOnly(2026, 1, 1), "s1", new TimeOnly(8, 0), new TimeOnly(9, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassHourService.ComputeAutoDeduct(entry, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassHourService.ComputeAutoDeduct(entry, 25));
    }

    [Fact]
    public void BuildSlotKey_格式正确()
    {
        var key = ClassHourService.BuildSlotKey(new DateOnly(2026, 1, 1), "s1", new TimeOnly(8, 0));
        Assert.Equal("2026-01-01|s1|08:00", key);
    }
}
