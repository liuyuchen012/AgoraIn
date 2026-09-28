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
    }
}
