using Khrenkov.top.Data;

namespace Khrenkov.top.Services;

// Filesystem coordination only: database queries belong to the scoped DbContext.
public sealed class FileStorage(StoragePaths paths) : IDisposable
{
    public string UploadsPath => paths.UploadsPath;
    public SemaphoreSlim MutationLock { get; } = new(1, 1);

    public static bool IsUserFileName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name is not "." and not ".."
        && !name.Contains('/') && !name.Contains('\\')
        && !name.Equals("_shares.json", StringComparison.OrdinalIgnoreCase);

    public string? ResolveExistingFileName(string fileName)
    {
        if (!IsUserFileName(fileName))
            return null;
        if (!OperatingSystem.IsWindows())
            return File.Exists(Path.Combine(UploadsPath, fileName)) ? fileName : null;
        return Directory.EnumerateFiles(UploadsPath).Select(Path.GetFileName)
            .FirstOrDefault(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() => MutationLock.Dispose();
}
