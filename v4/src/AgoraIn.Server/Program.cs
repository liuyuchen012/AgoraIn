using System.Text;
using AgoraIn.Server;
using AgoraIn.Server.Controllers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── EF Core SQLite ──
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "server.db");
builder.Services.AddDbContext<ServerDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));

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

// ── 数据库自动迁移 ──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
    await db.Database.EnsureCreatedAsync();
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
app.UseAuthorization();
app.MapControllers();
app.MapHub<LiveHub>("/hub/live");

// ── 静态文件（Web 管理面板） ──
app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();
