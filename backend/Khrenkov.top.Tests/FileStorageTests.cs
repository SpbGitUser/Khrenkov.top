using System.Net;
using System.Net.Http.Json;
using Khrenkov.top.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Khrenkov.top.Tests;

public sealed class FileStorageTests
{
    [Fact]
    public async Task Existing_file_named_staging_does_not_block_new_uploads()
    {
        using var site = new TestSite();
        site.WriteFile(".staging", "existing user file");
        using var app = site.Start();
        using var client = app.CreateClient();
        await TestSite.Login(client);
        using var body = new MultipartFormDataContent { { new StringContent("new content"), "file", "new.txt" } };

        using var response = await client.PostAsync("/api/files/upload", body);

        response.EnsureSuccessStatusCode();
        Assert.Equal("existing user file", File.ReadAllText(Path.Combine(site.UploadsPath, ".staging")));
        Assert.Equal("new content", File.ReadAllText(Path.Combine(site.UploadsPath, "new.txt")));
    }

    [Fact]
    public async Task Upload_cannot_replace_a_file_while_its_deletion_is_waiting_for_the_database()
    {
        using var site = new TestSite();
        site.WriteFile("race.txt", "old content");
        using var app = site.Start();
        using var client = app.CreateClient();
        await TestSite.Login(client);
        using var connection = site.OpenDatabase();
        using var transaction = connection.BeginTransaction();
        using var body = new MultipartFormDataContent { { new StringContent("replacement"), "file", "race.txt" } };
        var filePath = Path.Combine(site.UploadsPath, "race.txt");
        var deleteTask = client.DeleteAsync("/api/files/race.txt");
        var prematurelyReplaced = false;
        Task<HttpResponseMessage>? uploadTask = null;
        try
        {
            await WaitUntil(() => !File.Exists(filePath));
            uploadTask = client.PostAsync("/api/files/upload", body);
            await WaitUntil(() => File.Exists(filePath) || Directory.EnumerateFiles(site.UploadsPath, "*", SearchOption.AllDirectories)
                .Any(path => new FileInfo(path).Length == 11));
            await Task.Delay(200);
            prematurelyReplaced = File.Exists(filePath);
        }
        finally
        {
            transaction.Rollback();
        }
        using var delete = await deleteTask;
        delete.EnsureSuccessStatusCode();
        using var upload = await uploadTask!;
        upload.EnsureSuccessStatusCode();
        Assert.False(prematurelyReplaced, "Upload finalization and deletion must serialize across filesystem and metadata writes.");
        Assert.Equal("replacement", File.ReadAllText(filePath));
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Size FROM Files WHERE Name = 'race.txt'";
        Assert.Equal(11L, command.ExecuteScalar());
    }

    [Fact]
    public async Task Case_variant_upload_and_delete_follow_the_host_filesystem()
    {
        using var site = new TestSite();
        site.WriteFile("case.txt", "original");
        using var app = site.Start();
        using var client = app.CreateClient();
        await TestSite.Login(client);
        using var share = await client.PostAsJsonAsync("/api/files/share", new { fileName = "CASE.TXT", maxDownloads = 2 });
        Assert.Equal(OperatingSystem.IsWindows() ? HttpStatusCode.OK : HttpStatusCode.NotFound, share.StatusCode);
        using var body = new MultipartFormDataContent { { new StringContent("replacement"), "file", "CASE.TXT" } };
        using var upload = await client.PostAsync("/api/files/upload", body);
        upload.EnsureSuccessStatusCode();
        using var connection = site.OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Files";
        Assert.Equal(OperatingSystem.IsWindows() ? 1L : 2L, command.ExecuteScalar());

        using var deletion = await client.DeleteAsync("/api/files/CASE.TXT");
        deletion.EnsureSuccessStatusCode();
        Assert.Equal(OperatingSystem.IsWindows() ? 0L : 1L, command.ExecuteScalar());
        Assert.Equal(!OperatingSystem.IsWindows(), File.Exists(Path.Combine(site.UploadsPath, "case.txt")));
    }

    [Fact]
    public async Task Upload_persists_metadata_for_the_completed_file()
    {
        using var site = new TestSite();
        using var app = site.Start();
        using var client = app.CreateClient();
        await TestSite.Login(client);
        using var body = new MultipartFormDataContent { { new StringContent("new content"), "file", "new.txt" } };

        using var response = await client.PostAsync("/api/files/upload", body);
        response.EnsureSuccessStatusCode();

        Assert.Equal("new content", File.ReadAllText(Path.Combine(site.UploadsPath, "new.txt")));
        using var connection = site.OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Size FROM Files WHERE Name = 'new.txt'";
        Assert.Equal(11L, command.ExecuteScalar());
        var files = await client.GetFromJsonAsync<List<FileResponse>>("/api/files");
        var file = Assert.Single(files!);
        Assert.Equal("new.txt", file.Name);
        Assert.Equal(11, file.Size);
    }

    [Fact]
    public async Task Overwrite_updates_metadata_and_keeps_existing_share_links()
    {
        using var site = new TestSite();
        site.WriteFile("old.txt", "old");
        site.WriteLegacyLinks("""
            [{"Token":"keep-link","FileName":"old.txt","RemainingDownloads":2,"CreatedAt":"2025-01-02T03:04:05Z"}]
            """);
        using var app = site.Start();
        using var client = app.CreateClient();
        await TestSite.Login(client);
        using var body = new MultipartFormDataContent { { new StringContent("replacement"), "file", "old.txt" } };

        using var response = await client.PostAsync("/api/files/upload", body);
        response.EnsureSuccessStatusCode();

        using var connection = site.OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Size FROM Files WHERE Name = 'old.txt'";
        Assert.Equal(11L, command.ExecuteScalar());
        Assert.Equal("replacement", await client.GetStringAsync("/share/keep-link"));
    }

