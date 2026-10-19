using System.Globalization;
using Khrenkov.top.Data.Migrations;
using Microsoft.Data.Sqlite;

namespace Khrenkov.top.Data;

public sealed class ApplicationDatabase
{
    private readonly string _connectionString;
    private readonly ILogger<ApplicationDatabase> _logger;

    public string DatabasePath { get; }
    public string UploadsPath { get; }

    public ApplicationDatabase(IWebHostEnvironment environment, IConfiguration configuration, ILogger<ApplicationDatabase> logger)
    {
        _logger = logger;
        UploadsPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "Uploads"));
        DatabasePath = Path.GetFullPath(configuration["Database:Path"] ?? Path.Combine("Data", "application.db"), environment.ContentRootPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (DatabasePath.Equals(UploadsPath, comparison) || DatabasePath.StartsWith(UploadsPath + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Database:Path must be outside the downloadable Uploads directory.");

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
            DefaultTimeout = 30
        }.ToString();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void Migrate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        Directory.CreateDirectory(UploadsPath);
        using var connection = OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode = WAL;";
            command.ExecuteNonQuery();
        }
        using (var transaction = connection.BeginTransaction())
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS SchemaMigrations (
                    Version INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    AppliedAtUtc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
            command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigrations";
            if (Convert.ToInt64(command.ExecuteScalar()) > 2)
                throw new InvalidOperationException("The database schema is newer than this application. Deploy a compatible release.");
            transaction.Commit();
        }

        ApplyMigration(connection, 1, "InitialSchema", (database, transaction) =>
        {
            var assembly = typeof(ApplicationDatabase).Assembly;
            var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(".001_InitialSchema.sql", StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            using var command = database.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = reader.ReadToEnd();
            command.ExecuteNonQuery();
        });
        ApplyMigration(connection, 2, "ImportExistingFiles", (database, transaction) =>
            ImportExistingFiles.Apply(database, transaction, UploadsPath, _logger));
    }

    private void ApplyMigration(SqliteConnection connection, int version, string name, Action<SqliteConnection, SqliteTransaction> apply)
    {
        // An immediate write transaction also serializes simultaneous application startups.
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = $version";
        command.Parameters.AddWithValue("$version", version);
        if (Convert.ToInt64(command.ExecuteScalar()) != 0)
            return;

        try
        {
            apply(connection, transaction);
            command.CommandText = "INSERT INTO SchemaMigrations (Version, Name, AppliedAtUtc) VALUES ($version, $name, $appliedAt)";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$appliedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
            transaction.Commit();
            _logger.LogInformation("Applied database migration {Version}: {Name}", version, name);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Database migration {version} ({name}) failed. Its changes were rolled back; correct the input and restart to retry.", exception);
        }
    }
}
