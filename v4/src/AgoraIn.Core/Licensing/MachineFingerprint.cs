using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace AgoraIn.Core.Licensing;

/// <summary>
/// 机器指纹：与 v3.2 完全相同的算法，保证存量激活码可继续绑定同一台机器。
/// <code>fingerprint = SHA256("mac|hostname|os")[..16].ToUpperInvariant()</code>
///
/// 注意：多网卡、容器、系统升级都可能导致指纹漂移，因此 v4 的做法是
/// **首次激活时把指纹写入数据库并固定**，后续比对以库中值为准，避免因硬件变化突然失效。
/// </summary>
public static class MachineFingerprint
{
    /// <summary>计算当前机器指纹（大写 16 位 hex）。</summary>
    public static string Compute()
    {
        var raw = $"{PrimaryMacAddress()}|{Environment.MachineName}|{Environment.OSVersion}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToUpperInvariant();
    }

    /// <summary>取第一个已启用且非回环网卡的物理地址（12 位 hex，无分隔符）。</summary>
    public static string PrimaryMacAddress()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var mac = nic.GetPhysicalAddress().ToString();
                if (!string.IsNullOrEmpty(mac) && mac.Length >= 12)
                {
                    return mac[..12].ToUpperInvariant();
                }
            }
        }
        catch (NetworkInformationException)
        {
            // 无网络接口信息时回退到空值，仍能产生稳定指纹
        }

        return "000000000000";
    }
}
