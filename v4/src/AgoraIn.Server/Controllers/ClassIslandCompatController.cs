using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Controllers;

/// <summary>
/// ClassIsland 插件兼容适配层（规格 6.7 / 11.5：插件禁止修改，服务端提供等价接口）。
///
/// 契约来源：通读 <c>AgoraIn.ClassIslandPlugin/Services/CallPoller.cs</c> 提取。
/// 插件调用的是**无 /v4 前缀**的原始路径，因此本控制器直接映射 <c>api/calls_*</c>：
///   · POST api/calls_register —— 呼叫接收端自登记 {name, password, client_version} → {uuid}
///   · POST api/calls_pull     —— 拉取待展示呼叫 {uuid, password} → {calls:[…]}
///   · POST api/calls_ack      —— 确认已展示 {id, uuid, password}
/// 详见 v4/docs/api-contract-classisland.md
/// </summary>
[ApiController]
[Route("api")]
public class ClassIslandCompatController : ControllerBase
{
    private readonly ServerDbContext _db;
    private readonly IConfiguration _config;

    public ClassIslandCompatController(ServerDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // ═══════════════ api/calls_register ═══════════════

    /// <summary>呼叫接收端自登记（ClassIsland 插件启动时调用，无需 AgoraIn 客户端与 RSA 密钥）。</summary>
    [HttpPost("calls_register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] CallsRegisterRequest req)
    {
        if (!CheckPassword(req.Password))
            return Unauthorized(new { error = "连接密码错误" });

        // 幂等：同名设备复用 UUID
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceName == req.Name);
        if (device == null)
        {
            device = new Device
            {
                DeviceUuid = Guid.NewGuid().ToString("N"),
                DeviceName = req.Name ?? "ClassIsland-设备",
                LastSeen = DateTime.Now,
            };
            _db.Devices.Add(device);
        }
        else
        {
            device.LastSeen = DateTime.Now;
        }

        await _db.SaveChangesAsync();
        return Ok(new { uuid = device.DeviceUuid });
    }

    // ═══════════════ api/calls_pull ═══════════════

    /// <summary>拉取待展示的呼叫（插件轮询，返回未确认的呼叫队列）。</summary>
    [HttpPost("calls_pull")]
    [AllowAnonymous]
    public async Task<IActionResult> Pull([FromBody] CallsPullRequest req)
    {
        if (!CheckPassword(req.Password))
            return Unauthorized(new { error = "连接密码错误" });

        if (!string.IsNullOrEmpty(req.Uuid))
        {
            var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceUuid == req.Uuid);
            if (device != null)
            {
                device.LastSeen = DateTime.Now;
                await _db.SaveChangesAsync();
            }
        }

        // 未确认的呼叫：广播（TargetUuid 为空）+ 定向给本机
        var calls = await _db.Calls
            .Where(c => !c.Acked
                        && (c.TargetUuid == null || c.TargetUuid == "" || c.TargetUuid == req.Uuid))
            .OrderBy(c => c.CreatedAt)
            .Take(20)
            .ToListAsync();

        return Ok(new
        {
            calls = calls.Select(c => new
            {
                id = c.Id,
                type = c.Type,
                title = c.Title,
                message = c.Message,
                minutes_before = c.MinutesBefore,
                student_names = c.StudentNames,
                sender = c.Sender,
            }),
        });
    }

    // ═══════════════ api/calls_ack ═══════════════

    /// <summary>确认呼叫已展示（防重复拉取）。</summary>
    [HttpPost("calls_ack")]
    [AllowAnonymous]
    public async Task<IActionResult> Ack([FromBody] CallsAckRequest req)
    {
        if (!CheckPassword(req.Password))
            return Unauthorized(new { error = "连接密码错误" });

        var call = await _db.Calls.FindAsync(req.Id);
        if (call == null) return Ok(new { ok = false });

        call.Acked = true;
        call.AckedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ═══════════════ 教师端：下发呼叫（v4 新增，供桌面端使用） ═══════════════

    /// <summary>创建呼叫（教师端发起：待下课通知 / 上课应急 / 下课传唤）。</summary>
    [HttpPost("v4/calls")]
    [Authorize]
    public async Task<IActionResult> CreateCall([FromBody] CreateCallRequest req)
    {
        var call = new CallEntity
        {
            Type = req.Type,
            Title = req.Title ?? DefaultTitle(req.Type),
            Message = req.Message ?? "",
            MinutesBefore = req.MinutesBefore,
            StudentNames = req.StudentNames ?? "",
            Sender = User.Identity?.Name ?? "教师端",
            TargetUuid = string.IsNullOrEmpty(req.TargetUuid) ? null : req.TargetUuid,
            CreatedAt = DateTime.Now,
        };
        _db.Calls.Add(call);
        await _db.SaveChangesAsync();
        return Created("", new { call.Id });
    }

    /// <summary>呼叫队列（教师端查看发送历史）。</summary>
    [HttpGet("v4/calls")]
    [Authorize]
    public async Task<IActionResult> ListCalls([FromQuery] int take = 50)
        => Ok(await _db.Calls.OrderByDescending(c => c.CreatedAt)
            .Take(Math.Clamp(take, 1, 200)).ToListAsync());

    private static string DefaultTitle(string type) => type switch
    {
        "prenotice" => "待下课通知",
        "summon" => "下课传唤",
        "emergency" => "上课应急",
        _ => "呼叫",
    };

    /// <summary>比对集控服务器连接密码（未配置时放行，便于本地联调）。</summary>
    private bool CheckPassword(string? password)
    {
        var expected = _config["Server:Password"] ?? "";
        if (string.IsNullOrEmpty(expected)) return true;
        return password == expected;
    }
}

// ═══════════════ 请求模型 ═══════════════

public record CallsRegisterRequest(string? Name, string? Password, string? ClientVersion);

public record CallsPullRequest(string? Uuid, string? Password);

public record CallsAckRequest(int Id, string? Uuid, string? Password);

public record CreateCallRequest(
    string Type, string? Title, string? Message,
    int MinutesBefore, string? StudentNames, string? TargetUuid);
