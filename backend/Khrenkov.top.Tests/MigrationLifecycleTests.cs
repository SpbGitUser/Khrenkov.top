using System.Diagnostics;
using Khrenkov.top.Controllers.Api;
using Khrenkov.top.Data;
using Khrenkov.top.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Khrenkov.top.Tests;

public sealed class MigrationLifecycleTests
{
    [Fact]
    public void Normal_start_must_not_apply_migrations()
    {
        using var site = new TestSite();
        using var app = site.Start(migrate: false);

        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("database migrate", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(site.DatabasePath));
    }

    [Fact]
    public async Task Rollback_removes_schema_without_deleting_files_and_normal_start_cannot_reapply_it()
    {
        using var site = new TestSite();
        site.WriteFile("keep.txt", "keep");
        Assert.Equal(0, (await RunDatabaseCommand(site, "migrate")).ExitCode);

        var rollback = await RunDatabaseCommand(site, "rollback", "--to", "0");

        Assert.True(rollback.ExitCode == 0, rollback.Output);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(site.UploadsPath, "keep.txt")));
        using (var connection = site.OpenDatabase())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Files', 'ShareLinks', 'SchemaMigrations')";
            Assert.Equal(0L, command.ExecuteScalar());
        }
        using (var app = site.Start(migrate: false))
            Assert.Contains("database migrate", Assert.ThrowsAny<Exception>(() => app.CreateClient()).ToString());

        Assert.Equal(0, (await RunDatabaseCommand(site, "migrate")).ExitCode);
        await using var context = site.CreateContext();
        Assert.Equal("keep.txt", (await context.Files.SingleAsync()).Name);
    }

    [Fact]
    public async Task Entity_relationships_are_persisted_by_DbContext_in_the_separate_data_project()
    {
        using var site = new TestSite();
        site.Migrate();
        await using (var context = site.CreateContext())
        {
            context.Files.Add(new FileEntity
            {
                Name = "entity.txt", Size = 42, ModifiedUtc = DateTime.UtcNow,
                ShareLinks = [new ShareLinkEntity { Token = "entity-link", RemainingDownloads = 3 }]
            });
            await context.SaveChangesAsync();
        }
        await using var query = site.CreateContext();
        var link = await query.ShareLinks.Include(link => link.File).SingleAsync();
        Assert.Equal("entity.txt", link.File.Name);
        Assert.Equal(42, link.File.Size);
        Assert.NotEqual(typeof(FilesApiController).Assembly, typeof(ApplicationDbContext).Assembly);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("0 --invalid")]
    public async Task Unknown_rollback_target_leaves_rows_and_history_unchanged(string target)
    {
        using var site = new TestSite();
        site.WriteFile("keep.txt", "keep");
        site.Migrate();

        var result = await RunDatabaseCommand(site, "rollback", "--to", target);

        Assert.NotEqual(0, result.ExitCode);
        await using var context = site.CreateContext();
        Assert.Single(await context.Files.ToListAsync());
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Rollback_refuses_a_target_that_would_apply_migrations()
    {
        using var site = new TestSite();
        site.Migrate();
        await using (var context = site.CreateContext())
        {
            var initial = context.Database.GetMigrations().Single();
            Assert.Equal(0, (await RunDatabaseCommand(site, "rollback", "--to", "0")).ExitCode);
            var result = await RunDatabaseCommand(site, "rollback", "--to", initial);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("cannot apply", result.Output);
        }
    }

    [Fact]
    public async Task Explicit_migrate_exits_without_starting_HTTP()
    {
        using var site = new TestSite();
        site.WriteFile("existing.txt", "content");

        var result = await RunDatabaseCommand(site, "migrate");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain("Now listening", result.Output);
        using var connection = site.OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM Files";
        Assert.Equal("existing.txt", command.ExecuteScalar());
    }

    [Fact]
    public async Task Status_does_not_create_a_database_and_rollback_requires_an_explicit_target()
    {
        using var site = new TestSite();
        var status = await RunDatabaseCommand(site, "status");
        Assert.Equal(0, status.ExitCode);
        Assert.Contains("does not exist", status.Output);
        var rollback = await RunDatabaseCommand(site, "rollback");
        Assert.Equal(2, rollback.ExitCode);
        Assert.Contains("--to", rollback.Output);
        Assert.False(File.Exists(site.DatabasePath));
    }

    internal static async Task<(int ExitCode, string Output)> RunDatabaseCommand(TestSite site, params string[] command)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(FilesApiController).Assembly.Location);
        start.ArgumentList.Add("database");
        foreach (var argument in command)
            start.ArgumentList.Add(argument);
        foreach (var argument in new[] { "--contentRoot", site.RootPath, "--Database:Path", site.DatabasePath, "--environment", "Testing", "--urls", "http://127.0.0.1:0" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            return (-1, "Database command did not terminate.\n" + await output + await error);
        }
        return (process.ExitCode, await output + await error);
    }
}
