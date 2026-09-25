using AgoraIn.Core.SelfTest;
using AgoraIn.Data;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.App.Services;

/// <summary>data 模块自测：SQLite 本地库可创建、可打开、可释放。</summary>
public sealed class DataSelfTestModule : ISelfTestModule
{
    public string Name => "data";

    public IReadOnlyList<SelfTestItem> Run(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"agorain-selftest-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;

            using (var db = new AppDbContext(options))
            {
                var created = db.Database.EnsureCreated();
                var canQuery = db.Database.CanConnect();

                return new List<SelfTestItem>
                {
                    new("SQLite 建库（EnsureCreated）", created, path),
                    new("SQLite 连接可读（CanConnect）", canQuery),
                };
            }
        }
        catch (Exception ex)
        {
            return new List<SelfTestItem>
            {
                new("SQLite 本地库可用性", false, ex.Message),
            };
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 临时文件删除失败不影响自测结论
            }
        }
    }
}
