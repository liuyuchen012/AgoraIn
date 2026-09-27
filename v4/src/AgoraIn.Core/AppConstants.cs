namespace AgoraIn.Core;

/// <summary>
/// 全局常量：AgoraIn 服务端地址**硬编码锁定**，客户端不允许连接第三方服务器。
/// （产品决策：所有客户端统一连接到官方服务器，避免数据分散与私有部署不一致。）
/// </summary>
public static class AppConstants
{
    /// <summary>官方服务端主机名（唯一允许的服务器）。</summary>
    public const string ServerHost = "agorain.615mc.cn";

    /// <summary>官方服务端基地址（HTTPS）。</summary>
    public const string ServerBaseUrl = "https://agorain.615mc.cn";

    /// <summary>Web 管理面板地址（用于控制/教师模式内嵌）。</summary>
    public const string WebAdminUrl = "https://agorain.615mc.cn/";

    /// <summary>控制模式内嵌页面（仪表盘）。</summary>
    public const string WebAdminControlPath = "/";

    /// <summary>教师模式内嵌页面（学生/班级管理）。</summary>
    public const string WebAdminTeacherPath = "/students";

    /// <summary>应用版本号。</summary>
    public const string Version = "v4.0";
}
