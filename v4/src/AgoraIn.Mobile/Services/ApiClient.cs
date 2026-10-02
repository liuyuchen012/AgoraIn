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

    /// <summary>邀请码 → 所属区域与机构自定义隐私协议（注册页展示，匿名接口）。</summary>
    public Task<InviteRegionResult?> GetInviteRegionAsync(string inviteCode)
        => GetAsync<InviteRegionResult?>($"/api/v4/parent/invite/{Uri.EscapeDataString(inviteCode)}/region");

    /// <summary>发送注册邮箱验证码。</summary>
    public Task<SendCodeResult?> SendRegisterCodeAsync(string email)
        => PostAsync<SendCodeResult>("/api/v4/account/send-code", new { email, purpose = "register" });

    /// <summary>
    /// 自助注册。mode=join 家长加入已有区域（regionId 必填）；
    /// mode=region 机构注册创建新区域（regionName 必填，注册后需主区域激活码激活）。
    /// </summary>
    public Task<RegisterResult?> RegisterAsync(
        string email, string code, string username, string password,
        string mode, string? regionId, string? regionName, string? inviteCode = null,
        bool agreeTerms = true, bool agreeRegionTerms = false)
        => PostAsync<RegisterResult>("/api/v4/account/register", new
        {
            email, code, username, password, mode, regionId, regionName, inviteCode,
            displayName = username, agreeTerms, agreeRegionTerms,
        });

    /// <summary>个人历史记录。</summary>
    public Task<List<CheckInRecordItem>?> GetHistoryAsync(string studentId)
        => GetAsync<List<CheckInRecordItem>>($"/api/v4/checkin/records?studentId={Uri.EscapeDataString(studentId)}");

    // ── 教师阅卷 / 出分 ──

    /// <summary>试卷列表。</summary>
    public Task<List<ExamPaperItem>?> GetPapersAsync()
        => GetAsync<List<ExamPaperItem>?>("/api/v4/exams/papers");

    /// <summary>某试卷的扫卡提交列表。</summary>
    public Task<List<SubmissionItem>?> GetSubmissionsAsync(string paperId)
        => GetAsync<List<SubmissionItem>?>($"/api/v4/exams/submissions?paperId={Uri.EscapeDataString(paperId)}");

    /// <summary>逐题结果（含题干/满分/识别答案/AI 建议分）。</summary>
    public Task<List<QuestionResultItem>?> GetSubmissionResultsAsync(string submissionId)
        => GetAsync<List<QuestionResultItem>?>($"/api/v4/exams/submissions/{submissionId}/results");

    /// <summary>答题卡扫描原图（人工复盘对照学生原卷）。失败返回 null（如未存图）。</summary>
    public async Task<ImageSource?> GetSubmissionImageAsync(string submissionId)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{BaseUrl}/api/v4/exams/submissions/{submissionId}/image");
            AddAuth(req);
            using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead);
            if (!res.IsSuccessStatusCode) return null;
            var bytes = await res.Content.ReadAsByteArrayAsync();
            return bytes.Length == 0 ? null : ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>教师人工改分（留痕，状态推进到待人工）。</summary>
    public Task<object?> OverrideResultAsync(string submissionId, string questionId, double score, string? comment)
        => PutAsync<object>($"/api/v4/exams/submissions/{submissionId}/results/{questionId}",
            new { score, comment });

    /// <summary>确认成绩（状态 → 已确认，计入统计并出分）。</summary>
    public Task<ConfirmResult?> ConfirmSubmissionAsync(string submissionId)
        => PostAsync<ConfirmResult>($"/api/v4/exams/submissions/{submissionId}/confirm", new { });

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

    private async Task<T?> PutAsync<T>(string url, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, BaseUrl + url)
        {
            Content = JsonContent.Create(body),
        };
        AddAuth(req);
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode) throw await FailAsync(res);
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
        if (!res.IsSuccessStatusCode) throw await FailAsync(res);
        return await res.Content.ReadFromJsonAsync<T>(JsonOpts);
    }

    /// <summary>POST multipart/form-data（文件上传用）。</summary>
    public async Task<T?> PostAsync<T>(string url, HttpContent content)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + url) { Content = content };
        AddAuth(req);
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode) throw await FailAsync(res);
        return await res.Content.ReadFromJsonAsync<T>(JsonOpts);
    }

    /// <summary>
    /// 把失败响应转成带 code 的 <see cref="ApiException"/>：调用方据此分支
    /// （如扫卡被退回时区分"没认出答题卡"还是"考号没涂"），并把服务端的中文
    /// 提示原样显示给老师，而不是抛一串 "400: {"error":"…"}"。
    /// </summary>
    private static async Task<ApiException> FailAsync(HttpResponseMessage res)
    {
        var status = (int)res.StatusCode;
        var text = await res.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(text);
            var error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            var code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
            if (!string.IsNullOrWhiteSpace(error)) return new ApiException(status, code, error!);
        }
        catch (JsonException) { /* 非 JSON 错误体：退回原文 */ }
        return new ApiException(status, null, string.IsNullOrWhiteSpace(text) ? $"HTTP {status}" : text);
    }

    private void AddAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(Token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
    }
}

/// <summary>服务端返回的业务错误（含稳定 code，便于调用方分支处理）。</summary>
public sealed class ApiException(int status, string? code, string message) : Exception(message)
{
    public int Status { get; } = status;

    /// <summary>服务端错误码，如 sheet_not_recognized / student_ref_missing；旧接口可能为 null。</summary>
    public string? Code { get; } = code;
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

public sealed class ExamPaperItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Subject { get; set; }
    public double TotalScore { get; set; }
    public int QuestionCount { get; set; }
    public int SubmissionCount { get; set; }
}

public sealed class SubmissionItem
{
    public string Id { get; set; } = "";
    public string? StudentId { get; set; }
    public string? StudentName { get; set; }
    public string? StudentRef { get; set; }
    public int Status { get; set; }
    public double? TotalScore { get; set; }
    public string? SubmittedAt { get; set; }
}

public sealed class QuestionResultItem
{
    public string QuestionId { get; set; } = "";
    public int Index { get; set; }
    public int Type { get; set; }
    public string? Content { get; set; }
    public string? StandardAnswer { get; set; }
    public double FullScore { get; set; }
    public string? RecognizedAnswer { get; set; }
    public double? Score { get; set; }
    public string? Comment { get; set; }
    public double? Confidence { get; set; }
    public string? Source { get; set; }
}

public sealed class ConfirmResult
{
    public double TotalScore { get; set; }
    public string? Status { get; set; }
}

public sealed class InviteRegionResult
{
    public string RegionId { get; set; } = "";
    public string RegionName { get; set; } = "";
    public bool HasCustom { get; set; }
    public string? CustomPrivacy { get; set; }
    public string? CustomPrivacyUpdatedAt { get; set; }
}

public sealed class SendCodeResult
{
    public string? Message { get; set; }
}

public sealed class RegisterResult
{
    public string? Message { get; set; }
    public string? Username { get; set; }
    public string? RegionId { get; set; }
    public string? LoginName { get; set; }
    public string? Role { get; set; }
    public string? Error { get; set; }
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
