using System.Text.Json;

namespace AgoraIn.App.Services;

/// <summary>
/// 应用全局配置持久化（data/app-config.json）。
///
/// 注意：**服务端地址不再可配置**——所有客户端强制连接官方服务器
/// <see cref="Core.AppConstants.ServerHost"/>（产品决策，禁止使用第三方服务器）。
/// </summary>
public sealed class AppConfig
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private string _path = "";

    public int StartupMode { get; set; }
    public int ButtonRows { get; set; } = 6;
    public int ButtonCols { get; set; } = 6;
    public double HoursPerHour { get; set; } = 1;
    public bool AutoDeduct { get; set; }
    public bool OnlineMode { get; set; } = true;
    public string AdminPasswordHash { get; set; } = "";

    // 课表驱动行为（上下课自动切换模式 / 上课前点名提醒）
    public bool TimetableDriven { get; set; }
    public int RemindMinutesBefore { get; set; } = 2;

    /// <summary>服务端基地址（只读，恒为官方域名）。</summary>
    public static string ServerBaseUrl => Core.AppConstants.ServerBaseUrl;

    /// <summary>服务端主机名（只读）。</summary>
    public static string ServerHost => Core.AppConstants.ServerHost;

    public AppConfig(string basePath)
    {
        _path = Path.Combine(basePath, "data", "app-config.json");
    }

    public static AppConfig Load(string basePath)
    {
        var path = Path.Combine(basePath, "data", "app-config.json");
        if (File.Exists(path))
        {
            try
            {
                var text = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<AppConfig>(text, JsonOpts);
                if (cfg != null) { cfg._path = path; return cfg; }
            }
            catch { }
        }
        return new AppConfig(basePath);
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(this, JsonOpts));
    }
}
