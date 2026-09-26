using System.Text.Json;

namespace AgoraIn.App.Services;

/// <summary>
/// 应用全局配置持久化（data/app-config.json）。
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
    public string ServerIp { get; set; } = "";
    public int ServerPort { get; set; } = 5250;
    public string ServerPassword { get; set; } = "";
    public bool OnlineMode { get; set; }
    public string AdminPasswordHash { get; set; } = "";

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
