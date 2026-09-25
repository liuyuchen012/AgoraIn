using AgoraIn.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgoraIn.Data.Tests;

public class AppDbContextTests : IDisposable
{
    private readonly string _dbPath;

    public AppDbContextTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"agorain-data-tests-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // 临时文件清理失败不影响断言结论
        }
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void EnsureCreated_空模型可建库且可连接()
    {
        using var db = CreateContext();

        Assert.True(db.Database.EnsureCreated());
        Assert.True(db.Database.CanConnect());
        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public void EnsureCreated_重复调用幂等()
    {
        using (var db = CreateContext())
        {
            Assert.True(db.Database.EnsureCreated());
        }

        using (var db2 = CreateContext())
        {
            Assert.True(db2.Database.EnsureCreated());
        }
    }
}
