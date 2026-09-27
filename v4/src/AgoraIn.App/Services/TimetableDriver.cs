namespace AgoraIn.App.Services;

/// <summary>
/// 课表驱动行为：按当前课表判定上课/下课状态，驱动桌面端行为。
/// · 上课 → 切换大屏模式；下课/课间 → 切换控制模式
/// · 上课前 N 分钟触发点名提醒
/// · 下课后触发生成课时划消（事件回调，由外部落库）
/// 数据结构复用 <see cref="TimetableStore"/> 读写的 CSES 模型。
/// </summary>
public sealed class TimetableDriver : IDisposable
{
    private readonly string _baseDir;
    private readonly Timer _timer;
    private TimetableData _data = new();
    private bool? _lastIsClassTime;
    private readonly HashSet<string> _firedKeys = new(); // 去重：yyyy-MM-dd|类型|节次

    /// <summary>状态变化事件：参数为「是否上课中」。</summary>
    public event EventHandler<bool>? ClassStateChanged;

    /// <summary>上课前提醒事件：（节次序号, 节次名称, 距上课分钟数）。</summary>
    public event EventHandler<(int SlotIndex, string SlotName, int MinutesBefore)>? ClassStartingSoon;

    /// <summary>下课后事件：（节次序号, 开始时间, 结束时间），供课时划消联动。</summary>
    public event EventHandler<(int SlotIndex, TimeOnly Start, TimeOnly End)>? ClassEnded;

    /// <summary>是否启用课表驱动（默认关闭，由设置开关控制）。</summary>
    public bool Enabled { get; set; }

    /// <summary>上课前提醒提前量（分钟）。</summary>
    public int RemindMinutesBefore { get; set; } = 2;

    public TimetableDriver(string baseDir, TimeSpan? interval = null)
    {
        _baseDir = baseDir;
        Reload();
        _timer = new Timer(_ => Tick(), null,
            interval ?? TimeSpan.FromSeconds(30),
            interval ?? TimeSpan.FromSeconds(30));
    }

    /// <summary>重新加载课表数据（编辑课表后调用）。</summary>
    public void Reload() => _data = new TimetableStore(_baseDir).Load();

    /// <summary>判定当前是否处于上课时段。</summary>
    public bool IsClassTime(out int slotIndex, out TimeOnly start, out TimeOnly end, out string slotName)
    {
        slotIndex = -1;
        start = default;
        end = default;
        slotName = "";

        var plan = _data.ClassPlans.FirstOrDefault(p => p.IsActive) ?? _data.ClassPlans.FirstOrDefault();
        if (plan == null) return false;

        var layout = _data.TimeLayouts.FirstOrDefault(t => t.Id == plan.TimeLayoutId);
        if (layout == null) return false;

        var weekDay = WeekDayIndex();
        var now = TimeOnly.FromDateTime(DateTime.Now);

        foreach (var entry in layout.Entries.Where(e => !e.IsBreak))
        {
            if (!TimeOnly.TryParse(entry.StartTime, out var s) || !TimeOnly.TryParse(entry.EndTime, out var e))
                continue;

            // 仅当该节次今天有排课时才视为上课时段
            if (!plan.Entries.TryGetValue(weekDay * 100 + entry.Index, out var subjectId)
                || string.IsNullOrEmpty(subjectId))
                continue;

            if (now >= s && now < e)
            {
                slotIndex = entry.Index;
                start = s;
                end = e;
                slotName = entry.Name ?? $"第{entry.Index + 1}节";
                return true;
            }
        }

        return false;
    }

    private void Tick()
    {
        if (!Enabled) return;

        try
        {
            var isClass = IsClassTime(out _, out _, out _, out _);

            if (_lastIsClassTime != isClass)
            {
                _lastIsClassTime = isClass;
                ClassStateChanged?.Invoke(this, isClass);
            }

            CheckUpcoming();
            CheckJustEnded();
        }
        catch
        {
            // 课表数据异常不应导致应用崩溃
        }
    }

    private void CheckUpcoming()
    {
        if (RemindMinutesBefore <= 0) return;

        var (plan, layout) = ActivePlan();
        if (plan == null || layout == null) return;

        var weekDay = WeekDayIndex();
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var today = DateOnly.FromDateTime(DateTime.Now);

        foreach (var entry in layout.Entries.Where(e => !e.IsBreak))
        {
            if (!TimeOnly.TryParse(entry.StartTime, out var s)) continue;
            if (!plan.Entries.ContainsKey(weekDay * 100 + entry.Index)) continue;

            var minutesUntil = (s - now).TotalMinutes;
            if (minutesUntil <= 0 || minutesUntil > RemindMinutesBefore) continue;

            var key = $"{today:yyyy-MM-dd}|remind|{entry.Index}";
            if (!_firedKeys.Add(key)) continue;

            ClassStartingSoon?.Invoke(this, (entry.Index, entry.Name ?? $"第{entry.Index + 1}节", (int)Math.Ceiling(minutesUntil)));
        }
    }

    private void CheckJustEnded()
    {
        var (plan, layout) = ActivePlan();
        if (plan == null || layout == null) return;

        var weekDay = WeekDayIndex();
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var today = DateOnly.FromDateTime(DateTime.Now);

        foreach (var entry in layout.Entries.Where(e => !e.IsBreak))
        {
            if (!TimeOnly.TryParse(entry.StartTime, out var s) || !TimeOnly.TryParse(entry.EndTime, out var e))
                continue;
            if (!plan.Entries.ContainsKey(weekDay * 100 + entry.Index)) continue;

            var sinceEnd = (now - e).TotalMinutes;
            if (sinceEnd < 0 || sinceEnd >= 0.6) continue; // 刚结束 1 分钟内

            var key = $"{today:yyyy-MM-dd}|ended|{entry.Index}";
            if (!_firedKeys.Add(key)) continue;

            ClassEnded?.Invoke(this, (entry.Index, s, e));
        }
    }

    private (TtClassPlan? Plan, TtTimeLayout? Layout) ActivePlan()
    {
        var plan = _data.ClassPlans.FirstOrDefault(p => p.IsActive) ?? _data.ClassPlans.FirstOrDefault();
        if (plan == null) return (null, null);
        var layout = _data.TimeLayouts.FirstOrDefault(t => t.Id == plan.TimeLayoutId);
        return (plan, layout);
    }

    /// <summary>周几索引（周一=0 … 周日=6）。</summary>
    private static int WeekDayIndex() => ((int)DateTime.Now.DayOfWeek + 6) % 7;

    public void Dispose() => _timer.Dispose();
}
