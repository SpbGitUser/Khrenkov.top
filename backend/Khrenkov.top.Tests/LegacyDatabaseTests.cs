using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Khrenkov.top.Tests;

public sealed class LegacyDatabaseTests
{
    [Fact]
    public async Task Existing_database_is_adopted_without_reimporting_JSON_or_changing_ids_and_limits()
    {
        using var site = new TestSite();
        site.WriteFile("old.txt", "old");
        site.WriteLegacyLinks("""
            [{"Token":"keep-token","FileName":"old.txt","RemainingDownloads":100,"CreatedAt":"2025-01-01T00:00:00Z"},
             {"Token":"consumed-token","FileName":"old.txt","RemainingDownloads":100,"CreatedAt":"2025-01-01T00:00:00Z"}]
            """);
        CreateLegacyDatabase(site, imported: true);

        var result = await MigrationLifecycleTests.RunDatabaseCommand(site, "migrate");

        Assert.True(result.ExitCode == 0, result.Output);
        await using var database = site.CreateContext();
        var file = await database.Files.SingleAsync();
        Assert.Equal(77, file.Id);
        var link = await database.ShareLinks.SingleAsync();
        Assert.Equal("keep-token", link.Token);
        Assert.Equal(1, link.RemainingDownloads);
        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), link.CreatedAtUtc);
        Assert.Single(await database.Database.GetAppliedMigrationsAsync());
        Assert.Equal(2, await database.DataMigrations.CountAsync());
        using var app = site.Start(migrate: false);
        using var client = app.CreateClient();
        Assert.Equal("old", await client.GetStringAsync("/share/keep-token"));
        Assert.NotEqual("old", await client.GetStringAsync("/share/consumed-token"));
    }

    [Fact]
    public async Task Interrupted_legacy_import_can_complete_after_schema_adoption()
    {
        using var site = new TestSite();
        site.WriteFile("old.txt", "old");
        site.WriteLegacyLinks("""
            [{"Token":"legacy-link","FileName":"old.txt","RemainingDownloads":2,"CreatedAt":"2025-01-01T00:00:00Z"}]
            """);
        CreateLegacyDatabase(site, imported: false);

        site.Migrate();

        await using var database = site.CreateContext();
        Assert.Single(await database.Files.ToListAsync());
        Assert.Equal("legacy-link", (await database.ShareLinks.SingleAsync()).Token);
        Assert.Equal(2, await database.DataMigrations.CountAsync());
    }

    [Fact]
    public async Task Unknown_legacy_version_is_rejected_without_recording_an_EF_baseline()
    {
        using var site = new TestSite();
        CreateLegacyDatabase(site, imported: true);
        using (var connection = site.OpenDatabase())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO SchemaMigrations VALUES (99, 'FutureVersion', '2025-01-01T00:00:00Z')";
            command.ExecuteNonQuery();
        }

        var result = await MigrationLifecycleTests.RunDatabaseCommand(site, "migrate");

        Assert.NotEqual(0, result.ExitCode);
        await using var database = site.CreateContext();
        Assert.Empty(await database.Database.GetAppliedMigrationsAsync());
        Assert.Equal(77, (await database.Files.SingleAsync()).Id);
    }

    private static void CreateLegacyDatabase(TestSite site, bool imported)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(site.DatabasePath)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = site.DatabasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Files (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL UNIQUE CHECK(length(Name) > 0),
                Size INTEGER NOT NULL CHECK(Size >= 0), ModifiedUtc TEXT NOT NULL);
            CREATE TABLE ShareLinks (Token TEXT NOT NULL PRIMARY KEY CHECK(length(Token) > 0),
                FileId INTEGER NOT NULL REFERENCES Files(Id) ON DELETE CASCADE,
                RemainingDownloads INTEGER NOT NULL CHECK(RemainingDownloads >= 0), CreatedAtUtc TEXT NOT NULL);
            CREATE INDEX IX_ShareLinks_FileId ON ShareLinks(FileId);
            CREATE TABLE SchemaMigrations (Version INTEGER PRIMARY KEY, Name TEXT NOT NULL, AppliedAtUtc TEXT NOT NULL);
            INSERT INTO SchemaMigrations VALUES (1, 'InitialSchema', '2025-01-01T00:00:00Z');
            """;
        command.ExecuteNonQuery();
        if (imported)
        {
            command.CommandText = """
                INSERT INTO Files VALUES (77, 'old.txt', 3, '2025-01-01T00:00:00Z');
                INSERT INTO ShareLinks VALUES ('keep-token', 77, 1, '2025-01-01T00:00:00Z');
                INSERT INTO SchemaMigrations VALUES (2, 'ImportExistingFiles', '2025-01-01T00:00:00Z');
                """;
            command.ExecuteNonQuery();
        }
    }
}
