using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgoraIn.Core.Licensing;

/// <summary>
/// 授权码编解码与校验（RSA-2048 / SHA-256 / PKCS#1 v1.5 离线签名）。
///
/// **格式与 v3.2 签发工具完全一致**（<c>Tools/LicenseTool</c>），存量激活码可继续使用：
/// <code>
///   AGRN-&lt;Base64( [u32 LE payloadLen][payload UTF8 JSON][RSA 签名] )&gt;   每 5 个字符插入 '-'
/// </code>
/// 公钥内嵌于 <see cref="RsaPublicKeyXml"/>（与 v3.2 生产公钥逐字相同）。
/// </summary>
public static class LicenseCodec
{
    /// <summary>授权码前缀。</summary>
    public const string Prefix = "AGRN-";

    /// <summary>生产公钥（RSA-2048，与 v3.2 一致；私钥仅存在于签发工具本地）。</summary>
    public const string RsaPublicKeyXml =
        "<RSAKeyValue><Modulus>sfTAweuTAZgl/d2hy6hX1BbOwiG4Uzuhj9r35tWXAeCOTln0K6XdqxaL0LXncOO4fKuydivqYPjC7jGF+ICAvm+4ExwJqbsGDw9nRHn3AfGLwm+VFBikusMIEagJso9DgTb2ShOOSoTkzDKODQFHfbZADzc95JpklqN4yhjeCBaJqE0y6/V1xrI/sk/Jmwk2VXOcOR0U7o9Dmza9WX5UWHR0zPfIOnsyqc513MwxEjQ/XFaGpjfAbOf78FnXbZV3tpbI8Ro+aBLo5Jm4YR4AmWy/MLY1Z9uSDQXti8wlRl1o5Rs1MokZ6gzlnXXIlmk1yOjgF82hUuyiSxB30HXYsQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 校验授权码的签名与完整性（不含指纹比对）。
    /// </summary>
    /// <exception cref="InvalidDataException">格式错误或签名无效。</exception>
    public static LicensePayload Decode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidDataException("激活码为空。");

        var normalized = Normalize(code);
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(normalized);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("激活码格式无效（Base64 解码失败）。", ex);
        }

        if (blob.Length < 8)
            throw new InvalidDataException("激活码内容不完整。");

        // 载荷长度（小端 u32）+ 载荷 + 签名
        var payloadLen = BitConverter.ToInt32(blob, 0);
        if (payloadLen <= 0 || payloadLen > blob.Length - 8)
            throw new InvalidDataException("激活码内容不完整（载荷长度异常）。");

        var payloadBytes = blob.AsSpan(4, payloadLen).ToArray();
        var signature = blob.AsSpan(4 + payloadLen).ToArray();

        if (!VerifySignature(payloadBytes, signature))
            throw new InvalidDataException("激活码签名校验失败（可能被篡改或不是本产品签发的激活码）。");

        var payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOpts)
                      ?? throw new InvalidDataException("激活码内容无法解析。");

        if (payload.Months < 1 || payload.Months > 600)
            throw new InvalidDataException($"激活码时长无效（{payload.Months} 个月）。");

        if (payload.MaxDevices < 1 || payload.MaxDevices > 65535)
            throw new InvalidDataException($"激活码设备数无效（{payload.MaxDevices}）。");

        return payload;
    }

    /// <summary>
    /// 校验授权码并比对硬件指纹。
    /// </summary>
    /// <param name="code">激活码。</param>
    /// <param name="expectedFingerprint">当前机器指纹；传 null 表示只校验签名。</param>
    /// <returns>校验通过的载荷；指纹不匹配时返回 null。</returns>
    /// <exception cref="InvalidDataException">格式错误或签名无效。</exception>
    public static LicensePayload? Verify(string code, string? expectedFingerprint)
    {
        var payload = Decode(code);

        if (!string.IsNullOrEmpty(expectedFingerprint)
            && !string.Equals(payload.Fingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return payload;
    }

    /// <summary>去除前缀、分隔符与空白，返回纯 Base64 串。</summary>
    public static string Normalize(string code)
    {
        var s = code.Trim();

        if (s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            s = s[Prefix.Length..];

        return s
            .Replace("-", "")
            .Replace(" ", "")
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();
    }

    private static bool VerifySignature(byte[] payload, byte[] signature)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.FromXmlString(RsaPublicKeyXml);
            return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
