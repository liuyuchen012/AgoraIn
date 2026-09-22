using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// =============================================================================
// AgoraIn 离线激活码签发工具
// 功能：RSA-2048 离线签名，生成服务器软件激活码
// 用法：
//   LicenseTool generate <fingerprint> <months> <maxDevices> [--customer "姓名"] [--type server|region]
//   LicenseTool verify <code>
//   LicenseTool keygen [--export-public path]
//   LicenseTool fingerprint (显示当前机器指纹)
// =============================================================================

const string KeyFile = "license_key.xml";
const string PublicKeyFile = "license_public_key.xml";
const string CodePrefix = "AGRN-";

// ---- 加载或生成 RSA 密钥对 ----
RSA rsa = RSA.Create(2048);
if (File.Exists(KeyFile))
{
    rsa.FromXmlString(File.ReadAllText(KeyFile));
    Console.WriteLine($"[密钥] 已加载密钥对：{KeyFile}");
}
else
{
    File.WriteAllText(KeyFile, rsa.ToXmlString(true));
    File.WriteAllText(PublicKeyFile, rsa.ToXmlString(false));
    Console.WriteLine($"[密钥] 已生成新密钥对：{KeyFile}（私钥）+ {PublicKeyFile}（公钥）");
    Console.WriteLine("[重要] 请妥善保管私钥文件，切勿泄露！");
}

if (args.Length < 1)
{
    PrintUsage();
    return;
}

var command = args[0].ToLowerInvariant();
switch (command)
{
    case "generate" when args.Length >= 4:
        GenerateCode(args);
        break;
    case "verify" when args.Length >= 2:
        VerifyCode(args[1]);
        break;
    case "keygen":
        RegenerateKey(args);
        break;
    case "fingerprint":
        ShowFingerprint();
        break;
    default:
        PrintUsage();
        break;
}

