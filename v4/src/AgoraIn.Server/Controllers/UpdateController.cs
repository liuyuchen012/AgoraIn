using AgoraIn.Core.Security;
using AgoraIn.Server.Security;
using AgoraIn.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// 版本与更新 API。
///
/// | 路径 | 鉴权 | 用途 |
/// | --- | --- | --- |
/// | GET  /api/v4/version        | 匿名 | 版本信息汇总（客户端启动即查） |
/// | POST /api/v4/client-update  | 匿名 | 客户端检查更新（服务端代理 GitHub，避免客户端直连被墙） |
/// | GET  /api/v4/server-update  | 登录 | 服务端自身更新提示（v3.2 此接口无鉴权，此处收紧） |
///
/// 说明：兼容 v3.2 客户端的调用习惯（同样的路径后缀与响应字段名），
/// 同时区分「服务器说没有更新」与「查询失败」——v3 两者都返回 has_update=false，
/// 导致纯局域网部署每次检查都要等 GitHub 超时。
/// </summary>
[ApiController]
[Route("api/v4")]
public class UpdateController : ControllerBase
{
    private readonly UpdateService _updates;
    public UpdateController(UpdateService updates) => _updates = updates;

    /// <summary>版本信息汇总。</summary>
    [HttpGet("version")]
    [AllowAnonymous]
    public async Task<IActionResult> Version(CancellationToken ct)
    {
        var release = await _updates.GetLatestReleaseAsync(ct: ct);
        var serverHasUpdate = release != null && release.IsNewerThan(UpdateService.ServerVersion);

        return Ok(new
        {
            server_version = UpdateService.ServerVersion,
            latest_version = release?.Version,
            download_url = UpdateService.ReleasesPage,
            server_update_available = serverHasUpdate,
            server_latest_version = release?.Version,
            last_checked = _updates.LastChecked,
            update_source_available = release != null,
        });
    }

    /// <summary>客户端检查更新（服务端代查 GitHub，客户端无需直连外网）。</summary>
    [HttpPost("client-update")]
    [AllowAnonymous]
    public async Task<IActionResult> ClientUpdate([FromBody] ClientUpdateRequest? req, CancellationToken ct)
    {
        var platform = string.IsNullOrWhiteSpace(req?.Platform) ? "win-x64" : req!.Platform!;
        var current = string.IsNullOrWhiteSpace(req?.CurrentVersion) ? "v0.0.0" : req!.CurrentVersion!;

        var release = await _updates.GetLatestReleaseAsync(ct: ct);

        // 查询失败：明确告知客户端「稍后再试」，不要伪装成「已是最新」
        if (release == null)
        {
            return Ok(new
            {
                ok = false,
                has_update = false,
                error = _updates.LastError ?? "暂时无法获取版本信息，请稍后重试",
                server_version = UpdateService.ServerVersion,
                releases_page = UpdateService.ReleasesPage,
            });
        }

        var hasUpdate = release.IsNewerThan(current);
        var downloadUrl = UpdateService.PickDownloadUrl(release, platform, release.Tag);

        return Ok(new
        {
            ok = true,
            has_update = hasUpdate,
            latest_version = release.Version,
            current_version = current,
            download_url = downloadUrl ?? "",
            releases_page = UpdateService.ReleasesPage,
            published_at = release.PublishedAt,
            notes = release.Notes,
            assets = release.Assets.Select(a => new { a.Name, a.DownloadUrl, a.Size }),
            server_version = UpdateService.ServerVersion,
            checked_at = _updates.LastChecked,
        });
    }

    /// <summary>服务端自身更新检查（登录用户可见；v3.2 该接口无鉴权）。</summary>
    [HttpGet("server-update")]
    [Authorize]
    public async Task<IActionResult> ServerUpdate(CancellationToken ct)
    {
        var release = await _updates.GetLatestReleaseAsync(ct: ct);
        if (release == null)
        {
            return Ok(new
            {
                ok = false,
                error = _updates.LastError ?? "暂时无法获取版本信息",
                current_version = UpdateService.ServerVersion,
                releases_page = UpdateService.ReleasesPage,
            });
        }

        return Ok(new
        {
            ok = true,
            has_update = release.IsNewerThan(UpdateService.ServerVersion),
            current_version = UpdateService.ServerVersion,
            latest_version = release.Version,
            download_url = UpdateService.PickDownloadUrl(release, "linux-x64", release.Tag)
                           ?? UpdateService.ReleasesPage,
            releases_page = UpdateService.ReleasesPage,
            published_at = release.PublishedAt,
            notes = release.Notes,
            last_checked = _updates.LastChecked,
        });
    }

    /// <summary>强制刷新版本缓存（管理员，用于刚发版后立即生效）。</summary>
    [HttpPost("updates/refresh")]
    [RequirePermission(Permissions.SystemSettings)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var release = await _updates.GetLatestReleaseAsync(force: true, ct: ct);
        return Ok(new
        {
            ok = release != null,
            latest_version = release?.Version,
            error = _updates.LastError,
        });
    }
}

public record ClientUpdateRequest(string? Platform, string? CurrentVersion);
