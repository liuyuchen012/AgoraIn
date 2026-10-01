using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Services;

/// <summary>
/// 数据库结构补丁。
/// 项目使用 EnsureCreated 自动建库——它只在**空库**时建表，对已有库不做任何结构变更。
/// 因此后加入的表（座位/点名/签到码/AI 日志）在升级部署的生产库上不会自动出现，
/// 必须在启动时用 CREATE TABLE IF NOT EXISTS 补齐，否则运行期直接报 SQLite "no such table"。
/// 列定义与 EF Core 的 SQLite 映射约定保持一致（TEXT=DateTime/DateOnly/Guid 字符串、INTEGER=bool/enum/int）。
/// </summary>
public static class DbSchemaPatch
{
    public static async Task ApplyAsync(ServerDbContext db, CancellationToken ct = default)
    {
        string[] ddl =
        [
            """
            CREATE TABLE IF NOT EXISTS "SeatCharts" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SeatCharts" PRIMARY KEY,
                "ClassId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Rows" INTEGER NOT NULL,
                "Cols" INTEGER NOT NULL,
                "Podium" INTEGER NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL)
            """,
            """CREATE INDEX IF NOT EXISTS "IX_SeatCharts_ClassId" ON "SeatCharts" ("ClassId")""",
            """
            CREATE TABLE IF NOT EXISTS "Seats" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Seats" PRIMARY KEY,
                "ChartId" TEXT NOT NULL,
                "Row" INTEGER NOT NULL,
                "Col" INTEGER NOT NULL,
                "Disabled" INTEGER NOT NULL,
                "GroupName" TEXT NULL,
                "StudentId" TEXT NULL)
            """,
            """CREATE INDEX IF NOT EXISTS "IX_Seats_ChartId" ON "Seats" ("ChartId")""",
            """
            CREATE TABLE IF NOT EXISTS "RollCallSessions" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_RollCallSessions" PRIMARY KEY,
                "ClassId" TEXT NOT NULL,
                "Mode" INTEGER NOT NULL,
                "Subject" TEXT NULL,
                "StartedAt" TEXT NOT NULL,
                "EndedAt" TEXT NULL,
                "WeightFairness" INTEGER NOT NULL)
            """,
            """CREATE INDEX IF NOT EXISTS "IX_RollCallSessions_ClassId_StartedAt" ON "RollCallSessions" ("ClassId", "StartedAt")""",
            """
            CREATE TABLE IF NOT EXISTS "RollCallRecords" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_RollCallRecords" PRIMARY KEY,
                "SessionId" TEXT NOT NULL,
                "StudentId" TEXT NOT NULL,
                "Result" INTEGER NOT NULL,
                "CalledAt" TEXT NOT NULL,
                "ResultAt" TEXT NULL,
                "PointRecordId" TEXT NULL)
            """,
            """CREATE INDEX IF NOT EXISTS "IX_RollCallRecords_SessionId" ON "RollCallRecords" ("SessionId")""",
            """
            CREATE TABLE IF NOT EXISTS "SignInCodes" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SignInCodes" PRIMARY KEY,
                "Code" TEXT NOT NULL,
                "TaskId" TEXT NOT NULL,
                "Classroom" TEXT NULL,
                "Subject" TEXT NULL,
                "Password" TEXT NULL,
                "CreatedBy" TEXT NOT NULL,
                "Active" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "ExpiresAt" TEXT NULL)
            """,
            """CREATE INDEX IF NOT EXISTS "IX_SignInCodes_Code" ON "SignInCodes" ("Code")""",
            """
            CREATE TABLE IF NOT EXISTS "TaskRosterEntries" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_TaskRosterEntries" PRIMARY KEY,
                "TaskId" TEXT NOT NULL,
                "StudentId" TEXT NOT NULL,
                "SortOrder" INTEGER NOT NULL)
            """,
            """CREATE INDEX IF NOT EXISTS "IX_TaskRosterEntries_TaskId" ON "TaskRosterEntries" ("TaskId")""",
            """
            CREATE TABLE IF NOT EXISTS "AiCallLogs" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AiCallLogs" PRIMARY KEY AUTOINCREMENT,
                "Endpoint" TEXT NOT NULL,
                "Model" TEXT NOT NULL,
                "PromptTokens" INTEGER NULL,
                "CompletionTokens" INTEGER NULL,
                "TotalTokens" INTEGER NULL,
                "DurationMs" INTEGER NOT NULL,
                "Success" INTEGER NOT NULL,
                "Error" TEXT NULL,
                "CreatedAt" TEXT NOT NULL)
            """,
            """CREATE INDEX IF NOT EXISTS "IX_AiCallLogs_CreatedAt" ON "AiCallLogs" ("CreatedAt")""",
        ];

        foreach (var sql in ddl)
        {
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }

        await ApplyRegionColumnsAsync(db, ct);
    }

