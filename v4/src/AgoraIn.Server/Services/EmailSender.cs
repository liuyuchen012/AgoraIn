using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AgoraIn.Server.Services;

/// <summary>SMTP 配置（存于数据库单行表，可由管理员在 Web 面板维护）。</summary>
public sealed class SmtpSettings
{
    public int Id { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 465;

    /// <summary>登录账号（留空则匿名发信）。</summary>
    public string User { get; set; } = "";

    /// <summary>登录密码（API 返回时会脱敏）。</summary>
    public string Password { get; set; } = "";

    /// <summary>发件人地址。</summary>
    public string From { get; set; } = "";

    /// <summary>
    /// 是否启用 SSL/TLS。
    /// 说明：v3.2 该字段存而不用（只按端口 465 判定），v4 让它真正生效：
    /// true → 强制 StartTls（端口 465 时用 SslOnConnect），false → 自动协商。
    /// </summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>发件人显示名（默认取服务器名）。</summary>
    public string DisplayName { get; set; } = "AgoraIn";

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>是否已配置可用（Host 与 From 必填）。</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>
/// 邮件发送服务（MailKit）。
///
/// 相比 v3.2 的改进：
///  · 端口与 SSL 的正确对应（465=SslOnConnect；587/25 等按配置决定 StartTls 或自动协商）
///  · 配置存数据库而非明文 JSON 文件；密码在 API 出参中脱敏
///  · 失败返回明确错误信息，便于前端提示；连接超时可控
/// </summary>
public sealed class EmailSender
{
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(ILogger<EmailSender> logger) => _logger = logger;

    /// <summary>发送 HTML 邮件。</summary>
    public async Task<(bool Ok, string? Error)> SendAsync(
        SmtpSettings settings, string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!settings.IsConfigured)
            return (false, "邮件服务未配置，请联系系统管理员在「系统设置 → 邮件服务」中配置 SMTP。");

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.DisplayName, settings.From));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient { Timeout = 30_000 };

            // 端口 465 必须隐式 SSL；其余端口按配置决定 StartTls 或自动协商
            var security = settings.Port == 465
                ? SecureSocketOptions.SslOnConnect
                : settings.EnableSsl
                    ? SecureSocketOptions.StartTls
                    : SecureSocketOptions.Auto;

            await client.ConnectAsync(settings.Host, settings.Port, security, ct);

            if (!string.IsNullOrWhiteSpace(settings.User))
            {
                await client.AuthenticateAsync(settings.User, settings.Password, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            _logger.LogInformation("邮件已发送：{To} 主题={Subject}", to, subject);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "邮件发送失败：{To} 主题={Subject}", to, subject);
            return (false, $"邮件发送失败：{ex.Message}");
        }
    }

    /// <summary>发送验证码邮件（注册 / 重置密码共用）。</summary>
    public Task<(bool Ok, string? Error)> SendVerifyCodeAsync(
        SmtpSettings settings, string to, string code, string purpose, CancellationToken ct = default)
    {
        var (subject, action) = purpose switch
        {
            "register" => ("【AgoraIn】注册验证码", "注册账号"),
            "reset" => ("【AgoraIn】重置密码验证码", "重置密码"),
            _ => ("【AgoraIn】邮箱验证码", "验证邮箱"),
        };

        var body = $"""
            <div style="font-family:'Microsoft YaHei',sans-serif;font-size:14px;color:#333;line-height:1.7">
              <p>您好，</p>
              <p>您正在{action}，验证码是：</p>
              <p style="font-size:24px;font-weight:bold;letter-spacing:3px;color:#4285f4">{code}</p>
              <p>验证码 10 分钟内有效，请勿泄露给他人。</p>
              <p style="color:#999;font-size:12px">若非本人操作，请忽略本邮件。</p>
              <hr style="border:none;border-top:1px solid #eee;margin:16px 0">
              <p style="color:#999;font-size:12px">AgoraIn 课堂签到打卡与班级教学管理平台</p>
            </div>
            """;

        return SendAsync(settings, to, subject, body, ct);
    }
}
