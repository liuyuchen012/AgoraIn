using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using CheckIn.Client.Models;

namespace CheckIn.Client.Services;

/// <summary>
/// 远程打卡服务器控制服务：登录、仪表盘、设备、任务、考勤、用户管理
/// 使用 Bearer Token 认证，对应服务器 /api/auth/* 与 /api/mobile/* 接口
/// </summary>
public class RemoteControlService
{
    private readonly HttpClient _http;
    private string _baseUrl = "";
    private string _token = "";
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string BaseUrl => _baseUrl;
    public string? Token => string.IsNullOrEmpty(_token) ? null : _token;
    public bool IsLoggedIn => !string.IsNullOrEmpty(_token);

    public RemoteControlService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>设置服务器地址，格式如 http://192.168.1.100:5000</summary>
    public void SetBaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    private HttpRequestMessage MakeRequest(HttpMethod method, string path, object? body = null)
    {
        var req = new HttpRequestMessage(method, $"{_baseUrl}{path}");
        if (!string.IsNullOrEmpty(_token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        if (body != null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<JsonElement?> SendAsync(HttpRequestMessage req)
    {
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
        {
            string? errMsg = null;
            try
            {
                var err = await res.Content.ReadFromJsonAsync<JsonElement>();
                if (err.TryGetProperty("error", out var e))
                    errMsg = e.GetString();
            }
            catch { }
            throw new InvalidOperationException(errMsg ?? $"请求失败 ({res.StatusCode})");
        }
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ============ 认证 ============

    /// <summary>登录，成功保存 Token</summary>
    public async Task<RemoteUser> LoginAsync(string username, string password)
    {
        var res = await _http.PostAsJsonAsync($"{_baseUrl}/api/auth/login", new { username, password });
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        if (!res.IsSuccessStatusCode)
        {
            var err = json.TryGetProperty("error", out var e) ? e.GetString() : "登录失败";
            throw new InvalidOperationException(err ?? "登录失败");
        }
        _token = json.GetProperty("token").GetString() ?? "";
        return JsonSerializer.Deserialize<RemoteUser>(json.GetProperty("user").GetRawText(), _jsonOptions) ?? new RemoteUser();
    }

    public void Logout() => _token = "";

    // ============ 仪表盘 ============

    public async Task<DashboardResponse> GetDashboardAsync()
    {
        using var req = MakeRequest(HttpMethod.Get, "/api/mobile/dashboard");
        var json = await SendAsync(req);
        return JsonSerializer.Deserialize<DashboardResponse>(json?.GetRawText() ?? "{}", _jsonOptions) ?? new DashboardResponse();
    }

    // ============ 设备 ============

    public async Task<List<DeviceItem>> GetDevicesAsync()
    {
        using var req = MakeRequest(HttpMethod.Get, "/api/mobile/devices");
        var json = await SendAsync(req);
        return JsonSerializer.Deserialize<DeviceResponse>(json?.GetRawText() ?? "{}", _jsonOptions)?.Devices ?? new();
    }

    public async Task RenameDeviceAsync(string uuid, string name)
    {
        using var req = MakeRequest(HttpMethod.Put, $"/api/mobile/devices/{uuid}/rename", new { name });
        await SendAsync(req);
    }

    public async Task DeleteDeviceAsync(string uuid)
    {
        using var req = MakeRequest(HttpMethod.Delete, $"/api/mobile/devices/{uuid}");
        await SendAsync(req);
    }

    // ============ 呼叫 ============

    /// <summary>
    /// 向指定设备发送呼叫（prenotice 待下课时段通知 / emergency 上课应急通知 / summon 下课传唤）
    /// </summary>
    public async Task<int> SendCallAsync(string machineUuid, string type, string title, string message, int minutesBefore = 0, string? studentNames = null)
    {
        using var req = MakeRequest(HttpMethod.Post, "/api/mobile/calls", new
        {
            machine_uuid = machineUuid,
            type,
            title,
            message,
            minutes_before = minutesBefore,
            student_names = studentNames ?? ""
        });
        var json = await SendAsync(req);
        return json?.TryGetProperty("id", out var id) == true ? id.GetInt32() : 0;
    }

    // ============ 任务 ============

    public async Task<List<RemoteTask>> GetTasksAsync()
    {
        using var req = MakeRequest(HttpMethod.Get, "/api/mobile/tasks");
        var json = await SendAsync(req);
        return JsonSerializer.Deserialize<TaskListResponse>(json?.GetRawText() ?? "{}", _jsonOptions)?.Tasks ?? new();
    }

    public async Task CloseTaskAsync(int id)
    {
        using var req = MakeRequest(HttpMethod.Post, $"/api/mobile/tasks/{id}/close");
        await SendAsync(req);
    }

    public async Task DeleteTaskAsync(int id)
    {
        using var req = MakeRequest(HttpMethod.Delete, $"/api/mobile/tasks/{id}");
        await SendAsync(req);
    }

    public async Task RenameTaskAsync(int id, string name)
    {
        using var req = MakeRequest(HttpMethod.Put, $"/api/mobile/tasks/{id}/rename", new { name });
        await SendAsync(req);
    }

    // ============ 考勤 ============

    public async Task<List<AttendanceTask>> GetAttendanceAsync(string? machineUuid = null, string? taskId = null)
    {
        var path = "/api/mobile/attendance";
        var query = new List<string>();
        if (!string.IsNullOrEmpty(machineUuid)) query.Add($"machine_uuid={Uri.EscapeDataString(machineUuid)}");
        if (!string.IsNullOrEmpty(taskId)) query.Add($"task_id={Uri.EscapeDataString(taskId)}");
        if (query.Count > 0) path += "?" + string.Join("&", query);
        using var req = MakeRequest(HttpMethod.Get, path);
        var json = await SendAsync(req);
        return JsonSerializer.Deserialize<AttendanceResponse>(json?.GetRawText() ?? "{}", _jsonOptions)?.Tasks ?? new();
    }

    // ============ 签到历史 ============

    public async Task<HistoryResponse> GetHistoryAsync()
    {
        using var req = MakeRequest(HttpMethod.Get, "/api/mobile/students/history");
        var json = await SendAsync(req);
        return JsonSerializer.Deserialize<HistoryResponse>(json?.GetRawText() ?? "{}", _jsonOptions) ?? new HistoryResponse();
    }

    // ============ 用户管理 ============

    public async Task<List<RemoteUserItem>> GetUsersAsync()
    {
        using var req = MakeRequest(HttpMethod.Get, "/api/users");
        var json = await SendAsync(req);
        return JsonSerializer.Deserialize<List<RemoteUserItem>>(json?.GetRawText() ?? "[]", _jsonOptions) ?? new();
    }

    public async Task CreateUserAsync(string username, string password, string role, string displayName)
    {
        using var req = MakeRequest(HttpMethod.Post, "/api/users",
            new { username, password, role, display_name = displayName });
        await SendAsync(req);
    }

    public async Task UpdateUserAsync(int id, string? role = null, bool? isActive = null, string? displayName = null)
    {
        using var req = MakeRequest(HttpMethod.Put, $"/api/users/{id}",
            new { role, is_active = isActive, display_name = displayName });
        await SendAsync(req);
    }

    public async Task DeleteUserAsync(int id)
    {
        using var req = MakeRequest(HttpMethod.Delete, $"/api/users/{id}");
        await SendAsync(req);
    }

    public async Task ChangePasswordAsync(string oldPassword, string newPassword, int? userId = null)
    {
        using var req = MakeRequest(HttpMethod.Post, "/api/users/change-password",
            new { old_password = oldPassword, new_password = newPassword, user_id = userId });
        await SendAsync(req);
    }

    // ============ 排课管理 ============

    /// <summary>获取课时学生列表</summary>
    public async Task<List<JsonElement>> GetClassHourStudentsAsync(string machineUuid)
    {
        using var req = MakeRequest(HttpMethod.Get, $"/api/mobile/classhour-students?machine_uuid={Uri.EscapeDataString(machineUuid)}");
        var json = await SendAsync(req);
        var students = new List<JsonElement>();
        if (json?.TryGetProperty("students", out var arr) == true)
            foreach (var s in arr.EnumerateArray()) students.Add(s);
        return students;
    }

    /// <summary>添加课时学生</summary>
    public async Task<int> AddClassHourStudentAsync(string machineUuid, string name, double totalHours)
    {
        using var req = MakeRequest(HttpMethod.Post, "/api/mobile/classhour-students",
            new { machine_uuid = machineUuid, name, total_hours = totalHours });
        var json = await SendAsync(req);
        return json?.TryGetProperty("id", out var id) == true ? id.GetInt32() : 0;
    }

    /// <summary>删除课时学生</summary>
    public async Task DeleteClassHourStudentAsync(int id)
    {
        using var req = MakeRequest(HttpMethod.Delete, $"/api/mobile/classhour-students/{id}");
        await SendAsync(req);
    }

    /// <summary>调整课时（正数增加，负数扣减）</summary>
    public async Task<double> AdjustClassHoursAsync(int studentId, double hours, string remark = "")
    {
        using var req = MakeRequest(HttpMethod.Post, $"/api/mobile/classhour-students/{studentId}/adjust",
            new { hours, remark });
        var json = await SendAsync(req);
        return json?.TryGetProperty("remaining", out var r) == true ? r.GetDouble() : 0;
    }

    /// <summary>获取课时记录流水</summary>
    public async Task<List<JsonElement>> GetClassHourRecordsAsync(string machineUuid, int? studentId = null)
    {
        var path = $"/api/mobile/classhour-records?machine_uuid={Uri.EscapeDataString(machineUuid)}";
        if (studentId.HasValue) path += $"&student_id={studentId.Value}";
        using var req = MakeRequest(HttpMethod.Get, path);
        var json = await SendAsync(req);
        var records = new List<JsonElement>();
        if (json?.TryGetProperty("records", out var arr) == true)
            foreach (var r in arr.EnumerateArray()) records.Add(r);
        return records;
    }

    /// <summary>获取排课列表</summary>
    public async Task<List<JsonElement>> GetSchedulesAsync(string machineUuid, string? date = null)
    {
        var path = $"/api/mobile/schedules?machine_uuid={Uri.EscapeDataString(machineUuid)}";
        if (!string.IsNullOrEmpty(date)) path += $"&date={Uri.EscapeDataString(date)}";
        using var req = MakeRequest(HttpMethod.Get, path);
        var json = await SendAsync(req);
        var schedules = new List<JsonElement>();
        if (json?.TryGetProperty("schedules", out var arr) == true)
            foreach (var s in arr.EnumerateArray()) schedules.Add(s);
        return schedules;
    }

    /// <summary>创建排课</summary>
    public async Task<int> CreateScheduleAsync(string machineUuid, string date, int studentId, string studentName, string startTime, string endTime)
    {
        using var req = MakeRequest(HttpMethod.Post, "/api/mobile/schedules",
            new { machine_uuid = machineUuid, date, student_id = studentId, student_name = studentName, start_time = startTime, end_time = endTime });
        var json = await SendAsync(req);
        return json?.TryGetProperty("id", out var id) == true ? id.GetInt32() : 0;
    }

    /// <summary>删除排课</summary>
    public async Task DeleteScheduleAsync(int id)
    {
        using var req = MakeRequest(HttpMethod.Delete, $"/api/mobile/schedules/{id}");
        await SendAsync(req);
    }

    /// <summary>复制排课</summary>
    public async Task<int> CopySchedulesAsync(string machineUuid, string fromDate, List<string> toDates, bool skipOffDays = true)
    {
        using var req = MakeRequest(HttpMethod.Post, "/api/mobile/schedules/copy",
            new { machine_uuid = machineUuid, from_date = fromDate, to_dates = toDates, skip_off_days = skipOffDays });
        var json = await SendAsync(req);
        return json?.TryGetProperty("copied", out var c) == true ? c.GetInt32() : 0;
    }

    /// <summary>设置不排课日</summary>
    public async Task SetOffDayAsync(string machineUuid, string date, bool isOffDay)
    {
        using var req = MakeRequest(HttpMethod.Post, "/api/mobile/schedules/off-day",
            new { machine_uuid = machineUuid, date, is_off_day = isOffDay });
        await SendAsync(req);
    }

    /// <summary>获取月历排课数据</summary>
    public async Task<JsonElement?> GetScheduleCalendarAsync(string machineUuid, int year, int month)
    {
        using var req = MakeRequest(HttpMethod.Get, $"/api/mobile/schedules/calendar?machine_uuid={Uri.EscapeDataString(machineUuid)}&year={year}&month={month}");
        return await SendAsync(req);
    }

    /// <summary>获取课时设置</summary>
    public async Task<(double HoursPerHour, bool AutoDeduct)> GetClassHourSettingsAsync(string machineUuid)
    {
        using var req = MakeRequest(HttpMethod.Get, $"/api/mobile/classhour-settings?machine_uuid={Uri.EscapeDataString(machineUuid)}");
        var json = await SendAsync(req);
        var hph = json?.TryGetProperty("hours_per_hour", out var h) == true ? h.GetDouble() : 1.0;
        var ad = json?.TryGetProperty("auto_deduct", out var a) == true && a.GetBoolean();
        return (hph, ad);
    }

    /// <summary>更新课时设置</summary>
    public async Task UpdateClassHourSettingsAsync(string machineUuid, double hoursPerHour, bool autoDeduct)
    {
        using var req = MakeRequest(HttpMethod.Post, "/api/mobile/classhour-settings",
            new { machine_uuid = machineUuid, hours_per_hour = hoursPerHour, auto_deduct = autoDeduct });
        await SendAsync(req);
    }
}
