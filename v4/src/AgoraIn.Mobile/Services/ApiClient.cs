using System.Net.Http.Json;
using System.Text.Json;

namespace AgoraIn.Mobile.Services;

/// <summary>
/// AgoraIn v4 服务端 API 客户端（JWT 鉴权）。
/// 服务器地址**已锁定**为官方域名（<see cref="ServerBaseUrl"/>），不允许连接第三方服务器。
/// </summary>
public sealed class ApiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>官方服务端基地址（唯一允许的服务器）。</summary>
    public const string ServerBaseUrl = "https://agorain.615mc.cn";

    private const string TokenKey = "agorain_token";
    private const string UserKey = "agorain_user";

    /// <summary>服务端基地址（只读，恒为官方域名）。</summary>
    public string BaseUrl => ServerBaseUrl;

    public string Token
    {
        get => Preferences.Default.Get(TokenKey, "");
        private set => Preferences.Default.Set(TokenKey, value);
    }

    public UserInfo? CurrentUser
    {
        get
        {
            var json = Preferences.Default.Get(UserKey, "");
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<UserInfo>(json, JsonOpts);
        }
        private set => Preferences.Default.Set(UserKey, value == null ? "" : JsonSerializer.Serialize(value, JsonOpts));
    }

    public bool IsLoggedIn => !string.IsNullOrEmpty(Token);

    // ── 认证 ──

    public async Task<UserInfo> LoginAsync(string username, string password)
    {
        var res = await PostAsync<LoginResponse>("/api/v4/auth/login", new { username, password });
        if (res == null || string.IsNullOrEmpty(res.Token))
            throw new InvalidOperationException("服务器未返回令牌");

        Token = res.Token;
        var user = new UserInfo { Username = res.Username, Role = res.Role };
        CurrentUser = user;
        return user;
    }

    public void Logout()
    {
        Token = "";
        CurrentUser = null;
    }

    // ── 学生端接口 ──

    /// <summary>打卡任务列表（含签到进度）。</summary>
    public Task<List<CheckInTaskItem>?> GetTasksAsync()
        => GetAsync<List<CheckInTaskItem>>("/api/v4/checkin/tasks");

    /// <summary>学生签到（扫码/输码）。</summary>
    public Task<ScanResult?> SubmitScanAsync(string code, string name, string password)
        => PostAsync<ScanResult>("/api/v4/checkin/scan", new { code, name, password });

    /// <summary>个人历史记录。</summary>
    public Task<List<CheckInRecordItem>?> GetHistoryAsync(string studentId)
        => GetAsync<List<CheckInRecordItem>>($"/api/v4/checkin/records?studentId={Uri.EscapeDataString(studentId)}");

    /// <summary>管理员仪表盘数据。</summary>
    public Task<DashboardData?> GetDashboardAsync()
        => GetAsync<DashboardData>("/api/v4/devices");

    /// <summary>班级列表。</summary>
    public Task<List<ClassItem>?> GetClassesAsync()
        => GetAsync<List<ClassItem>>("/api/v4/classes");

    // ── HTTP 基元 ──

    public async Task<T?> GetAsync<T>(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + url);
        AddAuth(req);
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return default;
        return await res.Content.ReadFromJsonAsync<T>(JsonOpts);
    }

    private async Task<T?> PostAsync<T>(string url, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + url)
        {
            Content = JsonContent.Create(body),
        };
        AddAuth(req);
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
        {
            var text = await res.Content.ReadAsStringAsync();
            throw new HttpRequestException($"{(int)res.StatusCode}: {text}");
        }
        return await res.Content.ReadFromJsonAsync<T>(JsonOpts);
    }

    /// <summary>POST multipart/form-data（文件上传用）。</summary>
    public async Task<T?> PostAsync<T>(string url, HttpContent content)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + url) { Content = content };
        AddAuth(req);
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
        {
            var text = await res.Content.ReadAsStringAsync();
            throw new HttpRequestException($"{(int)res.StatusCode}: {text}");
        }
        return await res.Content.ReadFromJsonAsync<T>(JsonOpts);
    }

    private void AddAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(Token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
    }
}

// ── DTO ──

public sealed class UserInfo
{
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
}

internal sealed class LoginResponse
{
    public string Token { get; set; } = "";
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
}

public sealed class CheckInTaskItem
{
    public string TaskId { get; set; } = "";
    public string Name { get; set; } = "";
    public int CheckedCount { get; set; }
    public int TotalCount { get; set; }
}

public sealed class ScanResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int? Rank { get; set; }
}

public sealed class CheckInRecordItem
{
    public string TaskId { get; set; } = "";
    public DateTime CheckedAt { get; set; }
    public string Source { get; set; } = "";
}

public sealed class DashboardData
{
    public int TotalDevices { get; set; }
    public int OnlineDevices { get; set; }
}

public sealed class ClassItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Grade { get; set; } = "";
}
