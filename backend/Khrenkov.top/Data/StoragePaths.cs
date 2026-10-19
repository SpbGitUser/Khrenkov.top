using Microsoft.Data.Sqlite;

namespace Khrenkov.top.Data;

public sealed class StoragePaths
{
    public string DatabasePath { get; }
    public string UploadsPath { get; }
    public string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath, ForeignKeys = true, DefaultTimeout = 30
    }.ToString();

    public StoragePaths(IWebHostEnvironment environment, IConfiguration configuration)
    {
        UploadsPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "Uploads"));
        DatabasePath = Path.GetFullPath(configuration["Database:Path"] ?? Path.Combine("Data", "application.db"), environment.ContentRootPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (DatabasePath.Equals(UploadsPath, comparison) || DatabasePath.StartsWith(UploadsPath + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Database:Path must be outside the downloadable Uploads directory.");
    }
}
