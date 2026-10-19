using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Khrenkov.top.Data;

internal static class LegacyDatabaseAdoption
{
    // Only compatibility/history bookkeeping uses SQL. Application data uses DbSet entities.
    public static async Task ApplyAsync(ApplicationDbContext database)
    {
        if (!File.Exists(database.Database.GetDbConnection().DataSource)
            || (await database.Database.GetAppliedMigrationsAsync()).Any())
            return;

        await database.Database.OpenConnectionAsync();
        bool legacy;
        using (var command = database.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaMigrations'";
            legacy = Convert.ToInt64(await command.ExecuteScalarAsync()) != 0;
        }
        if (!legacy)
            return;

        await using var transaction = await database.Database.BeginTransactionAsync();
        var versions = await database.DataMigrations.AsNoTracking().ToListAsync();
        if (!versions.Any(version => version.Version == 1 && version.Name == "InitialSchema")
            || versions.Any(version => version.Version is < 1 or > 2
                || (version.Version == 2 && version.Name != "ImportExistingFiles")))
            throw new InvalidOperationException("Unknown legacy database version; adoption was refused.");

        // Materialization verifies the expected columns before any history is adopted.
        _ = await database.Files.AsNoTracking().OrderBy(file => file.Id).Take(1).ToListAsync();
        _ = await database.ShareLinks.AsNoTracking().OrderBy(link => link.Token).Take(1).ToListAsync();
        var history = database.GetService<IHistoryRepository>();
        await database.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript());
        await database.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Files_Name ON Files (Name)");
        var initial = database.Database.GetMigrations().First();
        await database.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(initial, "8.0.22")));
        await transaction.CommitAsync();
    }
}
