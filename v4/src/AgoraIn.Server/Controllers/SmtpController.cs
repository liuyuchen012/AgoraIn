using AgoraIn.Core.Security;
using AgoraIn.Server.Security;
using AgoraIn.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// SMTP 邮件服务配置 API。前缀 /api/v4/smtp（仅系统管理员）
///
/// v3.2 把 SMTP 配置明文存在 config.json 且密码脱敏逻辑写在端点里；
/// v4 改存数据库，密码仅在提交非空且非占位符时更新，出参始终脱敏。
/// </summary>
[ApiController]
[Route("api/v4/smtp")]
[Authorize]
[RequirePermission(Permissions.SystemSettings)]
public class SmtpController : ControllerBase
{
    /// <summary>密码脱敏占位符（前端回显时原样返回则不修改密码）。</summary>
    public const string MaskedPassword = "******";

    private readonly ServerDbContext _db;
    private readonly EmailSender _sender;

    public SmtpController(ServerDbContext db, EmailSender sender)
    {
        _db = db;
        _sender = sender;
    }

    /// <summary>读取 SMTP 配置（密码脱敏）。</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var s = await _db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (s == null)
        {
            return Ok(new
            {
                configured = false,
                host = "", port = 465, user = "", password = "", from = "",
                enableSsl = true, displayName = "AgoraIn",
            });
        }

        return Ok(new
        {
            configured = s.IsConfigured,
            host = s.Host,
            port = s.Port,
            user = s.User,
            password = string.IsNullOrEmpty(s.Password) ? "" : MaskedPassword,
            from = s.From,
            enableSsl = s.EnableSsl,
            displayName = s.DisplayName,
            updatedAt = s.UpdatedAt,
        });
    }

    /// <summary>保存 SMTP 配置。</summary>
    [HttpPut]
    public async Task<IActionResult> Save([FromBody] SmtpSettingsRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Host))
            return BadRequest(new { error = "请填写 SMTP 服务器地址。" });

        if (req.Port is < 1 or > 65535)
            return BadRequest(new { error = "端口需在 1–65535 之间。" });

        if (string.IsNullOrWhiteSpace(req.From) || !req.From.Contains('@'))
            return BadRequest(new { error = "请填写正确的发件人邮箱。" });

        var s = await _db.SmtpSettings.FirstOrDefaultAsync(ct);
        if (s == null)
        {
            s = new SmtpSettings();
            _db.SmtpSettings.Add(s);
        }

        s.Host = req.Host.Trim();
        s.Port = req.Port;
        s.User = req.User?.Trim() ?? "";
        s.From = req.From.Trim();
        s.EnableSsl = req.EnableSsl;
        s.DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? "AgoraIn" : req.DisplayName!.Trim();

        // 密码：仅在提交了新值（非空且非占位符）时更新
        if (!string.IsNullOrEmpty(req.Password) && req.Password != MaskedPassword)
        {
            s.Password = req.Password;
        }

        s.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "邮件服务配置已保存。", configured = s.IsConfigured });
    }

    /// <summary>发送测试邮件，验证配置是否正确。</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] SmtpTestRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.To) || !req.To.Contains('@'))
            return BadRequest(new { error = "请填写用于接收测试邮件的地址。" });

        var s = await _db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (s == null || !s.IsConfigured)
            return BadRequest(new { error = "请先保存 SMTP 配置。" });

        var body = """
            <div style="font-family:'Microsoft YaHei',sans-serif;font-size:14px;line-height:1.7">
              <p>这是一封来自 <strong>AgoraIn</strong> 的测试邮件。</p>
              <p>收到此邮件说明邮件服务配置正确，注册验证码与密码重置功能可正常使用。</p>
            </div>
            """;

        var (ok, error) = await _sender.SendAsync(s, req.To.Trim(), "【AgoraIn】邮件服务测试", body, ct);
        return ok
            ? Ok(new { message = $"测试邮件已发送至 {req.To}，请查收。" })
            : StatusCode(StatusCodes.Status502BadGateway, new { error });
    }
}

public record SmtpSettingsRequest(
    string? Host, int Port, string? User, string? Password,
    string? From, bool EnableSsl, string? DisplayName);

public record SmtpTestRequest(string? To);
