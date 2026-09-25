using AgoraIn.Core.Entities;

namespace AgoraIn.Core.Domain;

/// <summary>
/// 课时划消领域服务：账户正负流水、SlotKey 幂等去重、排课时长自动扣减计算。
/// 纯函数实现，不持有状态；持久化由数据层完成。
/// </summary>
public static class ClassHourService
{
    /// <summary>
    /// 应用一条课时流水：更新账户并返回流水实体。
    /// 约定：Delta &gt; 0 = 赠送（增加 TotalHours），Delta &lt; 0 = 划消（增加 UsedHours）。
    /// </summary>
    /// <param name="account">学生课时账户（就地更新）。</param>
    /// <param name="delta">课时变化量（正 = 赠送，负 = 划消；0 拒绝）。</param>
    /// <param name="date">业务日期。</param>
    /// <param name="note">备注/原因。</param>
    /// <param name="source">来源。</param>
    /// <param name="slotKey">自动划消幂等键（手工为 null）。</param>
    /// <param name="existingSlotKeys">该学生已有的幂等键集合（用于去重判断）。</param>
    /// <returns>待持久化的流水；幂等命中（slotKey 已存在）返回 null，账户不变。</returns>
    public static ClassHourRecord? ApplyRecord(
        ClassHourAccount account,
        double delta,
        DateOnly date,
        string note,
        ClassHourSource source,
        string? slotKey = null,
        IReadOnlyCollection<string>? existingSlotKeys = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (Math.Abs(delta) < 1e-9)
        {
            throw new ArgumentException("课时变化量不能为 0。", nameof(delta));
        }

        // SlotKey 幂等：同一键只入账一次（多实例/重复轮询安全）
        if (!string.IsNullOrEmpty(slotKey) && existingSlotKeys?.Contains(slotKey) == true)
        {
            return null;
        }

        if (delta > 0)
        {
            account.TotalHours += delta;
        }
        else
        {
            account.UsedHours += -delta;
        }

        account.UpdatedAt = DateTime.Now;

        return new ClassHourRecord
        {
            StudentId = account.StudentId,
            Date = date,
            Delta = delta,
            Note = note,
            SlotKey = slotKey,
            Source = source,
            CreatedAt = DateTime.Now,
        };
    }

    /// <summary>
    /// 创建排课条目：校验起止时间并推导跨天标记（下课 ≤ 上课视为跨天，与 v3 语义一致）。
    /// </summary>
    public static CourseScheduleEntry CreateScheduleEntry(
        DateOnly date, string studentId, TimeOnly startTime, TimeOnly endTime, string note = "")
    {
        if (startTime == endTime)
        {
            throw new ArgumentException("下课时间不能等于上课时间（v3 重复排课拦截规则）。", nameof(endTime));
        }

        return new CourseScheduleEntry
        {
            Date = date,
            StudentId = studentId,
            StartTime = startTime,
            EndTime = endTime,
            CrossesMidnight = endTime < startTime,
            Note = note,
            CreatedAt = DateTime.Now,
        };
    }

    /// <summary>
    /// 计算某节排课的自动扣减课时：实际时长 × 每小时课时消耗（负数 = 划消）。
    /// </summary>
    /// <param name="entry">排课条目。</param>
    /// <param name="hoursPerHour">每小时消耗课时数（0–24，支持小数）。</param>
    public static double ComputeAutoDeduct(CourseScheduleEntry entry, double hoursPerHour)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (hoursPerHour is < 0 or > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(hoursPerHour), "每小时课时消耗须在 0–24 之间。");
        }

        return -entry.DurationHours * hoursPerHour;
    }

    /// <summary>
    /// 构造自动划消幂等键：yyyy-MM-dd|学生ID|上课时间（与 v3.2 SlotKey 规则一致）。
    /// </summary>
    public static string BuildSlotKey(DateOnly date, string studentId, TimeOnly startTime)
        => $"{date:yyyy-MM-dd}|{studentId}|{startTime:HH:mm}";
}
