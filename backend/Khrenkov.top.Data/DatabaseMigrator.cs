using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace Khrenkov.top.Data;

public sealed class DatabaseMigrator(ApplicationDbContext database, ILogger<DatabaseMigrator> logger)
{
    public async Task ApplyAsync(string uploadsPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(database.Database.GetDbConnection().DataSource))!);
        Directory.CreateDirectory(uploadsPath);
        await LegacyDatabaseAdoption.ApplyAsync(database);
        await RejectUnknownHistoryAsync();
        await database.Database.MigrateAsync();
        await RejectUnknownImportVersionsAsync();
        await LegacyDataImporter.ApplyAsync(database, uploadsPath, logger);
    }

    public async Task RollbackAsync(string target)
    {
        if (!File.Exists(database.Database.GetDbConnection().DataSource))
            throw new InvalidOperationException("The database does not exist; nothing can be rolled back.");
        var known = database.Database.GetMigrations().ToArray();
        var targetId = target == "0" ? "0" : known.SingleOrDefault(id => id.Equals(target, StringComparison.OrdinalIgnoreCase)
            || id[(id.IndexOf('_') + 1)..].Equals(target, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown migration: {target}");
        await RejectUnknownHistoryAsync();
        var applied = (await database.Database.GetAppliedMigrationsAsync()).ToArray();
        if (targetId != "0" && !applied.Contains(targetId, StringComparer.Ordinal))
            throw new InvalidOperationException("Rollback cannot apply migrations. Use database migrate to move forwards.");
        await database.GetService<IMigrator>().MigrateAsync(targetId);
        logger.LogInformation("Database rolled back to {Migration}. Uploaded files were retained", targetId);
    }

    public async Task RequireCurrentAsync()
    {
        if (!File.Exists(database.Database.GetDbConnection().DataSource))
            throw new InvalidOperationException("Database is not initialized. Run database migrate before starting the application.");
        await RejectUnknownHistoryAsync();
        if ((await database.Database.GetPendingMigrationsAsync()).Any())
            throw new InvalidOperationException("Database schema is not current. Run database migrate before starting the application.");
        await RejectUnknownImportVersionsAsync();
        if (!await database.DataMigrations.AnyAsync(migration => migration.Version == 2))
            throw new InvalidOperationException("Initial import is incomplete. Run database migrate before starting the application.");
    }

    public async Task<string> StatusAsync()
    {
        if (!File.Exists(database.Database.GetDbConnection().DataSource))
            return "Database does not exist. Run database migrate.";
        var applied = (await database.Database.GetAppliedMigrationsAsync()).ToArray();
        var pending = (await database.Database.GetPendingMigrationsAsync()).ToArray();
        return $"Applied: {string.Join(", ", applied)}{Environment.NewLine}Pending: {string.Join(", ", pending)}";
    }

    private async Task RejectUnknownHistoryAsync()
    {
        var known = database.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        if ((await database.Database.GetAppliedMigrationsAsync()).Any(migration => !known.Contains(migration)))
            throw new InvalidOperationException("The database contains unknown EF migrations. Use a compatible application version.");
    }

    private async Task RejectUnknownImportVersionsAsync()
    {
        if (await database.DataMigrations.AnyAsync(migration => migration.Version < 1 || migration.Version > 2))
            throw new InvalidOperationException("The database contains an unknown data import version.");
    }
}
