namespace AgoraIn.Server;

/// <summary>
/// 服务端路径配置（由 Program 在启动时解析并注册为单例）。
/// 数据目录可用 appsettings.json 的 <c>Data:Directory</c> 或环境变量 <c>Data__Directory</c> 覆盖，
/// 默认 <c>&lt;ContentRoot&gt;/data</c>。
/// </summary>
public sealed class ServerPaths
{
    public ServerPaths(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(DataDirectory);

        ResourceDirectory = Path.Combine(DataDirectory, "resources");
        Directory.CreateDirectory(ResourceDirectory);

        SheetDirectory = Path.Combine(DataDirectory, "sheets");
        Directory.CreateDirectory(SheetDirectory);
    }

    /// <summary>数据目录（数据库、上传资源均位于其下）。</summary>
    public string DataDirectory { get; }

    /// <summary>上传资源目录。</summary>
    public string ResourceDirectory { get; }

    /// <summary>答题卡扫描原图目录（人工复盘需要对着学生原卷改分）。</summary>
    public string SheetDirectory { get; }

    /// <summary>数据库文件路径。</summary>
    public string DatabaseFile => Path.Combine(DataDirectory, "server.db");
}
