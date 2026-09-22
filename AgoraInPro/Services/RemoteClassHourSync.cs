using System.Text.Json;
using CheckIn.Client.Models;

namespace CheckIn.Client.Services;

/// <summary>
/// 课时数据远程同步服务：将本地 classhours.json 数据与集控服务器双向同步。
/// 用于控制中心连接集控平台后，自动或手动同步排课和课时数据。
/// </summary>
public class RemoteClassHourSync
{
    private readonly RemoteControlService _remote;
    private readonly ClassHourStore _store;
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RemoteClassHourSync(RemoteControlService remote)
    {
        _remote = remote;
        _store = new ClassHourStore();
    }

    /// <summary>
    /// 将本地数据推送到服务器（全量覆盖）。
    /// 需要已登录且有管理权限。
    /// </summary>
    /// <param name="machineUuid">目标设备 UUID</param>
    /// <returns>(成功数, 错误消息)</returns>
    public async Task<(int Synced, string? Error)> PushToServerAsync(string machineUuid)
    {
        if (!_remote.IsLoggedIn) return (0, "未连接集控平台");

        try
        {
            var data = _store.Load();
            int synced = 0;

            // 1. 同步学生列表
            var remoteStudents = await _remote.GetClassHourStudentsAsync(machineUuid);
            var remoteNameMap = new Dictionary<string, int>();
            foreach (var rs in remoteStudents)
            {
                var name = rs.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var id = rs.TryGetProperty("id", out var i) ? i.GetInt32() : 0;
                if (!string.IsNullOrEmpty(name) && id > 0)
                    remoteNameMap[name] = id;
            }

            // 创建本地有但服务器没有的学生
            foreach (var student in data.Students)
            {
                if (remoteNameMap.ContainsKey(student.Name)) continue;
                var newId = await _remote.AddClassHourStudentAsync(machineUuid, student.Name, student.TotalHours);
                if (newId > 0)
                {
                    remoteNameMap[student.Name] = newId;
                    synced++;
                }
            }

            // 2. 同步排课数据
            foreach (var kv in data.Schedule)
            {
                var date = kv.Key;
                foreach (var entry in kv.Value)
                {
                    var student = data.Students.FirstOrDefault(s => s.Id == entry.StudentId);
                    if (student == null) continue;

                    if (!remoteNameMap.TryGetValue(student.Name, out var remoteStudentId))
                        continue;

                    try
                    {
                        await _remote.CreateScheduleAsync(machineUuid, date, remoteStudentId, student.Name, entry.StartTime, entry.EndTime);
                        synced++;
                    }
                    catch
                    {
                        // 可能已存在，忽略
                    }
                }
            }

            // 3. 同步不排课日
            foreach (var offDay in data.OffDays)
            {
                try
                {
                    await _remote.SetOffDayAsync(machineUuid, offDay, true);
                    synced++;
                }
                catch { }
            }

            // 4. 同步设置
            await _remote.UpdateClassHourSettingsAsync(machineUuid, data.HoursPerHour, data.AutoDeduct);

            return (synced, null);
        }
        catch (Exception ex)
        {
            return (0, $"同步失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从服务器拉取数据并合并到本地。
    /// 服务器数据优先（排课和学生列表），本地课时记录保留。
    /// </summary>
    /// <param name="machineUuid">源设备 UUID</param>
    /// <returns>(合并数, 错误消息)</returns>
    public async Task<(int Merged, string? Error)> PullFromServerAsync(string machineUuid)
    {
        if (!_remote.IsLoggedIn) return (0, "未连接集控平台");

        try
        {
            var data = _store.Load();
            int merged = 0;

            // 1. 拉取学生列表
            var remoteStudents = await _remote.GetClassHourStudentsAsync(machineUuid);
            foreach (var rs in remoteStudents)
            {
                var name = rs.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var totalHours = rs.TryGetProperty("total_hours", out var th) ? th.GetDouble() : 0;
                var usedHours = rs.TryGetProperty("used_hours", out var uh) ? uh.GetDouble() : 0;

                if (string.IsNullOrEmpty(name)) continue;

                var existing = data.Students.FirstOrDefault(s => s.Name == name);
                if (existing == null)
                {
                    data.Students.Add(new ChStudent
                    {
                        Name = name,
                        TotalHours = totalHours,
                        UsedHours = usedHours
                    });
                    merged++;
                }
                else
                {
                    // 更新课时余额（服务器为准）
                    existing.TotalHours = totalHours;
                    existing.UsedHours = usedHours;
                }
            }

            // 2. 拉取排课数据（按月拉取当前月）
            var now = DateTime.Now;
            var calendar = await _remote.GetScheduleCalendarAsync(machineUuid, now.Year, now.Month);
            if (calendar?.TryGetProperty("schedule_counts", out var counts) == true)
            {
                foreach (var sc in counts.EnumerateArray())
                {
                    var date = sc.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(date)) continue;

                    var schedules = await _remote.GetSchedulesAsync(machineUuid, date);
                    var entries = new List<ScheduleEntry>();
                    foreach (var s in schedules)
                    {
                        var studentName = s.TryGetProperty("student_name", out var sn) ? sn.GetString() ?? "" : "";
                        var startTime = s.TryGetProperty("start_time", out var st) ? st.GetString() ?? "" : "";
                        var endTime = s.TryGetProperty("end_time", out var et) ? et.GetString() ?? "" : "";

                        var student = data.Students.FirstOrDefault(x => x.Name == studentName);
                        if (student != null)
                        {
                            entries.Add(new ScheduleEntry
                            {
                                StudentId = student.Id,
                                StartTime = startTime,
                                EndTime = endTime
                            });
                        }
                    }
                    if (entries.Count > 0)
                    {
                        data.Schedule[date] = entries;
                        merged++;
                    }
                }

                // 拉取不排课日
                if (calendar?.TryGetProperty("off_days", out var offDays) == true)
                {
                    foreach (var od in offDays.EnumerateArray())
                    {
                        var date = od.GetString() ?? "";
                        if (!string.IsNullOrEmpty(date) && !data.OffDays.Contains(date))
                        {
                            data.OffDays.Add(date);
                            merged++;
                        }
                    }
                }
            }

            // 3. 拉取设置
            var (hoursPerHour, autoDeduct) = await _remote.GetClassHourSettingsAsync(machineUuid);
            data.HoursPerHour = hoursPerHour;
            data.AutoDeduct = autoDeduct;

            // 保存到本地
            _store.Save(data);

            return (merged, null);
        }
        catch (Exception ex)
        {
            return (0, $"拉取失败: {ex.Message}");
        }
    }
}