using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Khrenkov.top.Data.Migrations;

internal static class ImportExistingFiles
{
    public static void Apply(SqliteConnection connection, SqliteTransaction transaction, string uploadsPath, ILogger logger)
    {
        var fileCount = 0;
        var fileNames = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(uploadsPath))
        {
            var file = new FileInfo(path);
            if (file.Name.Equals("_shares.json", StringComparison.OrdinalIgnoreCase))
                continue;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO Files (Name, Size, ModifiedUtc) VALUES ($name, $size, $modified)";
            command.Parameters.AddWithValue("$name", file.Name);
            command.Parameters.AddWithValue("$size", file.Length);
            command.Parameters.AddWithValue("$modified", file.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
            fileNames.Add(file.Name, file.Name);
            fileCount++;
        }

        var linksPath = Path.Combine(uploadsPath, "_shares.json");
        var linkCount = 0;
        var skippedCount = 0;
        if (File.Exists(linksPath))
        {
            var links = JsonSerializer.Deserialize<List<LegacyShareLink>>(File.ReadAllText(linksPath))
                ?? throw new InvalidDataException("The legacy share store must contain a JSON array.");
            foreach (var link in links)
            {
                if (link is null || string.IsNullOrWhiteSpace(link.Token) || string.IsNullOrWhiteSpace(link.FileName)
                    || link.FileName.Contains('/') || link.FileName.Contains('\\') || link.CreatedAt == default)
                    throw new InvalidDataException("The legacy share store contains an invalid link. Restore or correct the JSON before retrying.");

                if (link.RemainingDownloads <= 0 || !fileNames.TryGetValue(link.FileName, out var actualFileName))
                {
                    skippedCount++;
                    continue;
                }

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO ShareLinks (Token, FileId, RemainingDownloads, CreatedAtUtc)
                    SELECT $token, Id, $remaining, $created FROM Files WHERE Name = $fileName;
                    """;
                command.Parameters.AddWithValue("$token", link.Token);
                command.Parameters.AddWithValue("$fileName", actualFileName);
                command.Parameters.AddWithValue("$remaining", link.RemainingDownloads);
                command.Parameters.AddWithValue("$created", link.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                if (command.ExecuteNonQuery() == 1)
                    linkCount++;
                else
                    skippedCount++;
            }
        }
        logger.LogInformation("Imported {FileCount} existing files and {LinkCount} share links; skipped {SkippedCount} expired or orphan links", fileCount, linkCount, skippedCount);
    }

    private sealed record LegacyShareLink(string? Token, string? FileName, int RemainingDownloads, DateTime CreatedAt);
}
