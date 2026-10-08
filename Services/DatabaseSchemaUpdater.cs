using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PwcApi.Data;
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace PwcApi.Services
{
    /// <summary>
    /// Runs once at API startup and adds any missing columns/tables this version of the code needs.
    /// Safe to run every time: each change is applied only if it's missing.
    ///
    /// Why: the EF migrations in this project are out of date with the real MySQL database, so schema
    /// changes were done by hand – and a missed manual step (e.g. CheckOutDate) breaks every attendance call.
    /// </summary>
    public static class DatabaseSchemaUpdater
    {
        private static readonly (string Table, string Column, string Ddl)[] RequiredColumns =
        {
            ("ResourceAttendances", "CheckOutDate",
             "ALTER TABLE `ResourceAttendances` ADD COLUMN `CheckOutDate` DATETIME(6) NULL AFTER `CheckInTime`"),
        };

        private static readonly (string Table, string Ddl)[] RequiredTables =
        {
            ("BatchBroadcasts", @"CREATE TABLE IF NOT EXISTS `BatchBroadcasts` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `BatchId` INT NOT NULL,
                `CoachId` INT NOT NULL,
                `Message` LONGTEXT NULL,
                `MediaUrls` LONGTEXT NULL,
                `MediaPayloads` LONGTEXT NULL,
                `Status` VARCHAR(32) NOT NULL DEFAULT 'Queued',
                `TotalRecipients` INT NOT NULL DEFAULT 0,
                `SentCount` INT NOT NULL DEFAULT 0,
                `FailedCount` INT NOT NULL DEFAULT 0,
                `LastError` LONGTEXT NULL,
                `CreatedAt` DATETIME(6) NOT NULL,
                `StartedAt` DATETIME(6) NULL,
                `CompletedAt` DATETIME(6) NULL,
                PRIMARY KEY (`Id`),
                INDEX `IX_BatchBroadcasts_Status` (`Status`),
                INDEX `IX_BatchBroadcasts_Batch` (`BatchId`, `CreatedAt`)
              ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),

            ("BatchBroadcastRecipients", @"CREATE TABLE IF NOT EXISTS `BatchBroadcastRecipients` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `BroadcastId` INT NOT NULL,
                `EnrollmentId` INT NOT NULL,
                `ParentName` VARCHAR(255) NULL,
                `ChildName` VARCHAR(512) NULL,
                `Phone` VARCHAR(32) NOT NULL,
                `Status` VARCHAR(16) NOT NULL DEFAULT 'Pending',
                `Error` LONGTEXT NULL,
                `SentAt` DATETIME(6) NULL,
                PRIMARY KEY (`Id`),
                INDEX `IX_BatchBroadcastRecipients_Broadcast` (`BroadcastId`, `Status`)
              ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
        };

        private static readonly (string Table, string Index, string Ddl)[] RequiredIndexes =
        {
            ("ResourceAttendances", "IX_ResourceAttendances_Resource_Open",
             "CREATE INDEX `IX_ResourceAttendances_Resource_Open` ON `ResourceAttendances` (`ResourceId`, `CheckInDate`)"),
        };

        public static async Task EnsureAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var conn = db.Database.GetDbConnection();
                await conn.OpenAsync(ct);

                try
                {
                    foreach (var (table, ddl) in RequiredTables)
                    {
                        if (await CountAsync(conn, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t", ct, ("@t", table)) == 0)
                            await ApplyAsync(conn, ddl, $"create table {table}", logger, ct);
                    }

                    foreach (var (table, column, ddl) in RequiredColumns)
                    {
                        var exists = await CountAsync(conn,
                            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND COLUMN_NAME = @c",
                            ct, ("@t", table), ("@c", column));
                        if (exists == 0)
                            await ApplyAsync(conn, ddl, $"add column {table}.{column}", logger, ct);
                    }

                    foreach (var (table, index, ddl) in RequiredIndexes)
                    {
                        var exists = await CountAsync(conn,
                            "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND INDEX_NAME = @i",
                            ct, ("@t", table), ("@i", index));
                        if (exists == 0)
                            await ApplyAsync(conn, ddl, $"create index {index}", logger, ct, critical: false);
                    }

                    logger.LogInformation("✅ Database schema check complete");
                }
                finally
                {
                    await conn.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                // Never stop the API from starting – but make the problem impossible to miss.
                logger.LogError(ex, "🚨 Database schema check failed. Run backend/sql/2026-10_attendance_and_messaging.sql manually.");
            }
        }

        private static async Task ApplyAsync(DbConnection conn, string ddl, string what, ILogger logger,
                                             CancellationToken ct, bool critical = true)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync(ct);
                logger.LogWarning("🛠️ Database updated: {What}", what);
            }
            catch (Exception ex)
            {
                if (critical)
                    logger.LogError(ex, "🚨 Could not {What}. Ask your DB admin to run:\n{Ddl}", what, ddl);
                else
                    logger.LogWarning(ex, "Optional schema change skipped: {What}", what);
            }
        }

        private static async Task<long> CountAsync(DbConnection conn, string sql, CancellationToken ct,
                                                   params (string Name, object Value)[] parameters)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = name;
                p.Value = value;
                cmd.Parameters.Add(p);
            }
            var result = await cmd.ExecuteScalarAsync(ct);
            return result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
        }
    }
}
