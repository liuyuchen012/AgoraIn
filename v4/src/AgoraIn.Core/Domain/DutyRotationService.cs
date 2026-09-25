using AgoraIn.Core.Entities;

namespace AgoraIn.Core.Domain;

/// <summary>
/// 值日轮换领域服务：按轮换顺序生成各期次的岗位安排（按周/按日轮换），生成后允许手工微调。
/// </summary>
public static class DutyRotationService
{
    /// <summary>轮换顺序中的一个槽位（岗位 + 岗位内序号）。</summary>
    public sealed record RotationSlot(string PostId, int SlotIndex);

    /// <summary>
    /// 展开轮换顺序：按岗位排序，每岗位占 Capacity 个连续槽位。
    /// 例如岗位 A(2 人) + B(1 人) → [A0, A1, B0]，依次消耗学生顺序表。
    /// </summary>
    public static IReadOnlyList<RotationSlot> BuildRotation(IReadOnlyList<DutyPost> posts)
    {
        ArgumentNullException.ThrowIfNull(posts);
        return posts
            .OrderBy(p => p.SortOrder)
            .SelectMany(p => Enumerable.Range(0, Math.Max(1, p.Capacity)).Select(s => (p, s)))
            .Select(t => new RotationSlot(t.p.Id, t.s))
            .ToList();
    }

    /// <summary>
    /// 生成 0..periodCount-1 各期次的值日安排（round-robin）：
    /// 第 period 期第 i 个槽位 = 学生顺序表[(period × 槽位总数 + i) % 学生数]。
    /// </summary>
    /// <param name="posts">岗位列表（按 SortOrder 排序后展开）。</param>
    /// <param name="studentIdsInOrder">学生轮换顺序（按学号/手动指定/随机生成的结果）。</param>
    /// <param name="periodCount">生成多少期。</param>
    public static IReadOnlyList<DutyRosterEntry> Generate(
        IReadOnlyList<DutyPost> posts,
        IReadOnlyList<string> studentIdsInOrder,
        int periodCount)
    {
        ArgumentNullException.ThrowIfNull(posts);
        ArgumentNullException.ThrowIfNull(studentIdsInOrder);
        if (studentIdsInOrder.Count == 0)
        {
            throw new ArgumentException("轮换学生顺序不能为空。", nameof(studentIdsInOrder));
        }

        if (posts.Count == 0)
        {
            return [];
        }

        var rotation = BuildRotation(posts);
        var result = new List<DutyRosterEntry>(rotation.Count * periodCount);
        for (var period = 0; period < periodCount; period++)
        {
            for (var i = 0; i < rotation.Count; i++)
            {
                var slot = rotation[i];
                result.Add(new DutyRosterEntry
                {
                    PostId = slot.PostId,
                    SlotIndex = slot.SlotIndex,
                    PeriodIndex = period,
                    StudentId = studentIdsInOrder[(period * rotation.Count + i) % studentIdsInOrder.Count],
                });
            }
        }

        return result;
    }

    /// <summary>
    /// 计算某日期所属的期次序号（0 起）：
    /// 按周轮换以 <paramref name="startDate"/> 所在周为第 0 期，按日轮换以起始日为第 0 期。
    /// </summary>
    public static int PeriodIndexOf(DutyCycle cycle, DateOnly startDate, DateOnly date)
        => cycle switch
        {
            DutyCycle.Daily => date.DayNumber - startDate.DayNumber,
            DutyCycle.Weekly => (int)Math.Floor((double)((date.DayNumber - startDate.DayNumber) / 7)),
            _ => throw new ArgumentOutOfRangeException(nameof(cycle)),
        };
}
