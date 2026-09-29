using System.Text;
using AgoraIn.Server;
using AgoraIn.Server.Controllers;
using AgoraIn.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── 数据目录 ──
// 可用 appsettings.json 的 Data:Directory 或环境变量 Data__Directory 覆盖（默认 ContentRoot/data）。
// 注意：SQLite 只会创建数据库**文件**，不会创建所在目录；目录缺失会报
// "SqliteException: SQLite Error 14: 'unable to open database file'"，因此必须显式创建。
var dataDir = builder.Configuration["Data:Directory"] is { Length: > 0 } configuredDir
    ? Path.GetFullPath(configuredDir)
    : Path.Combine(builder.Environment.ContentRootPath, "data");

try
{
    Directory.CreateDirectory(dataDir);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"""
        [致命] 无法创建数据目录：{dataDir}
        原因：{ex.Message}
        请检查：
          1. 该路径的父目录是否存在
          2. 运行服务的用户对该路径是否有写权限（如 chown -R <用户> /path/to/data）
          3. 也可在 appsettings.json 中把 Data:Directory 指向可写目录，或设置环境变量 Data__Directory
        """);
    throw;
}

var dbPath = Path.Combine(dataDir, "server.db");
builder.Services.AddSingleton(new ServerPaths(dataDir));
// AddDbContextFactory 同时把 ServerDbContext 注册为 scoped 服务，控制器照常注入；
// 额外提供单例工厂，供 DeepSeekGradingService 等非作用域组件写 AI 调用日志
builder.Services.AddDbContextFactory<ServerDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));

// ── JWT ──
var jwtKey = builder.Configuration["Jwt:Key"] ?? "AgoraIn-v4-default-key-change-in-production!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        };
    });
builder.Services.AddAuthorization();

// ── SignalR ──
builder.Services.AddSignalR();

// ── DeepSeek AI ──
builder.Services.AddScoped<AiSettingsService>();
builder.Services.AddHttpClient<DeepSeekGradingService>();

// ── 离线授权 ──
builder.Services.AddScoped<LicenseService>();
// ── 多区域：区域激活码签发/校验 ──
builder.Services.AddScoped<RegionCodeService>();
// ── ClassIsland 档案导入/导出/推送 ──
builder.Services.AddScoped<ClassIslandProfileService>();

// ── SMTP 邮件 ──
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddSingleton<EmailRateLimiter>();
builder.Services.AddHostedService<EmailCodeCleaner>();

// ── 更新检查（GitHub Releases 代理 + 缓存）──
builder.Services.AddHttpClient();
builder.Services.AddSingleton<UpdateService>();

// ── Controllers + Swagger ──
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v4", new OpenApiInfo { Title = "AgoraIn v4 API", Version = "v4" });
});

// ── CORS ──
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// ── 数据库自动建库 ──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
    try
    {
        await db.Database.EnsureCreatedAsync();
        // EnsureCreated 只对空库建表；后加入的表（座位/点名/签到码/AI 日志）
        // 必须在已有库上用 CREATE TABLE IF NOT EXISTS 补齐，否则升级部署直接 no such table
        await DbSchemaPatch.ApplyAsync(db);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogCritical(ex, """
            数据库初始化失败。
              数据库文件：{DbPath}
              数据目录：  {DataDir}
            常见原因：
              1. 数据目录不存在或不可写（SQLite 只建文件、不建目录）——请确认目录已创建且运行用户有写权限
              2. 路径所在磁盘已满或只读
              3. 目录被安全策略（如宝塔的防跨站攻击限制）禁止访问
            """, dbPath, dataDir);
        throw;
    }
}

// ── 首次初始化检查 ──
app.MapGet("/api/v4/setup/status", async (ServerDbContext db) =>
{
    var hasAdmin = await db.Users.AnyAsync();
    return Results.Ok(new { needsSetup = !hasAdmin });
});

// ── Swagger ──
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v4/swagger.json", "AgoraIn v4 API"));
}

app.UseCors();
app.UseAuthentication();
// 多区域：认证后、授权前，把 JWT 的 region claim 写入 AsyncLocal 区域上下文，
// 供 ServerDbContext 的全局查询过滤器与 SaveChanges 自动落区域使用（匿名请求回落 manager）
app.Use(async (context, next) =>
{
    AgoraIn.Server.Security.RegionContext.Set(
        context.User.FindFirst("region")?.Value);
    await next();
});
app.UseAuthorization();
app.MapControllers();
app.MapHub<LiveHub>("/hub/live");

// ── 静态文件（Web 管理面板） ──
app.UseDefaultFiles();
app.UseStaticFiles();

// SPA 回退：Web 管理面板使用 history 路由（/login、/dashboard 等），
// 直接访问或刷新这些路径时服务端并无对应文件，必须回退到 index.html 交给前端路由，
// 否则会出现「找不到以下 Web 地址的网页：/login HTTP ERROR 404」。
app.MapFallbackToFile("index.html");

app.Run();
