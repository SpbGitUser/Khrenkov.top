using System.Text.Json;
using Khrenkov.top.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Khrenkov.top.Data;

internal static class LegacyDataImporter
{
    public static async Task ApplyAsync(ApplicationDbContext database, string uploadsPath, ILogger logger)
    {
        await using var transaction = await database.Database.BeginTransactionAsync();
        if (await database.DataMigrations.AnyAsync(migration => migration.Version == 2))
            return;

        var files = await database.Files.ToDictionaryAsync(file => file.Name);
        var diskNames = new Dictionary<string, FileEntity>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(uploadsPath))
        {
            var file = new FileInfo(path);
            if (file.Name.Equals("_shares.json", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!files.TryGetValue(file.Name, out var entity))
            {
                entity = new FileEntity { Name = file.Name };
                database.Files.Add(entity);
            }
            entity.Size = file.Length;
            entity.ModifiedUtc = file.LastWriteTimeUtc;
            diskNames.Add(file.Name, entity);
        }

        var count = 0;
        var skipped = 0;
        var jsonPath = Path.Combine(uploadsPath, "_shares.json");
        if (File.Exists(jsonPath))
        {
            var links = JsonSerializer.Deserialize<List<LegacyShareLink>>(await File.ReadAllTextAsync(jsonPath))
                ?? throw new InvalidDataException("The legacy share store must contain a JSON array.");
            foreach (var link in links)
            {
                if (link is null || string.IsNullOrWhiteSpace(link.Token) || string.IsNullOrWhiteSpace(link.FileName)
                    || link.FileName.Contains('/') || link.FileName.Contains('\\') || link.CreatedAt == default)
                    throw new InvalidDataException("The legacy share store contains an invalid link. Correct it and run database migrate again.");
                if (link.RemainingDownloads <= 0 || !diskNames.TryGetValue(link.FileName, out var file))
                {
                    skipped++;
                    continue;
                }
                database.ShareLinks.Add(new ShareLinkEntity
                {
                    Token = link.Token, File = file, RemainingDownloads = link.RemainingDownloads,
                    CreatedAtUtc = link.CreatedAt.ToUniversalTime()
                });
                count++;
            }
        }
        if (!await database.DataMigrations.AnyAsync(migration => migration.Version == 1))
            database.DataMigrations.Add(new DataMigration { Version = 1, Name = "InitialSchema" });
        database.DataMigrations.Add(new DataMigration { Version = 2, Name = "ImportExistingFiles" });
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        database.ChangeTracker.Clear();
        logger.LogInformation("Imported {Files} existing files and {Links} links; skipped {Skipped} expired or orphan links", diskNames.Count, count, skipped);
    }

    private sealed record LegacyShareLink(string? Token, string? FileName, int RemainingDownloads, DateTime CreatedAt);
}
