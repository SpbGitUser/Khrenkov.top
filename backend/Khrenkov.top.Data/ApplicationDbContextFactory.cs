using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Khrenkov.top.Data;

// EF tooling must not start HTTP or perform runtime imports just to scaffold a migration.
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // The deployed executable uses its configuration; tooling only needs a model to scaffold.
        var connection = Environment.GetEnvironmentVariable("Database__Path") ?? Path.Combine("Data", "application.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = connection, ForeignKeys = true }.ToString());
        return new ApplicationDbContext(options.Options);
    }
}
