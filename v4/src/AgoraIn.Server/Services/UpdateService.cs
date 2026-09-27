using System.Text.Json;
using AgoraIn.Core;

namespace AgoraIn.Server.Services;

/// <summary>GitHub Release 信息（供更新检查使用）。</summary>
public sealed class ReleaseInfo
{
    public string Tag { get; set; } = "";
    public string Version { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime? PublishedAt { get; set; }
    public List<ReleaseAsset> Assets { get; set; } = [];

    public bool IsNewerThan(string current)
        => VersionComparer.Compare(Version, current) > 0;
}

public sealed class ReleaseAsset
{
    public string Name { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public long Size { get; set; }
}

/// <summary>三段式版本号比较（兼容带/不带 v 前缀与不同段数）。</summary>
public static class VersionComparer
{
    public static int Compare(string? a, string? b)
    {
        var va = Parse(a);
        var vb = Parse(b);
        for (var i = 0; i < 4; i++)
        {
            var diff = va[i].CompareTo(vb[i]);
            if (diff != 0) return diff;
        }

        return 0;
    }

    private static int[] Parse(string? v)
    {
        var result = new int[4];
        if (string.IsNullOrWhiteSpace(v)) return result;

        var s = v.Trim().TrimStart('v', 'V');
        // 去掉预发布后缀（如 4.0.0-beta.1 → 4.0.0）
        var dash = s.IndexOfAny(['-', '+']);
        if (dash > 0) s = s[..dash];

        var parts = s.Split('.');
        for (var i = 0; i < Math.Min(parts.Length, 4); i++)
        {
            _ = int.TryParse(parts[i], out result[i]);
        }

        return result;
    }
}

/// <summary>
/// 更新检查服务：查询 GitHub Releases 并缓存（默认 30 分钟），
/// 供客户端检查更新（v3.2 的 /api/client_update）与服务端自更新提示使用。
///
/// 相比 v3.2 的改进：
///  · 资产名同时匹配 v4 与 v3 命名（AgoraIn-win-x64.zip / AgoraIn-Setup-v*.exe / Client.win-x64.zip），
///    避免 v3 客户端在 v4 服务端上"查不到更新"；
///  · 明确区分「服务器说没有更新」与「查询失败」（v3 两者都返回 has_update=false，导致客户端误判）；
///  · 缓存 last_checked，避免每次请求都打 GitHub（30 分钟 TTL，失败也有 5 分钟退避）。
/// </summary>
public sealed class UpdateService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<UpdateService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private ReleaseInfo? _cached;
    private DateTime _lastChecked = DateTime.MinValue;
    private string? _lastError;

    private static readonly TimeSpan SuccessTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(5);

    /// <summary>产品版本（服务端自身版本，取自编译期注入的程序集版本）。</summary>
    public static string ServerVersion { get; } =
        typeof(AppConstants).Assembly.GetName().Version is { } v
            ? $"v{v.Major}.{v.Minor}.{v.Build}"
            : "v4.0.0";

    /// <summary>仓库地址（用于发布页跳转与 GitHub API）。</summary>
    public const string GitHubRepo = "liuyuchen012/AgoraIn";

    /// <summary>发布页地址。</summary>
    public static string ReleasesPage => $"https://github.com/{GitHubRepo}/releases";

    public UpdateService(IHttpClientFactory httpFactory, ILogger<UpdateService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>最近一次查询的错误（null = 正常）。</summary>
    public string? LastError => _lastError;

    /// <summary>最近一次成功查询的时间。</summary>
    public DateTime? LastChecked => _lastChecked == DateTime.MinValue ? null : _lastChecked;

    /// <summary>获取最新 Release（带缓存；返回 null 表示查询失败）。</summary>
    public async Task<ReleaseInfo?> GetLatestReleaseAsync(bool force = false, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var ttl = _lastError == null ? SuccessTtl : FailureTtl;
            if (!force && _cached != null && DateTime.Now - _lastChecked < ttl)
                return _cached;

            var info = await FetchFromGitHubAsync(ct);
            _lastChecked = DateTime.Now;

            if (info == null)
            {
                _lastError ??= "无法访问更新服务器（可能是网络受限）";
                return _cached; // 保留上次成功结果，避免网络抖动导致"没有更新"
            }

            _cached = info;
            _lastError = null;
            return info;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<ReleaseInfo?> FetchFromGitHubAsync(CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(12);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AgoraIn-Server");

            var json = await http.GetStringAsync($"https://api.github.com/repos/{GitHubRepo}/releases/latest", ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var info = new ReleaseInfo
            {
                Tag = tag,
                Version = tag.StartsWith('v') ? tag : "v" + tag,
                Notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                PublishedAt = root.TryGetProperty("published_at", out var p) && DateTime.TryParse(p.GetString(), out var dt)
                    ? dt
                    : null,
            };

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                    var size = a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var s) ? s : 0;
                    if (name.Length > 0) info.Assets.Add(new ReleaseAsset { Name = name, DownloadUrl = url, Size = size });
                }
            }

            return info;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询 GitHub 最新版本失败");
            return null;
        }
    }

    /// <summary>
    /// 从 Release 资产中挑选适合指定平台的下载地址。
    /// 优先级：安装包 → 平台 zip（v4 命名）→ v3 旧命名（向后兼容）。
    /// </summary>
    public static string? PickDownloadUrl(ReleaseInfo release, string platform, string tag)
    {
        var candidates = platform.ToLowerInvariant() switch
        {
            "windows" or "win-x64" =>
                new[] { $"AgoraIn-Setup-{tag}.exe", $"{tag}.exe", "AgoraIn-win-x64.zip", "Client.win-x64.zip" },
            "linux" or "linux-x64" =>
                new[] { "AgoraIn-linux-x64.zip", "Server.linux-x64.zip" },
            "linux-arm64" =>
                new[] { "AgoraIn-linux-arm64.zip" },
            "macos" or "osx-arm64" =>
                new[] { "AgoraIn-osx-arm64.zip" },
            "osx-x64" =>
                new[] { "AgoraIn-osx-x64.zip" },
            "android" =>
                new[] { "AgoraIn-Android.apk" },
            _ => new[] { $"AgoraIn-{platform}.zip" },
        };

        foreach (var wanted in candidates)
        {
            var hit = release.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, wanted, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit.DownloadUrl;
        }

        return null;
    }
}
