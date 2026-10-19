using System.Globalization;
using Khrenkov.top.Data;
using Khrenkov.top.Models;

namespace Khrenkov.top.Services
{
    public sealed class ShareLinkService(ApplicationDatabase database)
    {
        public ShareLink CreateLink(string fileName, int maxDownloads)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxDownloads, 1);
            var link = new ShareLink { FileName = fileName, RemainingDownloads = maxDownloads };
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ShareLinks (Token, FileId, RemainingDownloads, CreatedAtUtc)
                SELECT $token, Id, $remaining, $created FROM Files WHERE Name = $fileName;
                """;
            command.Parameters.AddWithValue("$token", link.Token);
            command.Parameters.AddWithValue("$fileName", fileName);
            command.Parameters.AddWithValue("$remaining", maxDownloads);
            command.Parameters.AddWithValue("$created", link.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            if (command.ExecuteNonQuery() != 1)
                throw new FileNotFoundException("File metadata was not found in the database.", fileName);
            return link;
        }

        public (bool Success, string? FileName) TryConsume(string token)
        {
            using var connection = database.OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT f.Name FROM ShareLinks AS s JOIN Files AS f ON f.Id = s.FileId
                WHERE s.Token = $token AND s.RemainingDownloads > 0;
                """;
            command.Parameters.AddWithValue("$token", token);
            var fileName = command.ExecuteScalar() as string;
            if (fileName is null || !File.Exists(Path.Combine(database.UploadsPath, fileName)))
                return (false, null);

            command.CommandText = "UPDATE ShareLinks SET RemainingDownloads = RemainingDownloads - 1 WHERE Token = $token AND RemainingDownloads > 0";
            if (command.ExecuteNonQuery() != 1)
                return (false, null);
            command.CommandText = "DELETE FROM ShareLinks WHERE Token = $token AND RemainingDownloads = 0";
            command.ExecuteNonQuery();
            transaction.Commit();
            return (true, fileName);
        }
    }
}
