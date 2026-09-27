using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgoraIn.Core.Licensing;

namespace AgoraIn.LicenseTool;

/// <summary>
/// AgoraIn 授权码签发工具（离线运行，私钥只保存在本机）。
///
/// 产物格式与 v3.2 的 <c>Tools/LicenseTool</c> **完全一致**，因此：
///  · 若你保留了 v3 的私钥文件（license_key.xml），可直接用它继续签发；
///  · 已签发的存量激活码在 v4 服务端依旧有效。
///
/// 命令：
///   keygen                          生成新密钥对（private/public xml + 服务端公钥常量）
///   fingerprint                     显示本机指纹（申请激活码时提供给厂商）
///   generate &lt;指纹&gt; &lt;月数&gt; &lt;设备数&gt; [--customer 客户] [--type server|region] [--serial 编号]
///   verify &lt;激活码&gt; [--fingerprint 指纹]    校验激活码（含可选指纹比对）
/// </summary>
internal static class Program
{
    private const string PrivateKeyFile = "license_private_key.xml";
    private const string PublicKeyFile = "license_public_key.xml";
    private const string Prefix = LicenseCodec.Prefix;

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "keygen" => KeyGen(),
                "fingerprint" or "fp" => ShowFingerprint(),
                "generate" or "gen" => Generate(args),
                "verify" => Verify(args),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 1;
        }
    }

    // ══════════════ keygen ══════════════

    private static int KeyGen()
    {
        if (File.Exists(PrivateKeyFile) &&
            !Confirm($"{PrivateKeyFile} 已存在，覆盖将导致旧激活码无法再签发新码。继续？"))
        {
            Console.WriteLine("已取消。");
            return 1;
        }

        using var rsa = RSA.Create(2048);
        File.WriteAllText(PrivateKeyFile, rsa.ToXmlString(true));
        File.WriteAllText(PublicKeyFile, rsa.ToXmlString(false));

        Console.WriteLine("密钥对已生成：");
        Console.WriteLine($"  私钥（务必妥善保管，不要提交到仓库）：{Path.GetFullPath(PrivateKeyFile)}");
        Console.WriteLine($"  公钥（可分发）：{Path.GetFullPath(PublicKeyFile)}");
        Console.WriteLine();
        Console.WriteLine("请把下面的公钥替换到服务端 AgoraIn.Core/Licensing/LicenseCodec.cs 的 RsaPublicKeyXml 常量：");
        Console.WriteLine();
        Console.WriteLine(rsa.ToXmlString(false));
        Console.WriteLine();
        Console.WriteLine("⚠️ 更换密钥后，之前签发的所有激活码将失效。");
        return 0;
    }

    // ══════════════ fingerprint ══════════════

    private static int ShowFingerprint()
    {
        var fp = MachineFingerprint.Compute();
        Console.WriteLine($"本机指纹：{fp}");
        Console.WriteLine($"主机名：  {Environment.MachineName}");
        Console.WriteLine($"系统：    {Environment.OSVersion}");
        Console.WriteLine();
        Console.WriteLine("请把「本机指纹」提供给厂商以获取激活码：");
        Console.WriteLine($"  dotnet run --project v4/tools/AgoraIn.LicenseTool -- generate {fp} 12 50 --customer \"学校名称\"");
        return 0;
    }

    // ══════════════ generate ══════════════

    private static int Generate(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("用法：generate <指纹> <月数> <设备数> [--customer 客户] [--type server|region] [--serial 编号]");
            return 1;
        }

        var fingerprint = args[1].Trim().ToUpperInvariant();
        if (fingerprint.Length != 16 || !fingerprint.All(Uri.IsHexDigit))
        {
            Console.Error.WriteLine("指纹格式不正确（应为 16 位十六进制，可由 fingerprint 命令获取）。");
            return 1;
        }

        if (!int.TryParse(args[2], out var months) || months is < 1 or > 600)
        {
            Console.Error.WriteLine("月数需为 1–600 的整数。");
            return 1;
        }

        if (!int.TryParse(args[3], out var maxDevices) || maxDevices is < 1 or > 65535)
        {
            Console.Error.WriteLine("设备数需为 1–65535 的整数。");
            return 1;
        }

        var (opts, ok) = ParseOptions(args, 4, ["--customer", "--type", "--serial"]);
        if (!ok) return 1;

        var type = opts.GetValueOrDefault("--type", "server");
        if (type is not ("server" or "region"))
        {
            Console.Error.WriteLine("--type 只能是 server 或 region。");
            return 1;
        }

        if (!File.Exists(PrivateKeyFile))
        {
            Console.Error.WriteLine($"未找到私钥 {Path.GetFullPath(PrivateKeyFile)}。" +
                                    "请先执行 keygen 生成，或把厂商私钥放到本目录。");
            return 1;
        }

        var payload = new LicensePayload
        {
            Fingerprint = fingerprint,
            Type = type,
            Months = months,
            MaxDevices = maxDevices,
            Customer = opts.GetValueOrDefault("--customer"),
            Serial = opts.GetValueOrDefault("--serial") ?? NewSerial(),
            IssuedAt = DateTime.UtcNow.ToString("O"),
            Version = 1,
        };

        var code = Sign(payload, File.ReadAllText(PrivateKeyFile));

        Console.WriteLine();
        Console.WriteLine($"客户：    {payload.Customer ?? "(未填写)"}");
        Console.WriteLine($"类型：    {payload.Type}");
        Console.WriteLine($"时长：    {payload.Months} 个月");
        Console.WriteLine($"设备上限：{payload.MaxDevices} 台");
        Console.WriteLine($"绑定指纹：{payload.Fingerprint}");
        Console.WriteLine($"流水号：  {payload.Serial}");
        Console.WriteLine();
        Console.WriteLine("激活码（发给客户，在 Web 管理面板「授权」页输入）：");
        Console.WriteLine();
        Console.WriteLine(code);
        Console.WriteLine();
        return 0;
    }

    // ══════════════ verify ══════════════

    private static int Verify(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("用法：verify <激活码> [--fingerprint 指纹]");
            return 1;
        }

        var (opts, _) = ParseOptions(args, 2, ["--fingerprint"]);
        var expected = opts.GetValueOrDefault("--fingerprint");

        try
        {
            var payload = LicenseCodec.Verify(args[1], expected);

            Console.WriteLine("签名校验：通过");
            Console.WriteLine($"  客户：    {payload!.Customer ?? "(未填写)"}");
            Console.WriteLine($"  类型：    {payload.Type}");
            Console.WriteLine($"  时长：    {payload.Months} 个月");
            Console.WriteLine($"  设备上限：{payload.MaxDevices} 台");
            Console.WriteLine($"  绑定指纹：{payload.Fingerprint}");
            Console.WriteLine($"  流水号：  {payload.Serial}");
            Console.WriteLine($"  签发时间：{payload.IssuedAt}");

            if (expected != null)
            {
                Console.WriteLine(payload.Fingerprint.Equals(expected, StringComparison.OrdinalIgnoreCase)
                    ? "指纹比对：匹配 ✓"
                    : "指纹比对：不匹配 ✗（该激活码不是为这台机器签发的）");
                return payload.Fingerprint.Equals(expected, StringComparison.OrdinalIgnoreCase) ? 0 : 2;
            }

            return 0;
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"校验失败：{ex.Message}");
            return 1;
        }
    }

    // ══════════════ 签名 ══════════════

    private static string Sign(LicensePayload payload, string privateKeyXml)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);

        using var rsa = RSA.Create();
        rsa.FromXmlString(privateKeyXml);
        var signature = rsa.SignData(json, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // [u32 LE payloadLen][payload][signature] → Base64 → 每 5 字符插 '-'
        var blob = new byte[4 + json.Length + signature.Length];
        BitConverter.TryWriteBytes(blob.AsSpan(0, 4), json.Length);
        json.CopyTo(blob.AsSpan(4));
        signature.CopyTo(blob.AsSpan(4 + json.Length));

        var base64 = Convert.ToBase64String(blob);
        var sb = new StringBuilder(Prefix);
        for (var i = 0; i < base64.Length; i++)
        {
            if (i > 0 && i % 5 == 0) sb.Append('-');
            sb.Append(base64[i]);
        }

        return sb.ToString();
    }

    private static string NewSerial()
    {
        var rand = RandomNumberGenerator.GetBytes(4);
        return $"SL{DateTime.Now:yyyyMMdd}-{Convert.ToHexString(rand)}";
    }

    // ══════════════ 辅助 ══════════════

    private static (Dictionary<string, string> Options, bool Ok) ParseOptions(
        string[] args, int start, string[] allowed)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = start; i < args.Length; i++)
        {
            var key = args[i];
            if (!allowed.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"未知参数：{key}（可用：{string.Join(" ", allowed)}）");
                return (result, false);
            }

            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine($"参数 {key} 缺少取值。");
                return (result, false);
            }

            result[key] = args[++i];
        }

        return (result, true);
    }

    private static bool Confirm(string message)
    {
        Console.Write($"{message} [y/N] ");
        return Console.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes";
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"未知命令：{cmd}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            AgoraIn 授权码签发工具

              keygen                                      生成新密钥对
              fingerprint                                 显示本机指纹
              generate <指纹> <月数> <设备数> [选项]       签发激活码
                  --customer "客户名称"
                  --type server|region                    默认 server
                  --serial SL20260927-XXXXXXXX            默认自动生成
              verify <激活码> [--fingerprint 指纹]         校验激活码

            示例：
              dotnet run --project v4/tools/AgoraIn.LicenseTool -- keygen
              dotnet run --project v4/tools/AgoraIn.LicenseTool -- generate 1A2B3C4D5E6F7A8B 12 50 --customer "阳光小学"
            """);
    }
}
