using Khrenkov.top.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Khrenkov.top.Data;

public static class FileCatalogSynchronization
{
    public static async Task ApplyAsync(ApplicationDbContext database, string uploadsPath)
    {
        var disk = Directory.EnumerateFiles(uploadsPath).Select(path => new FileInfo(path))
            .Where(file => !string.IsNullOrWhiteSpace(file.Name) && !file.Name.Equals("_shares.json", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(file => file.Name, StringComparer.Ordinal);
        await using var transaction = await database.Database.BeginTransactionAsync();
        var records = await database.Files.ToListAsync();
        foreach (var record in records)
        {
            if (!disk.Remove(record.Name, out var file))
                database.Files.Remove(record);
            else
            {
                record.Size = file.Length;
                record.ModifiedUtc = file.LastWriteTimeUtc;
            }
        }
        foreach (var file in disk.Values)
            database.Files.Add(new FileEntity { Name = file.Name, Size = file.Length, ModifiedUtc = file.LastWriteTimeUtc });
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        database.ChangeTracker.Clear();
    }
}
