using AgoraIn.Core.Domain;
using AgoraIn.Core.Entities;
using AgoraIn.Core.SelfTest;

namespace AgoraIn.App.Services;

/// <summary>
/// 领域逻辑自测模块：课时划消 / 积分限额 / 值日轮换 / 点名落点四条核心链路的冒烟检查。
/// </summary>
public sealed class DomainSelfTestModule : ISelfTestModule
{
    public string Name => "domain";

    public IReadOnlyList<SelfTestItem> Run(CancellationToken cancellationToken = default)
    {
        var items = new List<SelfTestItem>();
        var date = new DateOnly(2026, 9, 29);

        // ── 课时：划消落账 + SlotKey 幂等 ──
        var account = new ClassHourAccount { StudentId = "s1", TotalHours = 20 };
        var record = ClassHourService.ApplyRecord(account, -1.5, date, "上课划消", ClassHourSource.Manual);
        items.Add(new SelfTestItem("课时：划消落账", record != null && record.Delta == -1.5 && Math.Abs(account.RemainingHours - 18.5) < 0.001));
        var slotKey = "2026-09-29|s1|19:00";
        var dup = ClassHourService.ApplyRecord(account, -1.5, date, "自动扣减", ClassHourSource.AutoSchedule,
            slotKey: slotKey, existingSlotKeys: [slotKey]);
        items.Add(new SelfTestItem("课时：SlotKey 幂等", dup == null));
        var gift = ClassHourService.ApplyRecord(account, 2, date, "赠送", ClassHourSource.Manual);
        items.Add(new SelfTestItem("课时：赠送落账", gift != null && Math.Abs(account.RemainingHours - 20.5) < 0.001));
        var auto = ClassHourService.ComputeAutoDeduct(
            ClassHourService.CreateScheduleEntry(date, "s1", new TimeOnly(19, 0), new TimeOnly(21, 0)), 1.5);
        items.Add(new SelfTestItem("课时：自动扣减 2h@1.5/h 返回 -3", Math.Abs(auto + 3) < 0.001));

        // ── 积分：每日上限封顶 ──
        var rule = new PointRule { Name = "课堂发言", DefaultDelta = 1, DailyCap = 2 };
        var today = PointService.EffectiveDelta(rule, 1, todayPositiveTotal: 1);
        var capped = PointService.EffectiveDelta(rule, 1, todayPositiveTotal: 2);
        var negative = PointService.EffectiveDelta(rule, -5, todayPositiveTotal: 0);
        items.Add(new SelfTestItem("积分：未达上限可加", today == 1));
        items.Add(new SelfTestItem("积分：达上限封顶为 0", capped == 0));
        items.Add(new SelfTestItem("积分：扣分不受上限影响", negative == -5));
        var pointRecord = PointService.CreateRecord("s1", 1, PointSource.Manual, "teacher", rule.Id);
        items.Add(new SelfTestItem("积分：CreateRecord 落字段", pointRecord.Delta == 1 && pointRecord.RuleId == rule.Id && pointRecord.Source == PointSource.Manual));

        // ── 值日：轮换生成 ──
        var posts = new List<DutyPost>
        {
            new() { Name = "扫地", Capacity = 1, SortOrder = 0 },
            new() { Name = "擦黑板", Capacity = 1, SortOrder = 1 },
        };
        var rotation = DutyRotationService.Generate(posts, ["a", "b", "c"], periodCount: 2);
        var period0 = rotation.Where(e => e.PeriodIndex == 0).Select(e => e.StudentId).ToList();
        var period1 = rotation.Where(e => e.PeriodIndex == 1).Select(e => e.StudentId).ToList();
        items.Add(new SelfTestItem("值日：每期岗位数正确", period0.Count == 2 && period1.Count == 2));
        items.Add(new SelfTestItem("值日：round-robin 轮转", !period0.SequenceEqual(period1)));

        // ── 点名：加权随机 + 顺序轮转 ──
        var candidates = new[]
        {
            new RollCallCandidate("just_called", Now(), 5),
            new RollCallCandidate("never_called", null, 0),
        };
        var weightedPicks = new HashSet<string>();
        var rng = new Random(2026);
        for (var i = 0; i < 100; i++)
        {
            var picked = RollCallPicker.PickWeighted(candidates, Now(), rng);
            if (picked != null) weightedPicks.Add(picked);
        }
        items.Add(new SelfTestItem("点名：加权随机人人可达", weightedPicks.Count == 2));
        var seq = new[] { new RollCallCandidate("a", null, 0), new RollCallCandidate("b", null, 0) };
        items.Add(new SelfTestItem("点名：顺序轮转按名单序", RollCallPicker.PickSequential(seq) == "a"));
        items.Add(new SelfTestItem("点名：空名单返回 null", RollCallPicker.PickSequential([]) == null));

        return items;

        static DateTime Now() => new(2026, 9, 29, 10, 0, 0);
    }
}
