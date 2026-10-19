using System.Net.Http.Json;
using Khrenkov.top.Data;
using Khrenkov.top.Controllers.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Khrenkov.top.Tests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task Legacy_file_lookup_preserves_host_filename_case_semantics()
    {
        using var site = new TestSite();
        site.WriteFile("case.txt", "original");
        site.WriteLegacyLinks("""
            [{"Token":"case-link","FileName":"CASE.TXT","RemainingDownloads":1,"CreatedAt":"2025-01-02T03:04:05Z"}]
            """);
        using var app = site.Start();
        using var client = app.CreateClient();

        var response = await client.GetStringAsync("/share/case-link");

        if (OperatingSystem.IsWindows())
            Assert.Equal("original", response);
        else
            Assert.NotEqual("original", response);
    }

    [Fact]
    public void Startup_imports_existing_files_with_their_metadata()
    {
        using var site = new TestSite();
        site.WriteFile("Отчёт.txt", "hello");
        File.SetLastWriteTimeUtc(Path.Combine(site.UploadsPath, "Отчёт.txt"), new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        site.WriteLegacyLinks("[]");

        using var app = site.Start();
        using var client = app.CreateClient();

        Assert.True(File.Exists(site.DatabasePath), "Startup must create a persistent .db file.");
        using var connection = site.OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, Size, ModifiedUtc FROM Files";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("Отчёт.txt", reader.GetString(0));
        Assert.Equal(5, reader.GetInt64(1));
        Assert.Equal(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc), DateTime.Parse(reader.GetString(2)).ToUniversalTime());
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task Startup_preserves_existing_share_tokens_and_limits()
    {
        using var site = new TestSite();
        site.WriteFile("old.txt", "old content");
        site.WriteLegacyLinks("""
            [{"Token":"existing-token","FileName":"old.txt","RemainingDownloads":2,"CreatedAt":"2025-01-02T03:04:05Z"}]
            """);
        using var app = site.Start();
        using var client = app.CreateClient();

        Assert.Equal("old content", await client.GetStringAsync("/share/existing-token"));
        using var connection = site.OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT RemainingDownloads, CreatedAtUtc FROM ShareLinks WHERE Token = 'existing-token'";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc), DateTime.Parse(reader.GetString(1)).ToUniversalTime());
    }

    [Fact]
    public async Task Restart_does_not_reimport_consumed_legacy_links_or_duplicate_files()
    {
        using var site = new TestSite();
        site.WriteFile("old.txt", "old content");
        site.WriteLegacyLinks("""
            [{"Token":"single-use","FileName":"old.txt","RemainingDownloads":1,"CreatedAt":"2025-01-02T03:04:05Z"}]
            """);
        using (var app = site.Start())
        using (var client = app.CreateClient())
            Assert.Equal("old content", await client.GetStringAsync("/share/single-use"));

        using (var app = site.Start())
        using (var client = app.CreateClient())
            Assert.NotEqual("old content", await client.GetStringAsync("/share/single-use"));

        using var connection = site.OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Files; SELECT COUNT(*) FROM SchemaMigrations;";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.True(reader.NextResult());
        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt64(0));
    }

    [Fact]
    public void Invalid_legacy_json_rolls_back_the_import_and_allows_retry()
    {
        using var site = new TestSite();
        site.WriteFile("old.txt", "old content");
        site.WriteLegacyLinks("{broken");
        Assert.ThrowsAny<Exception>(() => site.Migrate());

        using (var connection = site.OpenDatabase())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM Files";
            Assert.Equal(0L, command.ExecuteScalar());
        }

        site.WriteLegacyLinks("[]");
        using var retry = site.Start();
        using var client = retry.CreateClient();
        using var retryConnection = site.OpenDatabase();
        using var retryCommand = retryConnection.CreateCommand();
        retryCommand.CommandText = "SELECT COUNT(*) FROM Files";
        Assert.Equal(1L, retryCommand.ExecuteScalar());
    }

    [Fact]
    public async Task Restart_reconciles_files_added_or_removed_outside_the_application()
    {
        using var site = new TestSite();
        site.WriteFile("removed.txt", "old");
        using (var first = site.Start())
        using (var client = first.CreateClient())
            await TestSite.Login(client);

        File.Delete(Path.Combine(site.UploadsPath, "removed.txt"));
        site.WriteFile("added.txt", "new content");
        using var restarted = site.Start();
        using var restartedClient = restarted.CreateClient();
        await TestSite.Login(restartedClient);
        using var connection = site.OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, Size FROM Files";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("added.txt", reader.GetString(0));
        Assert.Equal(11L, reader.GetInt64(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Empty_installation_creates_the_schema_without_seed_files()
    {
        using var site = new TestSite();
        using var app = site.Start();
        using var client = app.CreateClient();
        using var connection = site.OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Files";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public void Database_inside_downloadable_uploads_is_rejected()
    {
        using var site = new TestSite();
        using var app = site.Start(Path.Combine(site.UploadsPath, "private.db"), migrate: false);
        Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.False(File.Exists(Path.Combine(site.UploadsPath, "private.db")));
    }
}

internal sealed class TestSite : IDisposable
{
    public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "khrenkov-sqlite-tests", Guid.NewGuid().ToString("N"));
    public string UploadsPath => Path.Combine(RootPath, "Uploads");
    public string DatabasePath => Path.Combine(RootPath, "Data", "application.db");

    public TestSite() => Directory.CreateDirectory(UploadsPath);
    public void WriteFile(string name, string content) => File.WriteAllText(Path.Combine(UploadsPath, name), content);
    public void WriteLegacyLinks(string json) => File.WriteAllText(Path.Combine(UploadsPath, "_shares.json"), json);

    public ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true, DefaultTimeout = 30 }.ToString()).Options);

    public void Migrate()
    {
        using var database = CreateContext();
        new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).ApplyAsync(UploadsPath).GetAwaiter().GetResult();
    }

    public WebApplicationFactory<FilesApiController> Start(string? databasePath = null, bool migrate = true)
    {
        if (migrate)
            Migrate();
        return new WebApplicationFactory<FilesApiController>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(RootPath);
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Path"] = databasePath ?? DatabasePath,
                ["SitePassword"] = "integration-test-password"
            }));
        });
    }

    public SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    public static async Task Login(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { password = "integration-test-password" });
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(RootPath, recursive: true);
    }
}