void PrintUsage()
{
    Console.WriteLine(@"
AgoraIn 离线激活码签发工具
==========================
用法:
  LicenseTool generate <fingerprint> <months> <maxDevices> [选项]
  LicenseTool verify <code>
  LicenseTool keygen [--export-public path]
  LicenseTool fingerprint

参数:
  fingerprint   服务器硬件指纹（由服务端 /api/server/fingerprint 获取）
  months        授权时长（月）
  maxDevices    设备数量上限

选项:
  --customer ""客户名""   客户名称（可选）
  --type server|region   激活类型：server=服务器软件授权，region=区域授权（默认 server）
  --serial ""序号""        自定义序号（可选，默认自动生成）

示例:
  LicenseTool generate ABC123FINGERPRINT 12 50 --customer ""某某学校""
  LicenseTool verify AGRN-XXXXX-XXXXX-XXXXX
  LicenseTool keygen --export-public public_key.xml
");
}

void GenerateCode(string[] args)
{
    var fingerprint = args[1];
    if (!int.TryParse(args[2], out var months) || months < 1 || months > 600)
    {
        Console.WriteLine("[错误] 时长必须在 1~600 个月之间");
        return;
    }
    if (!int.TryParse(args[3], out var maxDevices) || maxDevices < 1 || maxDevices > 65535)
    {
        Console.WriteLine("[错误] 设备数必须在 1~65535 之间");
        return;
    }

    // 解析可选参数
    var customer = GetOption(args, "--customer") ?? "";
    var licenseType = GetOption(args, "--type") ?? "server";
    var serial = GetOption(args, "--serial") ?? GenerateSerial();

    var payload = new Dictionary<string, object>
    {
        ["fingerprint"] = fingerprint.ToUpperInvariant(),
        ["type"] = licenseType,
        ["months"] = months,
        ["maxDevices"] = maxDevices,
        ["customer"] = customer,
        ["serial"] = serial,
        ["issuedAt"] = DateTime.UtcNow.ToString("O"),
        ["version"] = 1
    };

    var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
    var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

    // RSA-SHA256 签名
    var signature = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    // 拼接：payload长度(4字节) + payload + signature
    var codeBytes = new byte[4 + payloadBytes.Length + signature.Length];
    BitConverter.GetBytes(payloadBytes.Length).CopyTo(codeBytes, 0);
    payloadBytes.CopyTo(codeBytes, 4);
    signature.CopyTo(codeBytes, 4 + payloadBytes.Length);

    // Base64 编码，加分隔符使其易读
    var codeBase64 = Convert.ToBase64String(codeBytes);
    var code = CodePrefix + FormatCode(codeBase64);

    Console.WriteLine();
    Console.WriteLine("═══════════════════════════════════════════════════");
    Console.WriteLine("  AgoraIn 服务器软件激活码");
    Console.WriteLine("═══════════════════════════════════════════════════");
    Console.WriteLine($"  类型：{(licenseType == "server" ? "服务器软件授权" : "区域授权")}");
    Console.WriteLine($"  指纹：{fingerprint.ToUpperInvariant()}");
    Console.WriteLine($"  时长：{months} 个月");
    Console.WriteLine($"  设备：{maxDevices} 台");
    if (!string.IsNullOrEmpty(customer))
        Console.WriteLine($"  客户：{customer}");
    Console.WriteLine($"  序号：{serial}");
    Console.WriteLine($"  签发：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    Console.WriteLine("───────────────────────────────────────────────────");
    Console.WriteLine("  激活码：");
    Console.WriteLine($"  {code}");
    Console.WriteLine("═══════════════════════════════════════════════════");
    Console.WriteLine();
    Console.WriteLine("[提示] 将此激活码发送给客户，在服务端管理面板「授权管理」中输入即可激活。");
}

void VerifyCode(string codeStr)
{
    try
    {
        codeStr = codeStr.Trim();
        if (codeStr.StartsWith(CodePrefix))
            codeStr = codeStr[CodePrefix.Length..];

        var raw = codeStr.Replace("-", "").Replace(" ", "").Replace("\n", "").Replace("\r", "");
        var codeBytes = Convert.FromBase64String(raw);

        if (codeBytes.Length < 4)
        {
            Console.WriteLine("[错误] 激活码格式无效");
            return;
        }

        var payloadLen = BitConverter.ToInt32(codeBytes, 0);
        if (payloadLen < 1 || payloadLen > codeBytes.Length - 4)
        {
            Console.WriteLine("[错误] 激活码数据损坏");
            return;
        }

        var payloadBytes = codeBytes[4..(4 + payloadLen)];
        var signature = codeBytes[(4 + payloadLen)..];

        // 验证签名
        var valid = rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (!valid)
        {
            Console.WriteLine("[结果] ✗ 签名验证失败 — 激活码无效或被篡改");
            return;
        }

        var payloadJson = Encoding.UTF8.GetString(payloadBytes);
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson);

        Console.WriteLine("[结果] ✓ 签名验证通过");
        Console.WriteLine();
        if (payload != null)
        {
            Console.WriteLine("  激活码内容：");
            if (payload.TryGetValue("serial", out var s)) Console.WriteLine($"    序号：{s.GetString()}");
            if (payload.TryGetValue("type", out var t)) Console.WriteLine($"    类型：{(t.GetString() == "server" ? "服务器软件授权" : "区域授权")}");
            if (payload.TryGetValue("fingerprint", out var f)) Console.WriteLine($"    指纹：{f.GetString()}");
            if (payload.TryGetValue("months", out var m)) Console.WriteLine($"    时长：{m.GetInt32()} 个月");
            if (payload.TryGetValue("maxDevices", out var d)) Console.WriteLine($"    设备：{d.GetInt32()} 台");
            if (payload.TryGetValue("customer", out var c) && !string.IsNullOrEmpty(c.GetString())) Console.WriteLine($"    客户：{c.GetString()}");
            if (payload.TryGetValue("issuedAt", out var i)) Console.WriteLine($"    签发：{i.GetString()}");
        }
    }
    catch (FormatException)
    {
        Console.WriteLine("[错误] 激活码 Base64 编码无效");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[错误] 验证失败：{ex.Message}");
    }
}

void RegenerateKey(string[] args)
{
    rsa = RSA.Create(2048);
    File.WriteAllText(KeyFile, rsa.ToXmlString(true));

    var exportPath = GetOption(args, "--export-public");
    if (exportPath != null)
        File.WriteAllText(exportPath, rsa.ToXmlString(false));
    else
        File.WriteAllText(PublicKeyFile, rsa.ToXmlString(false));

    Console.WriteLine("[密钥] 已重新生成密钥对");
    Console.WriteLine("[重要] 旧激活码将全部失效，请确认后使用！");
}

void ShowFingerprint()
{
    Console.WriteLine("[提示] 硬件指纹由服务端生成，请在服务端运行以下命令获取：");
    Console.WriteLine("  curl http://localhost:5250/api/server/fingerprint");
    Console.WriteLine();
    Console.WriteLine("[测试指纹] 用于测试的示例指纹：");
    var testFp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("TEST_MACHINE")))[..16].ToUpperInvariant();
    Console.WriteLine($"  {testFp}");
}

string GenerateSerial()
{
    var bytes = RandomNumberGenerator.GetBytes(4);
    return $"SL{DateTime.Now:yyyyMMdd}-{Convert.ToHexString(bytes)}";
}

string FormatCode(string base64)
{
    // 每 5 字符加连字符，使其更易读
    var sb = new StringBuilder();
    for (int i = 0; i < base64.Length; i++)
    {
        if (i > 0 && i % 5 == 0) sb.Append('-');
        sb.Append(base64[i]);
    }
    return sb.ToString();
}

string? GetOption(string[] args, string option)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == option)
            return args[i + 1];
    }
    return null;
}
