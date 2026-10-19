using System.Globalization;
using Khrenkov.top.Data;
using Khrenkov.top.Models;
using Microsoft.Data.Sqlite;

namespace Khrenkov.top.Services;

public sealed class FileCatalogService(ApplicationDatabase database) : IDisposable
{
    private readonly SemaphoreSlim _mutations = new(1, 1);

    public static bool IsUserFileName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name is not "." and not ".."
        && !name.Contains('/') && !name.Contains('\\')
        && !name.Equals("_shares.json", StringComparison.OrdinalIgnoreCase);

    public List<StoredFile> List()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, Size, ModifiedUtc FROM Files ORDER BY Name";
        using var reader = command.ExecuteReader();
        var files = new List<StoredFile>();
        while (reader.Read())
            files.Add(new StoredFile(reader.GetString(0), reader.GetInt64(1), DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        return files;
    }

    public string? ResolveExistingFileName(string fileName)
    {
        if (!IsUserFileName(fileName))
            return null;
        if (!OperatingSystem.IsWindows())
            return File.Exists(Path.Combine(database.UploadsPath, fileName)) ? fileName : null;

        // Store the actual disk spelling, while preserving Windows' case-insensitive lookups.
        return Directory.EnumerateFiles(database.UploadsPath).Select(Path.GetFileName)
            .FirstOrDefault(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<StoredFile> CompleteUploadAsync(string temporaryPath, string fileName, CancellationToken cancellationToken)
    {
        if (!IsUserFileName(fileName))
            throw new ArgumentException("Invalid user file name.", nameof(fileName));
        await _mutations.WaitAsync(cancellationToken);
        try
        {
            fileName = ResolveExistingFileName(fileName) ?? fileName;
            File.Move(temporaryPath, Path.Combine(database.UploadsPath, fileName), overwrite: true);
            return RegisterFile(fileName);
        }
        finally
        {
            _mutations.Release();
        }
    }

    public async Task<bool> DeleteFileAsync(string fileName, CancellationToken cancellationToken)
    {
        if (!IsUserFileName(fileName))
            return false;
        await _mutations.WaitAsync(cancellationToken);
        try
        {
            var actualFileName = ResolveExistingFileName(fileName);
            if (actualFileName is null)
                return false;
            var path = Path.Combine(database.UploadsPath, actualFileName);
            File.Delete(path);
            DeleteMetadata(actualFileName);
            return true;
        }
        finally
        {
            _mutations.Release();
        }
    }

    private StoredFile RegisterFile(string fileName)
    {
        var file = new FileInfo(Path.Combine(database.UploadsPath, fileName));
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Upsert(connection, transaction, file);
        transaction.Commit();
        return new StoredFile(file.Name, file.Length, file.LastWriteTimeUtc);
    }

    private void DeleteMetadata(string fileName)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Files WHERE Name = $name";
        command.Parameters.AddWithValue("$name", fileName);
        command.ExecuteNonQuery();
    }

    public void SynchronizeWithDisk()
    {
        // Repair metadata after an interrupted filesystem/DB update, before requests start.
        var files = Directory.EnumerateFiles(database.UploadsPath).Select(path => new FileInfo(path))
            .Where(file => IsUserFileName(file.Name)).ToDictionary(file => file.Name, StringComparer.Ordinal);
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var file in files.Values)
            Upsert(connection, transaction, file);

        var missing = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT Name FROM Files";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (!files.ContainsKey(reader.GetString(0)))
                    missing.Add(reader.GetString(0));
        }
        foreach (var name in missing)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM Files WHERE Name = $name";
            command.Parameters.AddWithValue("$name", name);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void Upsert(SqliteConnection connection, SqliteTransaction transaction, FileInfo file)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Files (Name, Size, ModifiedUtc) VALUES ($name, $size, $modified)
            ON CONFLICT(Name) DO UPDATE SET Size = excluded.Size, ModifiedUtc = excluded.ModifiedUtc;
            """;
        command.Parameters.AddWithValue("$name", file.Name);
        command.Parameters.AddWithValue("$size", file.Length);
        command.Parameters.AddWithValue("$modified", file.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public void Dispose() => _mutations.Dispose();
}
