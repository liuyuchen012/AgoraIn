using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Data;

/// <summary>
/// AgoraIn v4 本地数据库上下文（SQLite，本地优先架构）。
/// P0 骨架：领域实体在 P1 阶段落位；届时在此注册 DbSet 并通过迁移建模。
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }
}