    [Fact]
    public async Task Delete_removes_metadata_and_related_share_links()
    {
        using var site = new TestSite();
        site.WriteFile("delete.txt", "delete");
        site.WriteLegacyLinks("""
            [{"Token":"delete-link","FileName":"delete.txt","RemainingDownloads":2,"CreatedAt":"2025-01-02T03:04:05Z"}]
            """);
        using var app = site.Start();
        using var client = app.CreateClient();
        await TestSite.Login(client);

        using var response = await client.DeleteAsync("/api/files/delete.txt");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(site.UploadsPath, "delete.txt")));
        using var connection = site.OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM Files) + (SELECT COUNT(*) FROM ShareLinks)";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task New_share_link_survives_restart_without_writing_legacy_json()
    {
        using var site = new TestSite();
        site.WriteFile("new.txt", "download");
        string token;
        using (var app = site.Start())
        using (var client = app.CreateClient())
        {
            await TestSite.Login(client);
            using var response = await client.PostAsJsonAsync("/api/files/share", new { fileName = "new.txt", maxDownloads = 2 });
            response.EnsureSuccessStatusCode();
            token = (await response.Content.ReadFromJsonAsync<ShareResponse>())!.Token;
            using var connection = site.OpenDatabase();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT RemainingDownloads FROM ShareLinks WHERE Token = $token";
            command.Parameters.AddWithValue("$token", token);
            Assert.Equal(2L, command.ExecuteScalar());
        }

        Assert.False(File.Exists(Path.Combine(site.UploadsPath, "_shares.json")));
        using var restarted = site.Start();
        using var restartedClient = restarted.CreateClient();
        Assert.Equal("download", await restartedClient.GetStringAsync($"/share/{token}"));
    }

    [Fact]
    public async Task Concurrent_consumers_can_only_use_a_single_use_link_once()
    {
        using var site = new TestSite();
        site.WriteFile("one.txt", "one");
        site.WriteLegacyLinks("""
            [{"Token":"one-link","FileName":"one.txt","RemainingDownloads":1,"CreatedAt":"2025-01-02T03:04:05Z"}]
            """);
        using var app = site.Start();
        using var client = app.CreateClient();
        var service = app.Services.GetRequiredService<ShareLinkService>();

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => service.TryConsume("one-link"))));

        Assert.Single(results, result => result.Success);
        using var connection = site.OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(SUM(RemainingDownloads), 0) FROM ShareLinks";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task Missing_file_does_not_consume_a_share_allowance()
    {
        using var site = new TestSite();
        site.WriteFile("missing.txt", "content");
        site.WriteLegacyLinks("""
            [{"Token":"missing-link","FileName":"missing.txt","RemainingDownloads":1,"CreatedAt":"2025-01-02T03:04:05Z"}]
            """);
        using var app = site.Start();
        using var client = app.CreateClient();
        File.Delete(Path.Combine(site.UploadsPath, "missing.txt"));

        using var missingResponse = await client.GetAsync("/share/missing-link");
        Assert.False(missingResponse.Content.Headers.ContentType?.MediaType == "application/octet-stream");
        site.WriteFile("missing.txt", "restored");

        Assert.Equal("restored", await client.GetStringAsync("/share/missing-link"));
    }

    [Fact]
    public async Task Legacy_json_cannot_be_downloaded_deleted_shared_or_overwritten()
    {
        using var site = new TestSite();
        site.WriteLegacyLinks("[]");
        using var app = site.Start();
        using var client = app.CreateClient();
        await TestSite.Login(client);

        using var download = await client.GetAsync("/api/files/download/_shares.json");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        using var delete = await client.DeleteAsync("/api/files/_shares.json");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        using var share = await client.PostAsJsonAsync("/api/files/share", new { fileName = "_shares.json", maxDownloads = 1 });
        Assert.Equal(HttpStatusCode.NotFound, share.StatusCode);
        using var body = new MultipartFormDataContent { { new StringContent("overwrite"), "file", "_shares.json" } };
        using var upload = await client.PostAsync("/api/files/upload", body);
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Equal("[]", File.ReadAllText(Path.Combine(site.UploadsPath, "_shares.json")));
    }

    [Theory]
    [InlineData("INSERT INTO Files (Name, Size, ModifiedUtc) VALUES ('bad', -1, '2025-01-01')")]
    [InlineData("INSERT INTO ShareLinks (Token, FileId, RemainingDownloads, CreatedAtUtc) VALUES ('bad', 999, 1, '2025-01-01')")]
    [InlineData("INSERT INTO ShareLinks (Token, FileId, RemainingDownloads, CreatedAtUtc) SELECT 'bad', Id, -1, '2025-01-01' FROM Files")]
    public void Schema_rejects_invalid_sizes_limits_and_orphan_links(string sql)
    {
        using var site = new TestSite();
        site.WriteFile("file.txt", "file");
        using var app = site.Start();
        using var client = app.CreateClient();
        using var connection = site.OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var exception = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

        Assert.Equal(19, exception.SqliteErrorCode);
    }

    private sealed record FileResponse(string Name, long Size, DateTime Modified);
    private sealed record ShareResponse(string Token);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
                throw new TimeoutException("The expected filesystem operation did not occur.");
            await Task.Delay(10);
        }
    }
}