    /// <summary>
    /// 多区域升级补丁：为区域隔离表补 RegionId 列并回填 manager（历史数据属于服务器运营方主区域），
    /// 建 Regions 表，Users 加 RegionId 并把「用户名全局唯一」改为「区域内唯一」。
    /// </summary>
    private static async Task ApplyRegionColumnsAsync(ServerDbContext db, CancellationToken ct)
    {
        string[] regionDdl =
        [
            """
            CREATE TABLE IF NOT EXISTS "Regions" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Regions" PRIMARY KEY AUTOINCREMENT,
                "RegionId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "DevicePassword" TEXT NULL,
                "OwnerUserId" INTEGER NOT NULL,
                "Activated" INTEGER NOT NULL,
                "ActivationCode" TEXT NULL,
                "ExpireAt" TEXT NULL,
                "MaxDevices" INTEGER NOT NULL,
                "ActivatedAt" TEXT NULL,
                "CreatedAt" TEXT NOT NULL)
            """,
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Regions_RegionId" ON "Regions" ("RegionId")""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Regions_Name" ON "Regions" ("Name")""",
        ];
        foreach (var sql in regionDdl)
        {
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }

        // 区域隔离表：补列 → 回填 manager → 建索引
        foreach (var tableName in db.RegionScopedTableNames)
        {
            var hasColumn = await db.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM pragma_table_info({tableName}) WHERE name = 'RegionId'")
                .SingleAsync(ct);
            if (hasColumn == 0)
            {
                await db.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{tableName}\" ADD COLUMN \"RegionId\" TEXT NULL", ct);
            }
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE \"{tableName}\" SET \"RegionId\" = 'manager' WHERE \"RegionId\" IS NULL", ct);
            await db.Database.ExecuteSqlRawAsync(
                $"CREATE INDEX IF NOT EXISTS \"IX_{tableName}_RegionId\" ON \"{tableName}\" (\"RegionId\")", ct);
        }

        // Users：补 RegionId 列 + 回填 + 唯一约束从全局改到区域内
        var usersHasRegion = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM pragma_table_info('Users') WHERE name = 'RegionId'")
            .SingleAsync(ct);
        if (usersHasRegion == 0)
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN \"RegionId\" TEXT NULL", ct);
        }
        await db.Database.ExecuteSqlRawAsync("UPDATE \"Users\" SET \"RegionId\" = 'manager' WHERE \"RegionId\" IS NULL", ct);
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS \"IX_Users_Username\"", ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Users_Username_RegionId\" ON \"Users\" (\"Username\", \"RegionId\")", ct);

        // Regions 自定义隐私政策列（第三方机构协议制作）
        foreach (var (col, ddlType) in new[] { ("CustomPrivacyContent", "TEXT"), ("CustomPrivacyUpdatedAt", "TEXT") })
        {
            var has = await db.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM pragma_table_info('Regions') WHERE name = {col}")
                .SingleAsync(ct);
            if (has == 0)
            {
                await db.Database.ExecuteSqlRawAsync($"ALTER TABLE \"Regions\" ADD COLUMN \"{col}\" {ddlType} NULL", ct);
            }
        }
    }
}
